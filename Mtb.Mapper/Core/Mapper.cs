using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Mtb.Mapper.Core;

/// <summary>
/// </summary>
/// <typeparam name="TSource"></typeparam>
/// <typeparam name="TTarget"></typeparam>
public static class Mapper<TSource, TTarget> where TTarget : new()
{
    private static Func<TSource, TTarget>? _mapFunc;

    /// <summary>
    /// </summary>
    /// <param name="configAction"></param>
    public static void ConfigureMapping(Action<MappingConfig<TSource, TTarget>>? configAction = null)
    {
        if (_mapFunc is not null) 
            return;
        
        var config = new MappingConfig<TSource, TTarget>();

        // Автоматическое маппирование по совпадающим свойствам (по имени *регистрозависим*)
        AutoMapProperties(config);

        // Применение пользовательских правил маппинга
        configAction?.Invoke(config);

        _mapFunc = CreateMappingFunction(config);
    }

    /// <summary>
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static TTarget Map(TSource source)
    {
        if (_mapFunc == null)
            throw new InvalidOperationException($"Mapping not configured." +
                                                $" Call Mapper<{typeof(TSource).Name}, {typeof(TTarget).Name}>" +
                                                $".ConfigureMapping() first");

        var result = _mapFunc(source);

        return result;
    }

    private static void AutoMapProperties(MappingConfig<TSource, TTarget> config)
    {
        foreach (var targetProperty in typeof(TTarget)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var sourceProperty = typeof(TSource)
                .GetProperty(targetProperty.Name, BindingFlags.Public | BindingFlags.Instance);

            if (sourceProperty == null 
                || !sourceProperty.CanRead 
                || !targetProperty.CanWrite
                || sourceProperty.PropertyType != targetProperty.PropertyType) continue;

            // Используем лямбда-выражения для автоматического маппинга
            var sourceParameter = Expression.Parameter(typeof(TSource));
            var targetParameter = Expression.Parameter(typeof(TTarget));

            var sourceLambda = Expression.Lambda(Expression.Property(sourceParameter, sourceProperty), sourceParameter);

            var targetLambda = Expression.Lambda(Expression.Property(targetParameter, targetProperty),targetParameter);

            // Используем наш метод MapProperty с лямбда-выражениями
            typeof(MappingConfig<TSource, TTarget>)
                .GetMethod(nameof(MappingConfig<TSource, TTarget>.MapProperty))
                ?.MakeGenericMethod(sourceProperty.PropertyType, targetProperty.PropertyType)
                .Invoke(config,
                new object[] 
                {
                    sourceLambda,
                    targetLambda,
                    null /*конвертер*/
                });
        }
    }

    private static Func<TSource, TTarget> CreateMappingFunction(MappingConfig<TSource, TTarget> config)
    {
        var sourceType = typeof(TSource);
        var targetType = typeof(TTarget);

        if ((sourceType.IsValueType || sourceType == typeof(string) || sourceType.IsEnum)
            && (targetType.IsValueType || targetType == typeof(string) || targetType.IsEnum))
            return source => (TTarget)(object)source!;

        var method = new DynamicMethod(
            "Map",
            targetType,
            new[] {sourceType},
            typeof(Mapper<TSource, TTarget>).Module,
            true);

        var ilGen = new IlGeneratorLogWrapper(method.GetILGenerator());

        // declare local for target
        var targetLocal = ilGen.DeclareLocal(targetType);

        // new TTarget(); stloc targetLocal
        var ctor = targetType.GetConstructor(Type.EmptyTypes)
                   ?? throw new InvalidOperationException(
                       $"Target type {targetType} must have a public parameterless constructor.");
        
        ilGen.Emit(OpCodes.Newobj, ctor);
        ilGen.Emit(OpCodes.Stloc, targetLocal);

        // map each property
        MapProperties(sourceType, targetType, ilGen, config, targetLocal);

        // load target and return
        ilGen.Emit(OpCodes.Ldloc, targetLocal);
        ilGen.Emit(OpCodes.Ret);

        /*
        // (optional) лог IL
        File.WriteAllText(
            $@"D:\Projects\mtb\Mtb.Mapper\IlDump{typeof(TSource).Name}-{typeof(TTarget).Name}.txt",
            ilGen.ToString());*/

        return (Func<TSource, TTarget>)method.CreateDelegate(typeof(Func<TSource, TTarget>));
    }

