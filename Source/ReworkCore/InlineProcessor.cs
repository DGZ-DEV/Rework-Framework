using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Bloque 24.4 — [ReworkInline]: sustituye TODAS las llamadas a un método trivial
/// del juego por el acceso directo al campo subyacente, en todos los ensamblados
/// reescribibles. El JIT ya no ve una llamada: ve un ldfld — el inline perfecto,
/// imposible de garantizar con un parche en runtime (Harmony solo puede envolver
/// el método; cada llamada sigue pagando despacho + límites de inlining).
///
/// Patrón aceptado (VALIDACIÓN ESTRICTA, cualquier otra cosa se rechaza):
///   - instancia:  ldarg.0 ; ldfld F ; ret          (getter de campo)
///   - estático:   ldsfld F ; ret
/// No se inlinean métodos virtuales no finales (rompería el polimorfismo: los
/// callvirt despacharían al original en las subclases) ni genéricos.
///
/// Si el campo de respaldo no es público, se hace público (cambio de metadata,
/// mismo criterio que [ReworkUnlock]) para que el acceso cruzado entre
/// ensamblados no dependa de la política de verificación del runtime.
///
/// La mutación es EN SITIO (mismo Instruction): los saltos que apuntaban a la
/// llamada siguen válidos.
/// </summary>
internal static class InlineProcessor
{
    private const string AttrFullName = "Rework.ReworkInlineAttribute";

