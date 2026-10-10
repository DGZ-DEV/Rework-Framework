using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Bloque 24.1 — [ReworkRedirect]: reescribe TODOS los call-sites de un método del
/// juego para que apunten al método del mod, en TODOS los ensamblados reescribibles
/// (Assembly-CSharp + mods), durante la pasada 1. El IL final llama directamente al
/// método del mod: sin detours, sin trampolines, coste cero.
///
/// Diferencia con Harmony: Harmony sustituye el CUERPO del método destino y deja
/// un trampolín permanente; aquí se sustituye cada PUNTO DE LLAMADA, igual que si
/// el juego se hubiera compilado contra tu método.
///
/// Regla anti-recursión: las llamadas al método destino que están DENTRO del propio
/// método redirector NO se reescriben — es el camino documentado para invocar al
/// original ("call-through"), y evita el bucle infinito clásico de los
/// interceptores de call-sites.
///
/// Aviso para métodos virtuales de instancia: los call-sites reescritos son los
/// compilados contra el tipo declarante EXACTO (match por firma completa); un
/// `base.Metodo()` inyectado por [ReworkOverride] o escrito por el propio juego
/// TAMBIÉN quedaría redirigido — redirigir métodos virtuales es responsabilidad
/// del modder (para colgarte del despacho virtual, [ReworkOverride] es la
/// herramienta correcta).
/// </summary>
internal static class CallSiteRedirector
{
    private const string AttrFullName = "Rework.ReworkRedirectAttribute";

    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        // 1) Recolectar las declaraciones [ReworkRedirect] de los mods.
        var entries = new List<(ModifiableAssembly asm, TypeDefinition type, MethodDefinition m, CustomAttribute ca)>();
        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
            foreach (var type in ILSurgeryCommon.AllTypes(asm.ModuleDefinition))
                foreach (var m in type.Methods)
                    foreach (var ca in m.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                        entries.Add((asm, type, m, ca));

        if (entries.Count == 0)
        {
            Lg.Verbose("ReworkRedirect: ninguna entrada declarada.");
            return;
        }

        // 2) Identidad de los métodos redirector (para la regla anti-recursión).
        var wrappers = new HashSet<string>(
            entries.Select(e => e.m.FullName), StringComparer.Ordinal);

        // 3) Validar cada entrada contra el método destino.
        var jobs = new List<(MethodReference modRefTemplate, MethodDefinition target, string key, string where)>();
        var targetsSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (asm, type, m, ca) in entries)
        {
            var where = $"{type.FullName}::{m.Name}";
            if (!ReworkDevMode.Filter(ILSurgeryCommon.ModIdentity(asm, type), $"{type.FullName}::{m.Name}"))
            {
                Lg.Info($"ReworkRedirect: {where} SALTO por ReworkDevMode.");
                continue;
            }

            if (!m.IsStatic || !m.IsPublic)
            {
                Lg.Error($"ReworkRedirect {where}: el método redirector debe ser 'public static' " +
                         "(lo llama el IL reescrito de otros ensamblados).");
                continue;
            }

            var typeName = ILSurgeryCommon.ReadStringProp(ca, "Type") ?? ReadCtorArg(ca, 0);
            var methodName = ILSurgeryCommon.ReadStringProp(ca, "Method") ?? ReadCtorArg(ca, 1);
            var asmName = ILSurgeryCommon.ReadStringProp(ca, "Assembly");

            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
            {
                Lg.Error($"ReworkRedirect {where}: faltan Type y/o Method.");
                continue;
            }

            // Ensamblado del destino: Assembly-CSharp por defecto (el usual).
            ModifiableAssembly? targetAsm = asmCSharp;
            if (!string.IsNullOrEmpty(asmName))
            {
                var found = set.FindAssembly(asmName!);
                if (found == null || found.SourceAssembly == null)
                {
                    Lg.Error($"ReworkRedirect {where}: el ensamblado destino '{asmName}' no está en el set " +
                             "o no es reescribible.");
                    continue;
                }
                targetAsm = found;
            }

            var targetType = ILSurgeryCommon.FindType(targetAsm.ModuleDefinition, typeName!);
            if (targetType == null)
            {
                Lg.Error($"ReworkRedirect {where}: tipo destino '{typeName}' no hallado en {targetAsm.FriendlyName}.");
                continue;
            }

            var target = MatchTargetMethod(targetType, methodName!, m);
            if (target == null)
            {
                Lg.Error($"ReworkRedirect {where}: no se halló {typeName}::{methodName} con firma " +
                         $"compatible con el método del mod ({Describe(m)}).");
                continue;
            }

            var key = target.FullName;
            if (!targetsSeen.Add(key))
            {
                Lg.Error($"ReworkRedirect {where}: ya hay otra redirección para '{key}'; se ignora esta.");
                continue;
            }

            if (target.HasGenericParameters)
            {
                Lg.Error($"ReworkRedirect {where}: el método destino es genérico; no soportado.");
                continue;
            }

            if (target.IsVirtual && target.HasThis)
                Lg.Info($"ReworkRedirect {where}: aviso — '{key}' es un virtual de instancia; se reescriben " +
                         "los call-sites compilados contra el tipo declarante exacto (los base-calls también).");

            jobs.Add((m, target, key, where));
        }

