using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Rework;

/// <summary>
/// Helpers de construcción de IL para transpilers amigables ([ReworkPatch]).
/// Prepatcher ofrece Prefix/Postfix; en Rework el autor escribe un transpiler —
/// estos helpers hacen que los casos comunes se escriban sin pelear con el IL:
///
///   - EmitCallAtStart / EmitCallAtEnd: llamar tu helper C# al inicio/fin del método.
///   - Ldc / OpCode sugar: constantes del tipo correcto (Ldc_I4, Ldc_R4, Ldstr, Ldnull...).
///   - Ldarg: cargar 'this' o cualquier parámetro por índice.
///   - Starg: escribir un parámetro (equivalente a un "postfix con resultado").
///   - ImportMethod / ImportType: importar al módulo destino (vía reflexión o por nombre).
///   - AppendFirst / AppendLast: ensamblar el resultado.
///
/// Regla de oro del transpiler (ver ERRORES.md): NO borrar instrucciones que sean
/// límite de regiones try/catch; si las borras, Rework detecta el daño y descarta ese
/// manejador con aviso en vez de emitir IL corrupto.
/// </summary>
public static class TranspilerHelpers
{
    // ---------- Constantear (tipos comunes) ----------

    public static Instruction Ldc(int value) => Instruction.Create(OpCodes.Ldc_I4, value);
    public static Instruction Ldc(long value) => Instruction.Create(OpCodes.Ldc_I8, value);
    public static Instruction Ldc(float value) => Instruction.Create(OpCodes.Ldc_R4, value);
    public static Instruction Ldc(double value) => Instruction.Create(OpCodes.Ldc_R8, value);
    public static Instruction Ldstr(string value) => Instruction.Create(OpCodes.Ldstr, value);
    public static Instruction Ldnull() => Instruction.Create(OpCodes.Ldnull);

    /// <summary>Ldc_I4 en su forma corta (Ldc_I4_0..Ldc_I4_8) si aplica, para IL compacto.</summary>
    public static Instruction LdcInt(int value)
    {
        switch (value)
        {
            case 0: return Instruction.Create(OpCodes.Ldc_I4_0);
            case 1: return Instruction.Create(OpCodes.Ldc_I4_1);
            case 2: return Instruction.Create(OpCodes.Ldc_I4_2);
            case 3: return Instruction.Create(OpCodes.Ldc_I4_3);
            case 4: return Instruction.Create(OpCodes.Ldc_I4_4);
            case 5: return Instruction.Create(OpCodes.Ldc_I4_5);
            case 6: return Instruction.Create(OpCodes.Ldc_I4_6);
            case 7: return Instruction.Create(OpCodes.Ldc_I4_7);
            case 8: return Instruction.Create(OpCodes.Ldc_I4_8);
            default: return Instruction.Create(OpCodes.Ldc_I4, value);
        }
    }

    /// <summary>Cargar un argumento por índice, con las formas cortas (Ldarg_0..Ldarg_3).</summary>
    public static Instruction Ldarg(int index)
    {
        switch (index)
        {
            case 0: return Instruction.Create(OpCodes.Ldarg_0);
            case 1: return Instruction.Create(OpCodes.Ldarg_1);
            case 2: return Instruction.Create(OpCodes.Ldarg_2);
            case 3: return Instruction.Create(OpCodes.Ldarg_3);
            default: return Instruction.Create(OpCodes.Ldarg, index);
        }
    }

    /// <summary>Escribir un parámetro por índice (starg, con índice numérico).</summary>
    public static Instruction Starg(int index) => Instruction.Create(OpCodes.Starg, index);

    /// <summary>Escribir un parámetro por su ParameterDefinition (recomendado: el
    /// opcode Starg espera el reference del parámetro, no solo un int).</summary>
    public static Instruction Starg(ParameterDefinition param) => Instruction.Create(OpCodes.Starg, param);

    /// <summary>Call a un método/helper importado.</summary>
    public static Instruction Call(MethodReference method) => Instruction.Create(OpCodes.Call, method);

    /// <summary>Cargar un campo de la instancia (this.campo), dado el FieldReference.</summary>
    public static Instruction Ldfld(FieldReference field) => Instruction.Create(OpCodes.Ldfld, field);

    /// <summary>Ret.</summary>
    public static Instruction Ret() => Instruction.Create(OpCodes.Ret);

    // ---------- Importar métodos/tipos al módulo destino ----------

    /// <summary>Importa un Type de la reflexión de runtime al módulo del transpiler.</summary>
    public static TypeReference ImportType(Type type, ModuleDefinition module)
        => module.ImportReference(type);

    /// <summary>Importa un método estático/instancia de runtime al módulo del transpiler.</summary>
    public static MethodReference ImportMethod(MethodInfo method, ModuleDefinition module)
        => module.ImportReference(method);

    /// <summary>Importa un método de tipo conocido por ruta "Namespace.Tipo" y nombre.</summary>
    public static MethodReference ImportMethod(string typeFullName, string methodName, ModuleDefinition module)
    {
        var t = module.GetType(typeFullName);
        return t?.Methods.FirstOrDefault(m => m.Name == methodName);
    }

    // ---------- Insertar tu helper al inicio / al final ----------

    /// <summary>Crea la secuencia "call Helper()" para inyectar al INICIO del método.</summary>
    public static IEnumerable<Instruction> CallAtStart(MethodReference helper)
    {
        yield return Instruction.Create(OpCodes.Call, helper);
    }

    /// <summary>Crea la secuencia "call Helper()" para inyectar justo ANTES del ret final.</summary>
    public static IEnumerable<Instruction> CallAtEnd(MethodReference helper)
    {
        yield return Instruction.Create(OpCodes.Call, helper);
    }

    // ---------- Ensamblar resultado ----------

    /// <summary>Suscinta lista de instrucciones (a lo LINQ).</summary>
    public static List<Instruction> List(params Instruction[] instrs) => new(instrs);

    /// <summary>Inserta 'toInsert' al INICIO de 'target' (no muta target con refs rotas).</summary>
    public static List<Instruction> Prepend(IEnumerable<Instruction> target, IEnumerable<Instruction> toInsert)
        => toInsert.Concat(target).ToList();
}