    private static void MapProperties(
        Type sourceType,
        Type targetType,
        IlGeneratorLogWrapper il,
        MappingConfig<TSource, TTarget> config,
        LocalBuilder targetLocal)
    {
        foreach (var targetProp in targetType
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var srcName = config.PropertyMappings.TryGetValue(targetProp.Name, out var mapped)
                ? mapped
                : targetProp.Name;
            var sourceProp = sourceType
                .GetProperty(srcName, BindingFlags.Public | BindingFlags.Instance);

            if (sourceProp == null || !sourceProp.CanRead || !targetProp.CanWrite)
                continue;

            var getMethod = sourceProp.GetGetMethod(true)!;
            var setMethod = targetProp.GetSetMethod(true)!;

            // load target
            il.Emit(OpCodes.Ldloc, targetLocal);
            // load source arg
            il.Emit(OpCodes.Ldarg_0);
            // call source getter
            il.Emit(OpCodes.Callvirt, getMethod);

            // converter or direct or complex
            if (config.Converters.TryGetValue(targetProp.Name, out var converter))
            {
                // value on stack, call converter
                il.Emit(OpCodes.Call, converter.Method);
            }
            else if (IsSimpleType(sourceProp.PropertyType) && sourceProp.PropertyType == targetProp.PropertyType)
            {
                // value already on stack
            }
            else if (IsDictionaryType(sourceProp.PropertyType) && IsDictionaryType(targetProp.PropertyType))
            {
                // stack: [target, dict]
                MapDictionary(il, sourceProp, targetProp, targetLocal);
                continue;
            }
            else if (IsEnumerableType(sourceProp.PropertyType) && IsEnumerableType(targetProp.PropertyType))
            {
                // stack: [target, collection]
                MapCollection(il, sourceProp, targetProp, targetLocal);
                continue;
            }
            else
            {
                // nested object
                var mapperType = typeof(Mapper<,>).MakeGenericType(sourceProp.PropertyType, targetProp.PropertyType);
                var mapMethod = mapperType.GetMethod("Map", BindingFlags.Public | BindingFlags.Static)
                                ?? throw new InvalidOperationException(
                                    $"No nested mapper for {sourceProp.PropertyType} -> {targetProp.PropertyType}");
                il.Emit(OpCodes.Call, mapMethod);
            }

            // set property
            il.Emit(OpCodes.Callvirt, setMethod);
        }
    }

    private static void MapCollection(
        IlGeneratorLogWrapper il,
        PropertyInfo sourceProperty,
        PropertyInfo targetProperty,
        LocalBuilder targetLocal)
    {
        // 1. Определяем типы
        var srcType = sourceProperty.PropertyType;
        var tgtType = targetProperty.PropertyType;
        var srcElementType = GetElementType(srcType)!;
        var targetElementType = GetElementType(tgtType)!;

        // 2. Простые совпадающие типы
        if (IsSimpleType(srcElementType) && srcElementType == targetElementType)
        {
            il.Emit(OpCodes.Callvirt, targetProperty.GetSetMethod(true)!);
            return;
        }

        // 3. Получаем статический Mapper.Map(srcElem → tgtElem)
        var mapperType = typeof(Mapper<,>).MakeGenericType(srcElementType, targetElementType);
        var mapMethod = mapperType.GetMethod("Map", BindingFlags.Public | BindingFlags.Static)
                        ?? throw new InvalidOperationException($"No mapper for {srcElementType}→{targetElementType}");

        // 4. Локали: исходная коллекция, результат, элемент и индекс
        var srcCollVar = il.DeclareLocal(srcType);
        var resultVar = il.DeclareLocal(tgtType);
        // индекс внутри Foreach

        // 5. Сохраняем аргументы
        il.Emit(OpCodes.Stloc, srcCollVar);
        il.Emit(OpCodes.Stloc, targetLocal);

        var notNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // 6. Проверка null
        il.Emit(OpCodes.Ldloc, srcCollVar);
        il.Emit(OpCodes.Brtrue, notNullLabel);

        // 7. Null-ветка: target.Prop = null
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, targetProperty.GetSetMethod(true)!);
        il.Emit(OpCodes.Br, endLabel);