        if (jobs.Count == 0)
        {
            Lg.Info("ReworkRedirect: ninguna redirección válida.");
            return;
        }

        // 4) Reescribir call-sites en todos los ensamblados reescribibles.
        //    UN SOLO PASE por ensamblado: por cada módulo se importa la referencia
        //    al método del mod y se consulta por diccionario (la clave es el
        //    FullName con firma del método destino).
        var sitesByJob = new Dictionary<string, int>(StringComparer.Ordinal); // where → total
        int totalSites = 0;
        var perAsm = new List<string>();

        foreach (var targetAsm in ILSurgeryCommon.RewritableAssemblies(set, asmCSharp))
        {
            var module = targetAsm.ModuleDefinition;

            // §47 — IMPORT PEREZOSO: la referencia al método del mod solo se
            // importa en un módulo cuando hay un call-site REAL que reescribir
            // en él. El import anterior (anticipado, en TODOS los rewritables)
            // añadía la AssemblyReference "Rework" a ensamblados INOCENTES
            // (0ReworkData, Mono.Cecil*, 0ReworkAPI) sin sitios → al ser
            // "Rework" semilla del BFS de PropagateNeedsReload, la propagación
            // los arrastraba al swap como "dependientes" → 0ReworkData recargado
            // → estáticos de DataStore frescos → BUCLE DE ARRANQUE (§47). Los
            // ensamblados sin sitios vuelven a quedar intactos.
            var map = new Dictionary<string, (MethodReference modTemplate, string where)>(StringComparer.Ordinal);
            foreach (var (modTemplate, target, key, where) in jobs)
                map[key] = (modTemplate, where);
            var imported = new Dictionary<string, MethodReference>(StringComparer.Ordinal);

            int asmSites = 0;

            foreach (var type in ILSurgeryCommon.AllTypes(module))
            {
                foreach (var meth in type.Methods)
                {
                    if (!meth.HasBody) continue;

                    // Regla anti-recursión: dentro de un redirector no se toca nada.
                    if (wrappers.Contains(meth.FullName)) continue;

                    foreach (var ins in meth.Body.Instructions)
                    {
                        if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        if (!map.TryGetValue(mr.FullName, out var job)) continue;

                        if (!imported.TryGetValue(mr.FullName, out var modRef))
                        {
                            modRef = module.ImportReference(job.modTemplate);
                            imported[mr.FullName] = modRef;
                        }

                        ins.Operand = modRef;
                        asmSites++;
                        sitesByJob[job.where] = sitesByJob.TryGetValue(job.where, out var n) ? n + 1 : 1;
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

        foreach (var (modTemplate, target, key, where) in jobs)
        {
            sitesByJob.TryGetValue(where, out int n);
            Lg.Info($"[ReworkRedirect] '{key}' → {where}: {n} call-site(s) reescrito(s).");
        }

        Lg.Info($"[ReworkRedirect] {jobs.Count} redirección(es) aplicada(s): {totalSites} call-site(s) " +
                $"reescrito(s) en total ({string.Join(", ", perAsm)}).");
        DataStore.AppliedPatches.Add($"[ReworkRedirect] {totalSites} call-site(s) reescrito(s) para " +
                                     string.Join("; ", jobs.Select(j => j.key)) + ".");
    }

    /// <summary>
    /// Busca entre los overloads de typeName::methodName el que encaje EXACTAMENTE
    /// con la firma del método del mod (incluido el 'this' si el destino es de
    /// instancia). Match exacto de tipos por FullName: es lo que garantiza que el
    /// IL resultante es válido sin conversiones.
    /// </summary>
    private static MethodDefinition? MatchTargetMethod(
        TypeDefinition targetType, string methodName, MethodDefinition modMethod)
    {
        var candidates = targetType.Methods.Where(x => x.Name == methodName && !x.HasGenericParameters).ToList();
        if (candidates.Count == 0) return null;

        foreach (var cand in candidates)
        {
            if (cand.Parameters.Any(p => p.ParameterType.IsByReference)) continue;

            int modParams = modMethod.Parameters.Count;
            int firstParam = cand.HasThis ? 1 : 0;
            int need = firstParam + cand.Parameters.Count;

            if (modParams != need) continue;

            // 'this' (solo instancia): el tipo declarante o una de sus bases.
            if (cand.HasThis)
            {
                var thisType = modMethod.Parameters[0].ParameterType.Resolve();
                if (thisType == null || !ILSurgeryCommon.IsOrDerivesFrom(targetType, thisType.FullName))
                    continue;
            }

            bool ok = true;
            for (int i = 0; i < cand.Parameters.Count; i++)
            {
                if (cand.Parameters[i].ParameterType.FullName !=
                    modMethod.Parameters[i + firstParam].ParameterType.FullName)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            if (cand.ReturnType.FullName != modMethod.ReturnType.FullName) continue;

            return cand;
        }
        return null;
    }

    private static string Describe(MethodDefinition m)
    {
        var ps = string.Join(", ", m.Parameters.Select(p => p.ParameterType.FullName));
        return $"({ps}) → {m.ReturnType.FullName}";
    }

    private static string? ReadCtorArg(CustomAttribute ca, int index)
    {
        if (ca.ConstructorArguments.Count > index && ca.ConstructorArguments[index].Value is string s)
            return s;
        return null;
    }
}
