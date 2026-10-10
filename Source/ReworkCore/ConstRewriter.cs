using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Bloque 24.5 — [ReworkConst]: pliega TODOS los <c>ldsfld</c> de un campo
/// <c>static readonly</c> a un valor constante, en todos los ensamblados
/// reescribibles, durante la pasada 1. Es el único momento en que esto es
/// posible: los call-sites de un readonly son lecturas del CAMPO, y aquí se
/// convierten en el literal nuevo. Un const de IL (literal) ya ni siquiera
/// tiene lecturas de campo: el valor está quemado en cada call-site desde que
/// el juego se compiló — para eso este procesador lo detecta y lo explica.
///
/// El .cctor original se conserva: el campo sigue valiendo lo mismo para quien
/// lo lea por reflexión; lo que cambia es lo que el CÓDIGO ve en cada lectura.
///
/// La sustitución se hace MUTANDO la instrucción en sitio (mismo objeto
/// Instruction, OpCode/Operand nuevos) para que ningún salto que apuntara a
/// ella quede huérfano. Un solo pase por ensamblado con la carga precomputada.
/// </summary>
internal static class ConstRewriter
{
    private const string AttrFullName = "Rework.ReworkConstAttribute";

    /// <summary>Una entrada validada con su carga constante precomputada.</summary>
    private readonly struct Job
    {
        public readonly string FieldKey;
        public readonly OpCode OpCode;
        public readonly object? Operand;
        public readonly string Where;
        public readonly string Describe;

        public Job(string fieldKey, OpCode opCode, object? operand, string where, string describe)
        {
            FieldKey = fieldKey;
            OpCode = opCode;
            Operand = operand;
            Where = where;
            Describe = describe;
        }
    }

    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var entries = new List<(ModifiableAssembly asm, TypeDefinition? type, MethodDefinition? m, CustomAttribute ca)>();

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            var module = asm.ModuleDefinition;

