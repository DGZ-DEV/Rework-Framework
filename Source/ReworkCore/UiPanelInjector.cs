using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// [ReworkUIPanel] — UI Inyectada (invención del roadmap).
///
/// Inyecta, en la pasada 1 (reescritura en memoria), la llamada al método del mod
/// dentro del DoWindowContents(Rect) de una ventana del juego. El método del mod
/// dibuja con el GUI estándar de Verse (Widgets/Text) cada frame mientras la
/// ventana esté abierta. Sin XML, sin Window propia, sin ThingComp, sin escribir
/// un [ReworkPatch] manual.
///
/// Firmas soportadas:
///   static void MiPanel(Window window, Rect inRect)   → 2 parámetros
///   static void MiPanel(Rect inRect)                  → 1 parámetro
///
/// Inserción:
///   AtStart=true  → al INICIO del DoWindowContents (el panel queda DEBAJO del
///                   contenido de la ventana).
///   AtStart=false → justo antes del último ret del cuerpo (el panel queda ENCIMA,
///                   visible sobre el contenido de la ventana). Si todos los ret
///                   están dentro de regiones try/catch (raro en ventanas), se
///                   degrada a inyección al inicio con aviso en el log.
///
/// Orden entre paneles de la misma ventana: Priority desc → orden de carga del
/// mod → nombre completo (estable), igual que FreePatcher.
/// </summary>
internal static class UiPanelInjector
{
    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        // Recolectar todos los métodos [ReworkUIPanel] de los ensamblados de mods.
        var panels = new List<(ModifiableAssembly asm, MethodDefinition md, UIPanelMeta attr, int modOrder)>();

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
                        a.AttributeType.FullName == "Rework.ReworkUIPanelAttribute");
                    if (ca == null)
                        continue;
                    panels.Add((asm, m, new UIPanelMeta(ca), order));
                }
            }
        }

        if (panels.Count == 0)
        {
            Lg.Info("UiPanelInjector: ningún [ReworkUIPanel] encontrado.");
            return;
        }

        var ordered = panels
            .OrderByDescending(p => p.attr.Priority)
            .ThenBy(p => p.modOrder)
            .ThenBy(p => p.md.FullName, StringComparer.Ordinal)
            .ToList();

        Lg.Info("UiPanelInjector: orden de inyección (prio desc → carga → nombre): " +
                string.Join("; ", ordered.Select(p =>
                    $"{p.modOrder}:prio{p.attr.Priority}:{p.md.DeclaringType.Name}::{p.md.Name}")));

        foreach (var (asm, m, attr, _) in ordered)
        {
            // 17.8/17.9 — Modo desarrollador: filtrar igual que FreePatcher.
            var devModName = asm.SourceAssembly?.GetType(m.DeclaringType.FullName, throwOnError: false)?.Namespace
                             ?? m.DeclaringType.Namespace;
            if (!ReworkDevMode.Filter(devModName, m.FullName))
            {
                Lg.Info($"UiPanelInjector: {m.FullName} SALTO por ReworkDevMode (OnlyPatch/OnlyMod).");
                continue;
            }

            InjectOne(asmCSharp, module, asm, m, attr);
        }
    }

    private static void InjectOne(
        ModifiableAssembly asmCSharp,
        ModuleDefinition module,
        ModifiableAssembly asm,
        MethodDefinition m,
        UIPanelMeta attr)
    {
        try
        {
            var targetType = module.Types.FirstOrDefault(t => t.FullName == attr.WindowType);
            if (targetType == null)
            {
                Lg.Error($"ReworkUIPanel {m.FullName}: tipo ventana '{attr.WindowType}' no hallado en Assembly-CSharp.");
                return;
            }

            // DoWindowContents(Rect inRect): en el tipo destino o subiendo por la
            // jerarquía de base dentro del mismo módulo.
            var doWindow = FindDoWindowContents(module, targetType);
            if (doWindow == null || !doWindow.HasBody)
            {
                Lg.Error($"ReworkUIPanel {m.FullName}: no se halló DoWindowContents(Rect) (con cuerpo) en {attr.WindowType}.");
                return;
            }

            // Importar el método del mod (ModuleDefinition.ImportReference como en
            // MethodInjector/FreePatcher).
            MethodReference modRef;
            try
            {
                modRef = module.ImportReference(m);
            }
            catch (Exception e)
            {
                Lg.Error($"ReworkUIPanel {m.FullName}: no se pudo importar el método al módulo de Assembly-CSharp: {e.Message}");
                return;
            }

            // Firmas soportadas:
            //   2 params: (Window o subclase destino, Rect)  → ldarg.0; ldarg.1
            //   1 param:  (Rect)                             → ldarg.1
            var ps = m.Parameters;
            bool twoParams = ps.Count == 2
                && ps[1].ParameterType.FullName == "UnityEngine.Rect";
            bool oneParam = ps.Count == 1
                && ps[0].ParameterType.FullName == "UnityEngine.Rect";
            if (!twoParams && !oneParam)
            {
                Lg.Error($"ReworkUIPanel {m.FullName}: firma no soportada " +
                         "(espera (Window window, Rect inRect) o (Rect inRect)).");
                return;
            }

            var il = doWindow.Body.GetILProcessor();

            Instruction first = doWindow.Body.Instructions[0];

            // Guarda anti-duplicado (montaje repetido): si el cuerpo ya llama al
            // método del mod en cualquier punto, no inyectar de nuevo.
            if (doWindow.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Call
                    && i.Operand is MethodReference mr
                    && mr.FullName == modRef.FullName))
            {
                Lg.Info($"ReworkUIPanel {m.FullName}: ya inyectado en {attr.WindowType}::DoWindowContents; se omite.");
                return;
            }

            // Construir el bloque: ldarg.0 (si firma de 2 params); ldarg.1 (inRect);
            // call PanelDelMod(Window, Rect) / (Rect).
            var block = new List<Instruction>(3);
            if (twoParams)
                block.Add(il.Create(OpCodes.Ldarg_0));
            block.Add(il.Create(OpCodes.Ldarg_1));
            block.Add(il.Create(OpCodes.Call, modRef));

            Instruction anchor;
            if (attr.AtStart)
            {
                // --- Al inicio: debajo del contenido ---
                anchor = first;
            }
            else
            {
                // --- Al final: por encima del contenido ---
                var ret = FindSafeRet(doWindow);
                if (ret == null)
                {
                    Lg.Error($"ReworkUIPanel {m.FullName}: no se halló un ret fuera de regiones try/catch en {attr.WindowType}::DoWindowContents; se inyecta al inicio.");
                    anchor = first;
                }
                else
                {
                    anchor = ret;
                }
            }

            // LECCIÓN (verificación empírica 2026-10-07): si el método destino tiene
            // ramas condicionales que saltan DIRECTAMENTE al punto de inserción (p.ej.
            // DoWindowContents de Dialog_MessageBox tiene brfalse.s → ret para botones
            // B/C ausentes), el call inyectado se ejecuta SOLO por el flujo que cae al
            // anchor, y las ramas se lo saltan → el panel nunca se dibuja. Fix: tras
            // insertar, re-apuntar TODAS las ramas/leave cuyo destino era el anchor al
            // INICIO del bloque inyectado, para que ningún camino omita el panel.
            foreach (var i in block)
                il.InsertBefore(anchor, i);

            int reassigned = 0;
            foreach (var instr in doWindow.Body.Instructions)
            {
                var fc = instr.OpCode.FlowControl;
                if (fc != FlowControl.Branch && fc != FlowControl.Cond_Branch)
                    continue;
                if (!ReferenceEquals(instr.Operand, anchor))
                    continue;
                instr.Operand = block[0];
                reassigned++;
            }
            if (reassigned > 0)
                Lg.Info($"ReworkUIPanel {m.FullName}: {reassigned} rama(s) re-apuntada(s) al panel inyectado (ningún camino lo omite).");

            asmCSharp.Modified = true;
            DataStore.AppliedPatches.Add($"{attr.WindowType}::DoWindowContents → {m.FullName} ({m.DeclaringType.Name})");
            Lg.Info($"Panel UI inyectado: {m.FullName} → {attr.WindowType}::DoWindowContents " +
                    $"({(attr.AtStart ? "debajo/inicio" : "encima/final")}, prio {attr.Priority})");
        }
        catch (Exception e)
        {
            Lg.Error($"ReworkUIPanel {m.FullName} falló: {e}");
        }
    }

    /// <summary>Busca DoWindowContents(Rect) en el tipo o subiendo por la base
    /// (todos dentro del módulo de Assembly-CSharp).</summary>
    private static MethodDefinition? FindDoWindowContents(ModuleDefinition module, TypeDefinition type)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var t = type;
        while (t != null && visited.Add(t.FullName))
        {
            var found = t.Methods.FirstOrDefault(x =>
                x.Name == "DoWindowContents"
                && x.Parameters.Count == 1
                && x.Parameters[0].ParameterType.FullName == "UnityEngine.Rect");
            if (found != null)
                return found;

            if (t.BaseType == null)
                break;
            t = module.Types.FirstOrDefault(bt => bt.FullName == t.BaseType.FullName);
        }
        return null;
    }

    /// <summary>Último ret que NO esté dentro de ninguna región try/catch, para
    /// poder insertar "por encima" sin romper los manejadores de excepción.</summary>
    private static Instruction? FindSafeRet(MethodDefinition method)
    {
        var rets = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        if (rets.Count == 0)
            return null;

        var handlers = method.Body.ExceptionHandlers;
        foreach (var r in rets.AsEnumerable().Reverse())
        {
            bool inside = false;
            foreach (var eh in handlers)
            {
                if (IsInside(eh.TryStart, eh.TryEnd, r) || IsInside(eh.HandlerStart, eh.HandlerEnd, r))
                {
                    inside = true;
                    break;
                }
            }
            if (!inside)
                return r;
        }
        return null;
    }

    private static bool IsInside(Instruction? start, Instruction? end, Instruction target)
    {
        if (start == null || end == null)
            return false;
        var cur = start;
        while (cur != null && cur != end)
        {
            if (cur == target)
                return true;
            cur = cur.Next;
        }
        return false;
    }

    /// <summary>Guarda anti-duplicado: la instrucción previa (o la primera) ya llama
    /// al método del mod.</summary>
    private static bool IsPresent(Instruction? prev, MethodReference modRef)
    {
        return prev != null && prev.OpCode == OpCodes.Call
            && prev.Operand is MethodReference mr
            && mr.FullName == modRef.FullName;
    }

    /// <summary>Datos leídos del ReworkUIPanelAttribute: WindowType + AtStart + Priority.</summary>
    private readonly struct UIPanelMeta
    {
        public string WindowType { get; }
        public bool AtStart { get; }
        public int Priority { get; }

        public UIPanelMeta(CustomAttribute ca)
        {
            WindowType = "";
            AtStart = false;
            Priority = 0;

            // Constructor [ReworkUIPanel(string windowType)] -> [0].
            var ctorArgs = ca.ConstructorArguments;
            if (ctorArgs.Count > 0 && ctorArgs[0].Value is string w)
                WindowType = w;

            // Propiedades con nombre.
            foreach (var prop in ca.Properties)
            {
                var val = prop.Argument.Value;
                switch (prop.Name)
                {
                    case "WindowType" when val is string s:
                        WindowType = s;
                        break;
                    case "AtStart" when val is bool b:
                        AtStart = b;
                        break;
                    case "Priority" when val is int i:
                        Priority = i;
                        break;
                }
            }
        }
    }
}