using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework;

namespace Rework.Core;

/// <summary>
/// Procesador de "parches libres" ([ReworkPatch], Fase 3 y 4): durante la pasada 1
/// (reescritura en memoria) invoca los métodos estáticos marcados, siguiendo el orden
/// de PRIORIDAD (mayor primero), sobre Assembly-CSharp.
///
/// Dos modos (ver ReworkPatchAttribute):
///   1. Directo:  void M(ModuleDefinition)        → el modder modifica el módulo con Cecil.
///   2. Transpiler amigable: IEnumerable&lt;Instruction&gt; M(IEnumerable&lt;Instruction&gt;, ModuleDefinition)
///      con Type/Method en el atributo → recibe las instrucciones del método destino y
///      devuelve la lista modificada; aquí se reconstruye el cuerpo REUSANDO el mismo
///      MethodBody (se conservan manejadores de excepción, variables y debug info).
/// </summary>
internal static class FreePatcher
{
    internal static void Process(AssemblySet set)
    {
        var asmCSharp = set.FindAssembly(AssemblyCollector.AssemblyCSharp);
        if (asmCSharp == null)
            return;
        var module = asmCSharp.ModuleDefinition;

        // Recolectar todos los [ReworkPatch] de los ensamblados con ProcessAttributes.
        // El orden de carga del mod (índice en AllAssemblies = RunningModsListForReading)
        // es el DESEMPATE del orden final: cross-mod ordering (Fase 4).
        var patches = new List<(ModifiableAssembly asm, TypeDefinition type, MethodDefinition md, ReworkPatchMeta attr, int modOrder)>();

        var loadOrder = new Dictionary<ModifiableAssembly, int>();
        for (int i = 0; i < set.AllAssemblies.Count; i++)
            loadOrder[set.AllAssemblies[i]] = i;

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            var order = loadOrder[asm];
            foreach (var type in asm.ModuleDefinition.Types)
            {
                foreach (var m in type.Methods)
                {
                    if (!m.IsStatic)
                        continue;
                    var ca = m.CustomAttributes.FirstOrDefault(a =>
                        a.AttributeType.FullName == "Rework.ReworkPatchAttribute");
                    if (ca == null)
                        continue;
                    patches.Add((asm, type, m, new ReworkPatchMeta(ca), order));
                }
            }
        }

        // Prioridad: mayor primero (Fase 4). Desempate: orden de carga del mod (menor
        // índice = mod más temprano en la lista de mods = primero). Estable por nombre.
        var ordered = patches
            .OrderByDescending(p => p.attr.Priority)
            .ThenBy(p => p.modOrder)
            .ThenBy(p => p.md.FullName, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count > 0)
            Lg.Info("FreePatcher: orden de aplicación (prio desc → orden de carga → nombre): " +
                    string.Join("; ", ordered.Select(p =>
                        $"{p.modOrder}:prio{p.attr.Priority}:{p.md.DeclaringType.Name}::{p.md.Name}")));

        foreach (var (asm, type, m, attr, _) in ordered)
        {
            // 17.8/17.9 — Modo desarrollador: ReworkDevMode.OnlyPatch / OnlyMod filtran
            // los parches que se aplican (para aislar un fallo o revertir uno sin quitarlo
            // del mod). El framework registra cuál se salta.
            var devModName = asm.SourceAssembly?.GetType(type.FullName, throwOnError: false)?.Namespace ?? type.Namespace;
            if (!ReworkDevMode.Filter(devModName, m.FullName))
            {
                Lg.Info($"FreePatcher: {m.FullName} SALTO por ReworkDevMode (OnlyPatch/OnlyMod).");
                continue;
            }

            ApplyOne(set, asmCSharp, module, asm, type, m, attr);
        }

