using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rework.Data;
using Verse;

namespace Rework.Core;

/// <summary>
/// Ejecuta UNA vez, al final de la pasada 2 (juego ya operativo, campos y parches
/// aplicados), todos los métodos estáticos marcados con [ReworkInit] de cualquier
/// mod activo. Equivalente funcional a los Init patches / [PrepatcherInit] de
/// Prepatcher.
///
/// Orden de ejecución (cross-mod): Priority desc → orden de carga del mod
/// (RunningModsListForReading) → nombre (determinista).
/// Cada init va envuelto: si lanza, se reporta con ErrorPrinter al Rework.log y se
/// sigue con el resto (no revienta el boot).
/// </summary>
public static class InitRunner
{
    public static void Run()
    {
        if (DataStore.InitRan)
            return;
        DataStore.InitRan = true;

        // Índice de carga de cada ensamblado = orden del mod en la lista de mods
        // (desempate a igual prioridad: gana el mod que carga antes).
        var loadOrder = new Dictionary<Assembly, int>();
        int idx = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
            foreach (var a in mod.assemblies.loadedAssemblies)
                if (!loadOrder.ContainsKey(a))
                    loadOrder[a] = idx++;

        var inits = new List<(int prio, int modOrder, string name, MethodInfo mi)>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                continue;
            }

            foreach (var t in types)
            {
                if (t == null)
                    continue;
                foreach (var m in t.GetMethods(
                             BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        var attr = m.GetCustomAttribute<Rework.ReworkInitAttribute>(false);
                        if (attr == null)
                            continue;
                        inits.Add((attr.Priority,
                                   loadOrder.TryGetValue(asm, out var o) ? o : int.MaxValue,
                                   (m.DeclaringType?.FullName ?? "?") + "." + m.Name,
                                   m));
                    }
                    catch
                    {
                        // Un atributo malformado no debe romper el descubrimiento.
                    }
                }
            }
        }

        foreach (var (prio, _, name, mi) in inits
                     .OrderByDescending(i => i.prio)
                     .ThenBy(i => i.modOrder)
                     .ThenBy(i => i.name, StringComparer.Ordinal))
        {
            // 17.8/17.9 — Modo desarrollador: ReworkDevMode.OnlyMod / OnlyPatch filtran
            // los inits que se ejecutan (para aislar un fallo o revertir uno sin quitarlo
            // del mod). El framework registra cuál se salta.
            if (!ReworkDevMode.Filter(mi.DeclaringType?.Namespace ?? "?",
                (mi.DeclaringType?.FullName ?? "?") + "." + mi.Name))
            {
                Lg.Info($"Init SALTO por ReworkDevMode (OnlyPatch/OnlyMod): {name}");
                continue;
            }

            Lg.Info($"Init (prio {prio}): {name}");
            try
            {
                mi.Invoke(null, null);
            }
            catch (Exception e)
            {
                ErrorPrinter.Print($"Init falló: {name}", e.InnerException ?? e);
            }
        }

        if (inits.Count == 0)
            Lg.Info("InitRunner: ningún [ReworkInit] encontrado.");
    }
}