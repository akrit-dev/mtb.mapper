using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Mtb.Mapper.Core;

public static class EmitHelper
{
    //  Кэш: элемент T  →  уже готовый делегат ForEach<T>
    private static readonly ConcurrentDictionary<
        Type,
        Action<
            IlGeneratorLogWrapper,
            Action<IlGeneratorLogWrapper>,
            Action<IlGeneratorLogWrapper, LocalBuilder, LocalBuilder>>> ForEachCache = new();
    
    public static void EmitDebugWriteLine(this IlGeneratorLogWrapper il, string msg)
    {
        /*
        // Загрузка сообщения на стек
        il.Emit(OpCodes.Ldstr, msg);

        // Вызов Console.WriteLine
        var writeLineMethod = typeof(Console).GetMethod(
            "WriteLine",
            [typeof(string)]
        );

        il.Emit(OpCodes.Call, writeLineMethod);*/
    }

    /// <summary>
    /// Печатает в консоль форматированную строку с одним аргументом – значением локальной переменной.
    /// </summary>
    public static void EmitDebugWriteLine(this IlGeneratorLogWrapper il, string msg, LocalBuilder local)
    {
        /*
        // Подготовим формат: "msg: {0}"
        il.Emit(OpCodes.Ldstr, msg + ": {0}");
    
        // Положим в стек переменную
        il.Emit(OpCodes.Ldloc, local);
        // Если это value-type — запакуем в object
        if (local.LocalType.IsValueType)
            il.Emit(OpCodes.Box, local.LocalType);
    
        // Вызов WriteLine(string format, object arg0)
        var writeLineFmt = typeof(Console).GetMethod(
            "WriteLine",
            new[] { typeof(string), typeof(object) }
        )!;
        il.EmitCall(OpCodes.Call, writeLineFmt, null);
        */
    }
    
    /// <summary>
    /// Выводит в консоль текущую глубину стека, отслеживаемую IlGeneratorLogWrapper.
    /// </summary>
    public static void EmitDebugStack(this IlGeneratorLogWrapper il)
    {
        //Console.WriteLine($"[IL DEBUG] Stack depth = {il.CurrentStackDepth}");
    }
    
    /// <summary>
    ///     Emits a for-loop from <paramref name="start" /> (inclusive) to <paramref name="end" /> (exclusive),
    ///     with a custom <paramref name="step" />. The <paramref name="body" /> action receives the ILGenerator and the loop
    ///     index local.
    /// </summary>
    public static void For(this IlGeneratorLogWrapper il, int start, int end, int step,
        Action<IlGeneratorLogWrapper, LocalBuilder> body)
    {
        var index = il.DeclareLocal(typeof(int));
        // index = start
        il.Emit(OpCodes.Ldc_I4, start);
        il.Emit(OpCodes.Stloc, index);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (step > 0) index >= end else index <= end
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4, end);
        il.Emit(step > 0 ? OpCodes.Bge : OpCodes.Ble, loopEnd);

        body(il, index);