    /// <summary>Una entrada validada, lista para el pase de reescritura.</summary>
    private readonly struct Job
    {
        public readonly MethodDefinition Target;
        public readonly FieldReference BackingField;
        public readonly string Where;

        public Job(MethodDefinition target, FieldReference backingField, string where)
        {
            Target = target;
            BackingField = backingField;
            Where = where;
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
            Lg.Verbose("ReworkInline: ninguna entrada declarada.");
            return;
        }

        // 1) Validar todas las entradas (sin tocar IL todavía).
        var jobs = new List<Job>();
        int skippedByDevMode = 0;
        foreach (var e in entries)
        {
            if (!ReworkDevMode.Filter(ILSurgeryCommon.ModIdentity(e.asm, e.type),
                                      ILSurgeryCommon.Where(e.type, e.m)))
            {
                Lg.Info($"ReworkInline: {ILSurgeryCommon.Where(e.type, e.m)} SALTO por ReworkDevMode.");
                skippedByDevMode++;
                continue;
            }
            var job = ValidateOne(set, asmCSharp, e);
            if (job != null) jobs.Add(job.Value);
        }

        if (jobs.Count == 0)
        {
            Lg.Info($"[ReworkInline] 0 getter(s) inlineado(s) ({entries.Count - skippedByDevMode} inválido(s)).");
            return;
        }

        // 2) UN SOLO PASE por ensamblado reescribible.
        var sitesByJob = new Dictionary<string, int>(StringComparer.Ordinal); // where → total
        int totalSites = 0;
        var perAsm = new List<string>();

        foreach (var targetAsm in ILSurgeryCommon.RewritableAssemblies(set, asmCSharp))
        {
            var module = targetAsm.ModuleDefinition;

            // §47 — IMPORT PEREZOSO (misma lección que CallSiteRedirector): la
            // referencia al campo de respaldo solo se importa en un módulo donde
            // haya un call-site REAL; los ensamblados sin sitios quedan intactos
            // (un import anticipado añadiría referencias que el BFS de
            // PropagateNeedsReload interpretaría como dependencias → swap de
            // inocentes → vértice del bucle §47 con 0ReworkData).
            var map = new Dictionary<string, (FieldReference backingField, bool isStatic, string where)>(StringComparer.Ordinal);
            foreach (var j in jobs)
                map[j.Target.FullName] = (j.BackingField, j.Target.IsStatic, j.Where);
            var imported = new Dictionary<string, FieldReference>(StringComparer.Ordinal);

            int asmSites = 0;

            foreach (var type in ILSurgeryCommon.AllTypes(module))
            {
                foreach (var meth in type.Methods)
                {
                    if (!meth.HasBody) continue;
                    foreach (var ins in meth.Body.Instructions)
                    {
                        if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        if (!map.TryGetValue(mr.FullName, out var j)) continue;

                        if (!imported.TryGetValue(mr.FullName, out var fieldRef))
                        {
                            fieldRef = module.ImportReference(j.backingField);
                            imported[mr.FullName] = fieldRef;
                        }

                        // Mutación EN SITIO: el receptor ya está en la pila (era el
                        // argumento de la llamada); ldfld/ldsfld consumen lo mismo y
                        // empujan el mismo valor. Los saltos siguen válidos.
                        ins.OpCode = j.isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;
                        ins.Operand = fieldRef;
                        asmSites++;
                        sitesByJob[j.where] = sitesByJob.TryGetValue(j.where, out var n) ? n + 1 : 1;
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
            Lg.Info($"[ReworkInline] {j.Target.FullName} → {j.BackingField.FullName}: {n} call-site(s) sustituido(s).");
        }

        Lg.Info($"[ReworkInline] {jobs.Count} getter(s) inlineado(s): {totalSites} call-site(s) en total " +
                $"({string.Join(", ", perAsm)}).");
        DataStore.AppliedPatches.Add($"[ReworkInline] {jobs.Count} getter(s) inlineado(s), {totalSites} call-site(s).");
    }

    /// <summary>Valida una entrada [ReworkInline] y devuelve el trabajo listo (o null).</summary>
    private static Job? ValidateOne(
        AssemblySet set, ModifiableAssembly asmCSharp,
        (ModifiableAssembly asm, TypeDefinition? type, MethodDefinition? m, CustomAttribute ca) e)
    {
        var where = ILSurgeryCommon.Where(e.type, e.m);
        var typeName = ILSurgeryCommon.ReadStringProp(e.ca, "Type") ?? ReadCtorArg(e.ca, 0);
        var methodName = ILSurgeryCommon.ReadStringProp(e.ca, "Method") ?? ReadCtorArg(e.ca, 1);

        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
        {
            Lg.Error($"ReworkInline {where}: faltan Type y/o Method.");
            return null;
        }

        var found = ILSurgeryCommon.FindTypeInRewritable(set, asmCSharp, typeName!);
        if (found == null)
        {
            Lg.Error($"ReworkInline {where}: tipo destino '{typeName}' no hallado.");
            return null;
        }
        var (ownerAsm, targetType) = found.Value;

        var candidates = targetType.Methods.Where(x => x.Name == methodName && !x.HasGenericParameters).ToList();
        if (candidates.Count == 0)
        {
            Lg.Error($"ReworkInline {where}: no se halló {typeName}::{methodName}.");
            return null;
        }
        if (candidates.Count > 1)
        {
            Lg.Error($"ReworkInline {where}: '{typeName}::{methodName}' tiene {candidates.Count} overloads; " +
                     "especifica un método único.");
            return null;
        }

        var target = candidates[0];

        if (!target.HasBody)
        {
            Lg.Error($"ReworkInline {where}: {target.FullName} no tiene cuerpo.");
            return null;
        }
        if (target.Parameters.Count > 0)
        {
            Lg.Error($"ReworkInline {where}: {target.FullName} tiene parámetros; solo se inlinean getters sin parámetros.");
            return null;
        }
        if (target.IsVirtual && !target.IsFinal)
        {
            Lg.Error($"ReworkInline {where}: {target.FullName} es virtual sin final; inlinearlo rompería el " +
                     "polimorfismo (los callvirt despacharían al original en las subclases).");
            return null;
        }

        FieldReference backingField;
        var ins = target.Body.Instructions;
        if (!target.IsStatic && ins.Count == 3
            && ins[0].OpCode == OpCodes.Ldarg_0
            && ins[1].OpCode == OpCodes.Ldfld
            && ins[2].OpCode == OpCodes.Ret)
        {
            backingField = (FieldReference)ins[1].Operand;
        }
        else if (target.IsStatic && ins.Count == 2
            && ins[0].OpCode == OpCodes.Ldsfld
            && ins[1].OpCode == OpCodes.Ret)
        {
            backingField = (FieldReference)ins[0].Operand;
        }
        else
        {
            Lg.Error($"ReworkInline {where}: {target.FullName} no es un getter trivial " +
                     "(ldarg.0; ldfld; ret / ldsfld; ret); NO se toca nada.");
            return null;
        }

        // Campo de respaldo público para el acceso cruzado entre ensamblados
        // (cambio de metadata seguro: no altera comportamiento ni serialización).
        if (backingField.Resolve() is { } fd && !fd.IsPublic)
        {
            fd.IsPublic = true;
            ownerAsm.Modified = true;
            Lg.Info($"[ReworkInline] {fd.FullName}: campo de respaldo ahora public (acceso cruzado).");
        }

        return new Job(target, backingField, where);
    }

    private static string? ReadCtorArg(CustomAttribute ca, int index)
    {
        if (ca.ConstructorArguments.Count > index && ca.ConstructorArguments[index].Value is string s)
            return s;
        return null;
    }
}
