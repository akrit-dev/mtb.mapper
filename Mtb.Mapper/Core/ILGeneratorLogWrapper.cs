using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace Mtb.Mapper.Core;

/// <summary>
/// 
/// </summary>
/// <param name="ilGenerator"></param>
public class IlGeneratorLogWrapper(ILGenerator ilGenerator)
{
    private readonly StringBuilder _stringBuilder = new();
    
    public override string ToString()
    {
        return _stringBuilder.ToString();
    }
    
    public ILGenerator IlGenerator { get; } = ilGenerator;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="msg"></param>
    public void EmitDebugWriteLine(string msg)
    {
        EmitHelper.EmitDebugWriteLine(this, msg);
    }

    public static void EmitDebugWriteLine(IlGeneratorLogWrapper il, string msg, LocalBuilder local)
    {
        il.EmitDebugWriteLine(msg, local);
    }
    
    public void BeginCatchBlock(Type? exceptionType)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginCatchBlock)} {exceptionType};");
        IlGenerator.BeginCatchBlock(exceptionType);
    }

    public void BeginExceptFilterBlock()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginExceptFilterBlock)};");
        IlGenerator.BeginExceptFilterBlock();
    }

    public Label BeginExceptionBlock()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginExceptionBlock)};");
        return IlGenerator.BeginExceptionBlock();
    }

    public void BeginFaultBlock()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginFaultBlock)};");
        IlGenerator.BeginFaultBlock();
    }

    public void BeginFinallyBlock()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginFinallyBlock)};");
        IlGenerator.BeginFinallyBlock();
    }

    public void BeginScope()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(BeginScope)};");
        IlGenerator.BeginScope();
    }

    public LocalBuilder DeclareLocal(Type localType, bool pinned = false)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(DeclareLocal)}, localType:{localType}, pinned:{pinned};");
        return IlGenerator.DeclareLocal(localType, pinned);
    }
    
    public Label DefineLabel()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(DefineLabel)};");
        return IlGenerator.DefineLabel();
    }

    public void Emit(OpCode opcode)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode};");
        IlGenerator.Emit(opcode);
    }

    public void Emit(OpCode opcode, byte arg)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
        IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, double arg)
    { 
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
        IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, short arg)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
        IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, int arg)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
        IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, long arg)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
       IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, ConstructorInfo con)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, con:{con};");
        IlGenerator.Emit(opcode, con);
    }

    public void Emit(OpCode opcode, Label label)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, label:{label};");
        IlGenerator.Emit(opcode, label);
    }

    public void Emit(OpCode opcode, Label[] labels)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, labels:{labels};");
        IlGenerator.Emit(opcode, labels);
    }

    public void Emit(OpCode opcode, LocalBuilder local)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, local:{local};");
        IlGenerator.Emit(opcode, local);
    }

    public void Emit(OpCode opcode, SignatureHelper signature)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, signature:{signature};");
        IlGenerator.Emit(opcode, signature);
    }

    public void Emit(OpCode opcode, FieldInfo field)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, field:{field};");
        IlGenerator.Emit(opcode, field);
    }

    public void Emit(OpCode opcode, MethodInfo meth)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, meth:{meth};");
        IlGenerator.Emit(opcode, meth);
    }

    public void Emit(OpCode opcode, float arg)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, arg:{arg};");
        IlGenerator.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, string str)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, str:{str};");
        IlGenerator.Emit(opcode, str);
    }

    public void Emit(OpCode opcode, Type cls)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(Emit)}, opcode:{opcode}, cls:{cls};");
        IlGenerator.Emit(opcode, cls);
    }

    public void EmitCall(OpCode opcode, MethodInfo? methodInfo, Type[]? optionalParameterTypes)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(EmitCall)}, opcode:{opcode}, methodInfo:{methodInfo}, optionalParameterTypes:{optionalParameterTypes};");
        IlGenerator.EmitCall(opcode, methodInfo!, optionalParameterTypes);
    }

    public void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes,
        Type[]? optionalParameterTypes)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(EmitCalli)}, opcode:{opcode}, callingConvention:{callingConvention}, returnType:{returnType}, parameterTypes:{parameterTypes}, optionalParameterTypes:{optionalParameterTypes};");
        IlGenerator.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    }

    public void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(EmitCalli)}, opcode:{opcode}, unmanagedCallConv:{unmanagedCallConv}, returnType:{returnType}, parameterTypes:{parameterTypes};");
        IlGenerator.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    }

    public void EndExceptionBlock()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(EndExceptionBlock)};");
        IlGenerator.EndExceptionBlock();
    }

    public void EndScope()
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(EndScope)};");
        IlGenerator.EndScope();
    }

    public void MarkLabel(Label loc)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(MarkLabel)}, loc:{loc};");
        IlGenerator.MarkLabel(loc);
    }

    public void UsingNamespace(string usingNamespace)
    {
        _stringBuilder.AppendLine($"{IlGenerator.ILOffset}: {nameof(UsingNamespace)}, usingNamespace:{usingNamespace};");
        IlGenerator.UsingNamespace(usingNamespace);
    }
    
}