        // index += step
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4, step);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
    }

    /// <summary>
    ///     Emits a foreach-style loop over an array local. Body receives ILGenerator and the current element local.
    /// </summary>
    public static void ForEachArray<T>(this IlGeneratorLogWrapper il, LocalBuilder arrayLocal,
        Action<IlGeneratorLogWrapper, LocalBuilder> body)
    {
        var length = il.DeclareLocal(typeof(int));
        var index = il.DeclareLocal(typeof(int));

        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, length);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, loopEnd);

        // Load element: support value and reference types
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, index);
        if (typeof(T).IsValueType)
        {
            // For value types: get address then load
            il.Emit(OpCodes.Ldelema, typeof(T));
            il.Emit(OpCodes.Ldobj, typeof(T));
        }
        else
        {
            il.Emit(OpCodes.Ldelem_Ref);
        }

        var element = il.DeclareLocal(typeof(T));
        il.Emit(OpCodes.Stloc, element);
        body(il, element);

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits a foreach-style loop over an IEnumerable T.
    /// Body receives ILGenerator and the current element local.
    /// Automatically disposes the enumerator if IDisposable.
    /// </summary>
    public static void ForEach<T>(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> loadEnumerable,
        Action<IlGeneratorLogWrapper, LocalBuilder> body)
    {
        var enumeratorLocal = il.DeclareLocal(typeof(IEnumerator<T>));
        loadEnumerable(il);
        Call(il, typeof(IEnumerable<T>).GetMethod("GetEnumerator"), isVirtual: true);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        
        il.TryFinally(
            innerIl =>
            {
                var loopStart = innerIl.DefineLabel();
                var loopEnd = innerIl.DefineLabel();

                innerIl.Emit(OpCodes.Br, loopEnd);
                innerIl.MarkLabel(loopStart);

                innerIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                Call(innerIl, typeof(IEnumerator<T>).GetProperty("Current")?.GetGetMethod(), isVirtual: true);
                var element = innerIl.DeclareLocal(typeof(T));
                innerIl.Emit(OpCodes.Stloc, element);
                body(innerIl, element);

                innerIl.MarkLabel(loopEnd);
                innerIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                Call(innerIl, typeof(IEnumerator).GetMethod("MoveNext"), isVirtual: true);
                innerIl.Emit(OpCodes.Brtrue, loopStart);
            },
            finallyIl =>
            {
                finallyIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                Call(finallyIl, typeof(IDisposable).GetMethod("Dispose"), isVirtual: true);
            }
        );
    }
    
    /// <summary>
    /// Emits a foreach-style loop over an IEnumerable T given as Type at runtime,
    /// providing both element and index.
    /// </summary>

    public static void ForEach(
        this IlGeneratorLogWrapper il,
        Type elementType,
        Action<IlGeneratorLogWrapper> loadEnumerable,
        Action<IlGeneratorLogWrapper, LocalBuilder, LocalBuilder> bodyWithIndex)
    {
        // Попробовать взять делегат из кэша
        var forEachDelegate = ForEachCache.GetOrAdd(elementType, t =>
        {
            var template = typeof(EmitHelper)
                .GetMethods(BindingFlags.Public|BindingFlags.Static)
                .Where(m => m is { Name: nameof(ForEach), IsGenericMethodDefinition: true })
                .Single(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == 3
                           && ps[1].ParameterType == typeof(Action<IlGeneratorLogWrapper>)
                           && ps[2].ParameterType.IsGenericType
                           && ps[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<,,>);
                })
                .MakeGenericMethod(t);
            
            var delegateType = typeof(Action<
                IlGeneratorLogWrapper,
                Action<IlGeneratorLogWrapper>,
                Action<IlGeneratorLogWrapper, LocalBuilder, LocalBuilder>>);

            return (Action<IlGeneratorLogWrapper, Action<IlGeneratorLogWrapper>, Action<IlGeneratorLogWrapper,LocalBuilder,LocalBuilder>>)
                template.CreateDelegate(delegateType);
        });

        // Викнуть делегат
        forEachDelegate(il, loadEnumerable, bodyWithIndex);
    }

    /// <summary>
    /// Emits a foreach-style loop over an IEnumerable T, providing both element and index.
    /// Automatically disposes the enumerator if IDisposable.
    /// </summary>
    public static void ForEach<T>(
        this IlGeneratorLogWrapper il,
        Action<IlGeneratorLogWrapper> loadEnumerable,
        Action<IlGeneratorLogWrapper, LocalBuilder /* element */, LocalBuilder /* index */> bodyWithIndex)
    {
        // локаль для IEnumerator<T>
        var enumeratorLocal = il.DeclareLocal(typeof(IEnumerator<T>));
        // локаль для индекса
        var indexLocal = il.DeclareLocal(typeof(int));

        // 1) load enumerable and call GetEnumerator()
        loadEnumerable(il);
        il.Call(typeof(IEnumerable).GetMethod("GetEnumerator")!, isVirtual: true);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // 2) index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // 3) try/finally для корректного Dispose()
        il.TryFinally(
            innerIl =>
            {
                var loopStart = innerIl.DefineLabel();
                var loopCheck = innerIl.DefineLabel();

                // сразу прыгаем на проверку MoveNext
                innerIl.Emit(OpCodes.Br, loopCheck);

                // метка начала тела цикла
                innerIl.MarkLabel(loopStart);

                // получаем Current
                innerIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                innerIl.Call(typeof(IEnumerator<T>).GetProperty("Current")!.GetGetMethod()!, isVirtual: true);
                var elementLocal = innerIl.DeclareLocal(typeof(T));
                innerIl.Emit(OpCodes.Stloc, elementLocal);

                // вызываем тело с элементом и индексом
                bodyWithIndex(innerIl, elementLocal, indexLocal);

                // ++index
                innerIl.Emit(OpCodes.Ldloc, indexLocal);
                innerIl.Emit(OpCodes.Ldc_I4_1);
                innerIl.Emit(OpCodes.Add);
                innerIl.Emit(OpCodes.Stloc, indexLocal);

                // проверка MoveNext
                innerIl.MarkLabel(loopCheck);
                innerIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                innerIl.Call(typeof(IEnumerator).GetMethod("MoveNext")!, isVirtual: true);
                innerIl.Emit(OpCodes.Brtrue, loopStart);
            },
            finallyIl =>
            {
                // в finally — Dispose()
                finallyIl.Emit(OpCodes.Ldloc, enumeratorLocal);
                finallyIl.Call(typeof(IDisposable).GetMethod("Dispose")!, isVirtual: true);
            }
        );
    }

    /// <summary>
    ///     Emits a while-loop based on <paramref name="emitCondition" />. Loop continues while condition is true.
    /// </summary>
    public static void While(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> emitCondition,
        Action<IlGeneratorLogWrapper> emitBody)
    {
        var start = il.DefineLabel();
        var end = il.DefineLabel();

        il.MarkLabel(start);
        emitCondition(il); // push int32
        il.Emit(OpCodes.Brfalse, end);
        emitBody(il);
        il.Emit(OpCodes.Br, start);
        il.MarkLabel(end);
    }

    /// <summary>
    ///     Emits a do-while loop: body executes at least once and repeats while condition is true.
    /// </summary>
    public static void DoWhile(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> emitBody,
        Action<IlGeneratorLogWrapper> emitCondition)
    {
        var start = il.DefineLabel();

        il.MarkLabel(start);
        emitBody(il);
        emitCondition(il);
        il.Emit(OpCodes.Brtrue, start);
    }

    /// <summary>
    ///     Emits an if-then-else based on <paramref name="emitCondition" />. Condition must push int32 (0=false).
    /// </summary>
    public static void If(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> emitCondition,
        Action<IlGeneratorLogWrapper> emitThen,
        Action<IlGeneratorLogWrapper>? emitElse = null)
    {
        emitCondition(il);
        var elseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Brfalse, elseLabel);
        emitThen(il);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(elseLabel);
        emitElse?.Invoke(il);
        il.MarkLabel(endLabel);
    }

    /// <summary>
    ///     Emits a switch/case for integer values. Cases map to actions; defaultCase is optional.
    /// </summary>
    public static void Switch(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> emitValue,
        IDictionary<int, Action<IlGeneratorLogWrapper>> cases,
        Action<IlGeneratorLogWrapper>? defaultCase = null)
    {
        // Evaluate switch value
        emitValue(il);
        var end = il.DefineLabel();

        foreach (var kvp in cases)
        {
            var caseLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, kvp.Key);
            il.Emit(OpCodes.Bne_Un, caseLabel);
            il.Emit(OpCodes.Pop);
            kvp.Value(il);
            il.Emit(OpCodes.Br, end);
            il.MarkLabel(caseLabel);
        }

        // default
        il.Emit(OpCodes.Pop);
        defaultCase?.Invoke(il);

        il.MarkLabel(end);
    }

    /// <summary>
    ///     Emits a try/finally block.
    /// </summary>
    public static void TryFinally(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> tryBody,
        Action<IlGeneratorLogWrapper> finallyBody)
    {
        il.BeginExceptionBlock();
        tryBody(il);
        il.BeginFinallyBlock();
        finallyBody(il);
        il.EndExceptionBlock();
    }

    /// <summary>
    ///     Emits a try/catch block for <typeparamref name="TException" />. Catch body receives the exception local.
    /// </summary>
    public static void TryCatch<TException>(this IlGeneratorLogWrapper il, Action<IlGeneratorLogWrapper> tryBody,
        Action<IlGeneratorLogWrapper, LocalBuilder> catchBody)
        where TException : Exception
    {
        il.BeginExceptionBlock();
        tryBody(il);
        il.BeginCatchBlock(typeof(TException));
        var exLocal = il.DeclareLocal(typeof(TException));
        il.Emit(OpCodes.Stloc, exLocal);
        catchBody(il, exLocal);
        il.EndExceptionBlock();
    }

    /// <summary>
    ///     Emits an integer constant onto the evaluation stack.
    /// </summary>
    public static void LoadInt(this IlGeneratorLogWrapper il, int value) 
        => il.Emit(OpCodes.Ldc_I4, value);

    /// <summary>
    ///     Emits a call or callvirt instruction for the specified method.
    /// </summary>
    public static void Call(this IlGeneratorLogWrapper il, MethodInfo? method, Type[]? parameterTypes = null,
        bool isVirtual = false) 
        => il.EmitCall(isVirtual ? OpCodes.Callvirt : OpCodes.Call, method, parameterTypes);
}