        // 8. notNull-ветка: создаём пустую коллекцию
        il.MarkLabel(notNullLabel);
        if (tgtType.IsArray)
        {
            // 8.1. Новый массив нужной длины
            il.Emit(OpCodes.Ldloc, srcCollVar);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Newarr, targetElementType);
        }
        else
        {
            // 8.2. Новый List<T>
            var listCtor = tgtType.GetConstructor(Type.EmptyTypes)!;
            il.Emit(OpCodes.Newobj, listCtor);
        }

        il.Emit(OpCodes.Stloc, resultVar);
        
        // 9. Проход по srcCollVar с индексом
        il.ForEach(
            srcElementType,
            loopIl => loopIl.Emit(OpCodes.Ldloc, srcCollVar),
            (loopIl, elemLocal, indexLocal) =>
            {
                if (tgtType.IsArray)
                {
                    // вызов Mapper.Map(elemLocal)
                    //loopIl.Emit(OpCodes.Ldloc, elemLocal);
                    //loopIl.Call(mapMethod);
                    // resultVar[index] = mapped
                    loopIl.Emit(OpCodes.Ldloc, resultVar);
                    loopIl.Emit(OpCodes.Ldloc, indexLocal);
                    loopIl.Emit(OpCodes.Stelem, targetElementType);
                }
                else
                {
                    loopIl.Emit(OpCodes.Ldloc, resultVar);      // [ list ]
                    loopIl.Emit(OpCodes.Ldloc, elemLocal);      // [ list, elem ]
                    loopIl.Call(mapMethod);                     // [ list, mapped ]

                    var addMethod = tgtType.GetMethod(
                        "Add",
                        new[] {targetElementType}
                    )!;
                    loopIl.Emit(OpCodes.Callvirt, addMethod);   // []
                }
            }
        );

        // 10. Установка свойства: target.Prop = результат
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldloc, resultVar);
        il.Emit(OpCodes.Callvirt, targetProperty.GetSetMethod(true)!);

        il.MarkLabel(endLabel);
    }

    private static void MapDictionary(
        IlGeneratorLogWrapper il,
        PropertyInfo sourceProperty,
        PropertyInfo targetProperty,
        LocalBuilder targetLocal)
    {
        il.EmitDebugWriteLine($"[MapDictionary] Start for {sourceProperty.PropertyType.FullName} -> {targetProperty.PropertyType.FullName}");

        var srcDictType = sourceProperty.PropertyType;
        var tgtDictType = targetProperty.PropertyType;


        var srcPairType = typeof(KeyValuePair<,>)
            .MakeGenericType(GetDictionaryKeyType(srcDictType)!, GetDictionaryValueType(srcDictType)!);


        var tgtKeyType = GetDictionaryKeyType(tgtDictType)!;
        var tgtValueType = GetDictionaryValueType(tgtDictType)!;


        MethodInfo? mapKeyMethod = null, mapValueMethod = null;
        if (!IsSimpleType(srcPairType.GetGenericArguments()[0]) || srcPairType.GetGenericArguments()[0] != tgtKeyType)
        {
            var mapperKeyType = typeof(Mapper<,>).MakeGenericType(srcPairType.GetGenericArguments()[0], tgtKeyType);

            mapKeyMethod = mapperKeyType.GetMethod("Map", BindingFlags.Public | BindingFlags.Static)
                           ?? throw new InvalidOperationException($"No mapper for key {srcPairType}→{tgtKeyType}");
        }

        if (!IsSimpleType(srcPairType.GetGenericArguments()[1]) || srcPairType.GetGenericArguments()[1] != tgtValueType)
        {
            var mapperValType = typeof(Mapper<,>).MakeGenericType(srcPairType.GetGenericArguments()[1], tgtValueType);
            
            mapValueMethod = mapperValType.GetMethod("Map", BindingFlags.Public | BindingFlags.Static)
                             ?? throw new InvalidOperationException(
                                 $"No mapper for value {srcPairType}→{tgtValueType}");
        }


        var srcVar = il.DeclareLocal(srcDictType);
        var resultVar = il.DeclareLocal(tgtDictType);


        il.Emit(OpCodes.Stloc, srcVar);
        il.Emit(OpCodes.Stloc, targetLocal);

        var lblNotNull = il.DefineLabel();
        var lblEnd = il.DefineLabel();


        il.Emit(OpCodes.Ldloc, srcVar);
        il.Emit(OpCodes.Brtrue, lblNotNull);


        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, targetProperty.GetSetMethod(true)!);
        il.Emit(OpCodes.Br, lblEnd);


        il.MarkLabel(lblNotNull);

        
        var ctor = tgtDictType.GetConstructor(Type.EmptyTypes)
                   ?? throw new InvalidOperationException($"No parameterless ctor for {tgtDictType}");
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stloc, resultVar);

        il.ForEach(
            srcPairType,
            loopIl => loopIl.Emit(OpCodes.Ldloc, srcVar),
            (loopIl, pairLocal, _) =>
            {
                il.EmitDebugWriteLine("[MapDictionary] Start", pairLocal);
                // 1) this: словарь
                loopIl.Emit(OpCodes.Ldloc, resultVar);
                
                loopIl.Emit(OpCodes.Ldloca, pairLocal);
                loopIl.Call(srcPairType.GetProperty("Key")!.GetGetMethod()!);
                
                if (mapKeyMethod != null)
                    loopIl.Call(mapKeyMethod); 
                
                loopIl.Emit(OpCodes.Ldloca, pairLocal);
                loopIl.Call(srcPairType.GetProperty("Value")!.GetGetMethod()!);
                if (mapValueMethod != null)
                    loopIl.Call(mapValueMethod);
                
                var add = tgtDictType.GetMethod(
                    "Add",
                    new[] { tgtKeyType, tgtValueType }
                )! ?? throw new InvalidOperationException(
                    $"Метод Add({tgtKeyType.Name},{tgtValueType.Name}) не найден у {tgtDictType.FullName}"
                );
                loopIl.Call(add, isVirtual:true);
            }
        );
        
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldloc, resultVar);
        il.Emit(OpCodes.Callvirt, targetProperty.GetSetMethod(true)!);

        il.MarkLabel(lblEnd);
    }

    /// <summary>
    ///     TODO поддержка пользовательских стуктур
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static bool IsSimpleType(Type type)
    {
        return type.IsValueType || type == typeof(string) || type.IsEnum;
    }

    /// <summary>
    ///     Проверяет, является ли переданный тип массивом\списком
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static bool IsEnumerableType(Type type)
    {
        return typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
    }

    /// <summary>
    ///     Проверяет, является ли переданный тип словарем
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static bool IsDictionaryType(Type type)
    {
        return type.IsGenericType
               && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                   || type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
    }

    /// <summary>
    ///     Возвращает тип внутреннего элемента коллекции
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition()))
            return type.GetGenericArguments()[0];

        return type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Возвращает TKey для IDictionary&lt;TKey, TValue&gt; или IReadOnlyDictionary&lt;TKey, TValue&gt;.
    /// Если тип не является словарём — возвращает null.
    /// </summary>
    public static Type? GetDictionaryKeyType(Type type)
    {
        // 1) если сам тип — generic IDictionary<,> или IReadOnlyDictionary<,>
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        // 2) ищем среди реализованных интерфейсов
        foreach (var @iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var def = iface.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Возвращает TValue для IDictionary&lt;TKey, TValue&gt; или IReadOnlyDictionary&lt;TKey, TValue&gt;.
    /// Если тип не является словарём — возвращает null.
    /// </summary>
    public static Type? GetDictionaryValueType(Type type)
    {
        // 1) если сам тип — generic IDictionary<,> или IReadOnlyDictionary<,>
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                return type.GetGenericArguments()[1];
            }
        }

        // 2) ищем среди реализованных интерфейсов
        foreach (var @iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var def = iface.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                return iface.GetGenericArguments()[1];
            }
        }

        return null;
    }
}



/// <summary>
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public static class KeySelectorsHolder<TKey, TValue>
{
    /// <summary>
    /// </summary>
    public static readonly Func<KeyValuePair<TKey, TValue>, TKey> KeySelectorDelegate;

    static KeySelectorsHolder()
    {
        KeySelectorDelegate = kvp => kvp.Key;
    }
}

/// <summary>
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TSourceValue"></typeparam>
/// <typeparam name="TTargetValue"></typeparam>
public static class ValueSelectorsHolder<TKey, TSourceValue, TTargetValue> where TTargetValue : new()
{
    public static readonly Func<KeyValuePair<TKey, TSourceValue>, TTargetValue> ValueSelectorDelegate;

    static ValueSelectorsHolder()
    {
        // Если простые и одинаковые (например, string), копируем напрямую
        if (typeof(TSourceValue) == typeof(TTargetValue) &&
            (typeof(TSourceValue).IsValueType || typeof(TSourceValue) == typeof(string)))
            ValueSelectorDelegate = kvp => (TTargetValue)(object)kvp.Value;
        else
            // Иначе маппим через Mapper
            ValueSelectorDelegate = kvp => Mapper<TSourceValue, TTargetValue>.Map(kvp.Value);
    }
}