            foreach (var ca in module.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                entries.Add((asm, null, null, ca));

            foreach (var type in ILSurgeryCommon.AllTypes(module))
            {
                foreach (var ca in type.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                    entries.Add((asm, type, null, ca));

                foreach (var m in type.Methods)
                    foreach (var ca in m.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                        entries.Add((asm, type, m, ca));
            }
        }

        if (entries.Count == 0)
        {
            Lg.Verbose("ReworkConst: ninguna entrada declarada.");
            return;
        }

        // 1) Validar todas las entradas y precomputar la carga constante.
        var jobs = new List<Job>();
        int skippedByDevMode = 0;
        foreach (var e in entries)
        {
            if (!ReworkDevMode.Filter(ILSurgeryCommon.ModIdentity(e.asm, e.type),
                                      ILSurgeryCommon.Where(e.type, e.m)))
            {
                Lg.Info($"ReworkConst: {ILSurgeryCommon.Where(e.type, e.m)} SALTO por ReworkDevMode.");
                skippedByDevMode++;
                continue;
            }
            var job = ValidateOne(set, asmCSharp, e);
            if (job != null) jobs.Add(job.Value);
        }

        if (jobs.Count == 0)
        {
            Lg.Info($"[ReworkConst] 0 constante(s) plegada(s) ({entries.Count - skippedByDevMode} inválida(s)).");
            return;
        }

        // 2) UN SOLO PASE por ensamblado reescribible.
        var sitesByJob = new Dictionary<string, int>(StringComparer.Ordinal);
        var addrsByJob = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalSites = 0;
        var perAsm = new List<string>();

        foreach (var targetAsm in ILSurgeryCommon.RewritableAssemblies(set, asmCSharp))
        {
            var module = targetAsm.ModuleDefinition;
            var map = jobs.ToDictionary(j => j.FieldKey, j => j, StringComparer.Ordinal);
            int asmSites = 0;

            foreach (var type in ILSurgeryCommon.AllTypes(module))
            {
                foreach (var meth in type.Methods)
                {
                    if (!meth.HasBody) continue;
                    foreach (var ins in meth.Body.Instructions)
                    {
                        if (ins.Operand is not FieldReference fr) continue;
                        if (!map.TryGetValue(fr.FullName, out var job)) continue;

                        if (ins.OpCode == OpCodes.Ldsfld)
                        {
                            ins.OpCode = job.OpCode;
                            ins.Operand = job.Operand;
                            asmSites++;
                            sitesByJob[job.Where] = sitesByJob.TryGetValue(job.Where, out var n) ? n + 1 : 1;
                        }
                        else if (ins.OpCode == OpCodes.Ldsflda)
                        {
                            // Una DIRECCIÓN no puede sustituirse por una constante: se
                            // deja y se avisa (el conteo saldrá en el resumen).
                            addrsByJob[job.Where] = addrsByJob.TryGetValue(job.Where, out var a) ? a + 1 : 1;
                        }
                    }
                }
            }

            if (asmSites > 0)
            {
                targetAsm.Modified = true;
                totalSites += asmSites;
                perAsm.Add($"{targetAsm.FriendlyName}={asmSites}");
            }
        }

        foreach (var j in jobs)
        {
            sitesByJob.TryGetValue(j.Where, out int n);
            addrsByJob.TryGetValue(j.Where, out int a);
            string addrs = a > 0 ? $" ({a} ldsflda sin plegar)" : "";
            if (n == 0 && a == 0)
                Lg.Info($"[ReworkConst] {j.FieldKey} → {j.Describe}: sin lecturas ldsfld en los ensamblados " +
                        "reescribibles (solo se usa por reflexión o no se usa).");
            else
                Lg.Info($"[ReworkConst] {j.FieldKey} → {j.Describe}: {n} lectura(s) reescrita(s){addrs}.");
        }

        int folded = jobs.Count(j => sitesByJob.TryGetValue(j.Where, out var n) && n > 0);
        Lg.Info($"[ReworkConst] {folded}/{jobs.Count} constante(s) plegada(s): {totalSites} lectura(s) en total " +
                $"({string.Join(", ", perAsm)}).");
        DataStore.AppliedPatches.Add($"[ReworkConst] {folded}/{jobs.Count} constante(s) plegada(s), {totalSites} lectura(s).");
    }

    /// <summary>Valida una entrada [ReworkConst] y precomputa su carga constante.</summary>
    private static Job? ValidateOne(
        AssemblySet set, ModifiableAssembly asmCSharp,
        (ModifiableAssembly asm, TypeDefinition? type, MethodDefinition? m, CustomAttribute ca) e)
    {
        var where = ILSurgeryCommon.Where(e.type, e.m);
        var typeName = ILSurgeryCommon.ReadStringProp(e.ca, "Type") ?? ReadCtorArg(e.ca, 0);
        var fieldName = ILSurgeryCommon.ReadStringProp(e.ca, "Field") ?? ReadCtorArg(e.ca, 1);
        var value = ReadValue(e.ca);

        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(fieldName))
        {
            Lg.Error($"ReworkConst {where}: faltan Type y/o Field.");
            return null;
        }

        var found = ILSurgeryCommon.FindTypeInRewritable(set, asmCSharp, typeName!);
        if (found == null)
        {
            Lg.Error($"ReworkConst {where}: tipo destino '{typeName}' no hallado.");
            return null;
        }
        var (_, targetType) = found.Value;

        var field = targetType.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            Lg.Error($"ReworkConst {where}: no se halló el campo '{fieldName}' en {targetType.FullName}.");
            return null;
        }
        if (!field.IsStatic)
        {
            Lg.Error($"ReworkConst {where}: {targetType.FullName}.{fieldName} no es static; solo se pliegan campos estáticos.");
            return null;
        }
        if (field.IsLiteral)
        {
            Lg.Error($"ReworkConst {where}: {targetType.FullName}.{fieldName} es const (literal): su valor está " +
                     "QUEMADO en cada call-site desde que el juego se compiló (no hay lecturas de campo que " +
                     "reescribir). Sustitúyelo con [ReworkRedirect]/[ReworkPatch] o elige un static readonly.");
            return null;
        }