        if (patches.Count == 0)
            Lg.Info("FreePatcher: ningún [ReworkPatch] encontrado.");
    }

    private static void ApplyOne(
        AssemblySet set,
        ModifiableAssembly asmCSharp,
        ModuleDefinition module,
        ModifiableAssembly asm,
        TypeDefinition type,
        MethodDefinition m,
        ReworkPatchMeta attr)
    {
        var sourceAsm = asm.SourceAssembly;
        if (sourceAsm == null)
        {
            Lg.Error($"Parche libre {m.FullName}: sin SourceAssembly (¿no está cargado el mod?).");
            return;
        }

        try
        {
            var rtType = sourceAsm.GetType(type.FullName, throwOnError: false);
            if (rtType == null)
            {
                Lg.Error($"Parche libre {m.FullName}: el tipo de runtime no se halló en {asm.FriendlyName}.");
                return;
            }

            var rtMethod = rtType.GetMethod(
                m.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (rtMethod == null)
            {
                Lg.Error($"Parche libre {m.FullName}: el método de runtime no se halló.");
                return;
            }

            var retType = rtMethod.ReturnType;
            var ps = rtMethod.GetParameters();

            if (retType == typeof(void))
            {
                // --- Modo directo ---
                object[] args;
                if (ps.Length == 0)
                    args = Array.Empty<object>();
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(ModuleDefinition))
                    args = new object[] { module };
                else
                {
                    Lg.Error($"Parche libre {m.FullName}: firma directa no soportada " +
                             "(solo 0 o 1 parámetro ModuleDefinition).");
                    return;
                }

                rtMethod.Invoke(null, args);
                asmCSharp.Modified = true;
                Lg.Info($"Parche libre ejecutado (prio {attr.Priority}): {m.FullName}");
            }
            else if (retType == typeof(IEnumerable<Instruction>))
            {
                // --- Modo transpiler ---
                if (string.IsNullOrEmpty(attr.Type) || string.IsNullOrEmpty(attr.Method))
                {
                    Lg.Error($"Transpiler {m.FullName}: falta Type/Method en el atributo para el destino.");
                    return;
                }

                var targetType = module.GetType(attr.Type);
                var targetMethod = targetType?.Methods.FirstOrDefault(x => x.Name == attr.Method);
                if (targetMethod == null || !targetMethod.HasBody)
                {
                    Lg.Error($"Transpiler {m.FullName}: no se halló {attr.Type}::{attr.Method} en Assembly-CSharp.");
                    return;
                }

                IEnumerable<Instruction> input = targetMethod.Body.Instructions.ToList();
                object[] targs;
                if (ps.Length == 1 && ps[0].ParameterType == typeof(IEnumerable<Instruction>))
                    targs = new object[] { input };
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(IEnumerable<Instruction>)
                                        && ps[1].ParameterType == typeof(ModuleDefinition))
                    targs = new object[] { input, module };
                else
                {
                    Lg.Error($"Transpiler {m.FullName}: firma no soportada " +
                             "(espera IEnumerable<Instruction>[, ModuleDefinition]).");
                    return;
                }

                var result = (IEnumerable<Instruction>)rtMethod.Invoke(null, targs)!;

                // Reconstruir el cuerpo REUSANDO el MISMO MethodBody (no crear uno nuevo).
                // Así se conservan los manejadores de excepción (ExceptionHandlers), las
                // variables locales (LocalVariableDefinitions) y el debug info (sequence
                // points). Antes se hacía `new MethodBody` y TODO eso se descartaba, lo que
                // rompía los try/catch y cualquier lógica que dependiera de vars locales.
                var body = targetMethod.Body;
                int hanPre = body.ExceptionHandlers.Count;
                body.Instructions.Clear(); // resetea el Next/Previous de las instrucciones viejas
                var ilp = body.GetILProcessor();
                foreach (var inst in result)
                    ilp.Append(inst);

                // Validación defensiva: un manejador cuyo límite apunte a una instrucción que
                // el modder ELIMINÓ produciría una Assembly corrupta (pendiente en negro en el
                // boot). Se descarta ese manejador con aviso. Norma: el modder no debe borrar
                // las instrucciones que son límites de regiones EH ni los saltos que las tocan.
                var present = new HashSet<Instruction>(body.Instructions);
                for (int i = body.ExceptionHandlers.Count - 1; i >= 0; i--)
                {
                    var eh = body.ExceptionHandlers[i];
                    bool stale = (eh.TryStart != null && !present.Contains(eh.TryStart))
                              || (eh.TryEnd != null && !present.Contains(eh.TryEnd))
                              || (eh.HandlerStart != null && !present.Contains(eh.HandlerStart))
                              || (eh.HandlerEnd != null && !present.Contains(eh.HandlerEnd))
                              || (eh.FilterStart != null && !present.Contains(eh.FilterStart));
                    if (stale)
                        body.ExceptionHandlers.RemoveAt(i);
                }
                int dropped = hanPre - body.ExceptionHandlers.Count;
                if (dropped > 0)
                    Lg.Error($"Transpiler {m.FullName}: {dropped} manejador(es) EH descartado(s) " +
                             "por referenciar instrucciones eliminadas.");
                else if (hanPre > 0)
                    Lg.Info($"Transpiler {m.FullName}: {body.ExceptionHandlers.Count} manejador(es) " +
                            "de excepción preservado(s).");

                asmCSharp.Modified = true;
                Lg.Info($"Transpiler aplicado (prio {attr.Priority}): {m.FullName} → {attr.Type}::{attr.Method}");
            }
            else
            {
                Lg.Error($"Parche libre {m.FullName}: tipo de retorno no soportado ({retType}).");
            }
        }
        catch (Exception e)
        {
            Lg.Error($"Parche libre {m.FullName} falló: {e}");
        }
    }

    /// <summary>Datos leídos del ReworkPatchAttribute: prioridad + destino.</summary>
    private readonly struct ReworkPatchMeta
    {
        public int Priority { get; }
        public string? Type { get; }
        public string? Method { get; }

        public ReworkPatchMeta(CustomAttribute ca)
        {
            Priority = 0;
            Type = null;
            Method = null;

            // Prioriad por constructor [ReworkPatch(int)] -> [0].
            var ctorArgs = ca.ConstructorArguments;
            if (ctorArgs.Count > 0 && ctorArgs[0].Value is int prio)
                Priority = prio;

            // Propiedades con nombre (Priority y/o Type/Method).
            foreach (var prop in ca.Properties)
            {
                var val = prop.Argument.Value;
                switch (prop.Name)
                {
                    case "Priority" when val is int i:
                        Priority = i;
                        break;
                    case "Type" when val is string s:
                        Type = s;
                        break;
                    case "Method" when val is string s:
                        Method = s;
                        break;
                }
            }
        }
    }
}