        var fieldType = field.FieldType;
        var resolved = fieldType.Resolve();
        bool isEnum = resolved is { IsEnum: true };

        if (value == null && fieldType.MetadataType != MetadataType.String
                           && (resolved == null || resolved.IsValueType))
        {
            Lg.Error($"ReworkConst {where}: falta Value (y {targetType.FullName}.{fieldName} no admite null).");
            return null;
        }

        var (op, operand) = ComputeConstant(fieldType, isEnum, resolved, value, where);
        if (op == null) return null; // error ya reportado

        return new Job(field.FullName, op.Value, operand, where, DescribeValue(value));
    }

    /// <summary>Precomputa el (OpCode, operand) de la carga constante. Null → error.</summary>
    private static (OpCode? Op, object? Operand) ComputeConstant(
        TypeReference fieldType, bool isEnum, TypeDefinition? resolvedEnum, object? value, string where)
    {
        try
        {
            if (value == null)
                return (OpCodes.Ldnull, null);

            if (isEnum)
            {
                // Tipo subyacente: el campo estático 'value__' (forma canónica en metadata).
                var valueField = resolvedEnum?.Fields.FirstOrDefault(f => f.Name == "value__");
                var md = valueField?.FieldType.MetadataType ?? MetadataType.Int32;
                if (md == MetadataType.Int64 || md == MetadataType.UInt64)
                    return (OpCodes.Ldc_I8, System.Convert.ToInt64(value));
                return (OpCodes.Ldc_I4, System.Convert.ToInt32(value));
            }

            switch (fieldType.MetadataType)
            {
                case MetadataType.Boolean:
                    return (OpCodes.Ldc_I4, System.Convert.ToBoolean(value) ? 1 : 0);
                case MetadataType.Byte:
                case MetadataType.SByte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Char:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                    return (OpCodes.Ldc_I4, System.Convert.ToInt32(value));
                case MetadataType.Int64:
                case MetadataType.UInt64:
                    return (OpCodes.Ldc_I8, System.Convert.ToInt64(value));
                case MetadataType.Single:
                    return (OpCodes.Ldc_R4, System.Convert.ToSingle(value));
                case MetadataType.Double:
                    return (OpCodes.Ldc_R8, System.Convert.ToDouble(value));
                case MetadataType.String:
                    if (value is not string s)
                    {
                        Lg.Error($"ReworkConst {where}: el campo es string pero Value es {value.GetType().Name}.");
                        return (null, null);
                    }
                    return (OpCodes.Ldstr, s);
                default:
                    Lg.Error($"ReworkConst {where}: tipo de campo no soportado para plegado " +
                             $"({fieldType.FullName}); soportados: bool, enteros, float, double, string, null y enums.");
                    return (null, null);
            }
        }
        catch (Exception ex)
        {
            Lg.Error($"ReworkConst {where}: el valor {DescribeValue(value)} no es válido para " +
                     $"{fieldType.FullName}: {ex.Message}");
            return (null, null);
        }
    }

    private static string DescribeValue(object? v) => v == null ? "null" : $"{v} ({v.GetType().Name})";

    /// <summary>Lee Value: propiedad "Value" o tercer argumento posicional del ctor.</summary>
    private static object? ReadValue(CustomAttribute ca)
    {
        foreach (var p in ca.Properties)
            if (p.Name == "Value")
                return ILSurgeryCommon.Unwrap(p.Argument.Value);
        if (ca.ConstructorArguments.Count > 2)
            return ILSurgeryCommon.Unwrap(ca.ConstructorArguments[2].Value);
        return null;
    }

    private static string? ReadCtorArg(CustomAttribute ca, int index)
    {
        if (ca.ConstructorArguments.Count > index && ca.ConstructorArguments[index].Value is string s)
            return s;
        return null;
    }
}
