using System;
using System.Collections.Generic;
using System.Linq;

namespace Rework.Core;

/// <summary>
/// Ejecuta el swap de ensamblados: serializar → marcar originales como
/// reflection-only → cargar los duplicados desde memoria.
/// (Equivalente a Prepatcher Source/Implementation/Process/Reloader.cs.)
/// </summary>
internal static class Reloader
{
    internal static void Reload(AssemblySet set, Action<ModifiableAssembly> loadAssemblyAction)
    {
        PropagateNeedsReload(set);

        // Los ensamblados del motor (UnityEngine*, Unity, etc.) nunca participan en el
        // swap: se añadieron solo para resolver referencias (AllowPatches=false) y su
        // flag nativa no debe tocarse. Excluirlos aquí es el "contexto seguro" del
        // bloque 1 (1.2/1.3).
        var toSwap = set.AllAssemblies
            .Where(a => a.NeedsReload && a.SourceAssembly != null
                        && !ModifiableAssembly.IsUnityEngineAssembly(a.SourceAssembly)
                        && !IsBarrierAssembly(a))
            .ToList();

        // §47 — INVARIANTE DE BARRERA: 0ReworkData NUNCA se intercambia. DataStore
        // cruza la barrera del reload precisamente porque su ensamblado no se
        // recarga; si entrara al swap (por cualquier marcado de Modified/
        // NeedsReload), cada ciclo de arranque cargaría una copia nueva con
        // estáticos FRESCOS → startedOnce/ReworkLogSessionOpened se perderían →
        // BUCLE DE ARRANQUE infinito (verificado empíricamente en §47). El
        // Reloader es la última línea de defensa: aunque algo lo marque, aquí no
        // se intercambia, y se loguea el aviso para poder cazar al escritor.
        foreach (var barrera in set.AllAssemblies.Where(a => a.NeedsReload && IsBarrierAssembly(a)))
            Lg.Error($"[Reload] {barrera.FriendlyName} quedó marcado para recarga siendo un ensamblado " +
                     "de BARRERA (0ReworkData): NO se intercambia (invariante §47). Algo lo marcó como " +
                     "modificado en la pasada 1 — revisar la línea 'Reload: intercambiando' y ERRORES.md §47.");

        // §47 — Diagnóstico: qué entra al swap en este ciclo (los nombres dicen
        // quién fue marcado Modified/NeedsReload por cada procesador).
        Lg.Info($"Reload: intercambiando {toSwap.Count} ensamblado(s): " +
                string.Join(", ", toSwap.Select(a => a.FriendlyName)) + ".");

        var skippedEngine = set.AllAssemblies
            .Count(a => a.NeedsReload && a.SourceAssembly != null
                        && ModifiableAssembly.IsUnityEngineAssembly(a.SourceAssembly));
        if (skippedEngine > 0)
            Lg.Info($"Reload: {skippedEngine} ensamblado(s) del motor excluidos del swap (solo lectura).");

        // 1) Serializar a memoria los ensamblados que se van a recargar
        foreach (var toReload in toSwap)
            toReload.SerializeToByteArray();

        // 2) Marcar refonly TODOS los originales ANTES de cargar ningún duplicado:
        //    si Mono encuentra dos ensamblados con el mismo nombre, el buscador
        //    interno salta los marcados como reflection-only (UnsafeAssembly).
        foreach (var toReload in toSwap)
            toReload.SetSourceRefOnly();

        // 3) Cargar los duplicados desde bytes
        foreach (var toReload in toSwap)
            loadAssemblyAction(toReload);
    }

    /// <summary>
    /// §47 — ¿Es un ensamblado de BARRERA (nunca se intercambia)? 0ReworkData
    /// porta los estáticos que cruzan el reload (startedOnce, logs, mapas de
    /// recarga): recargarlo resetea TODO el estado de control del framework.
    /// </summary>
    private static bool IsBarrierAssembly(ModifiableAssembly a) =>
        a.AsmDefinition.Name.Name == "0ReworkData";

    /// <summary>
    /// Cualquier ensamblado que dependa de uno que se recarga, también se recarga
    /// (BFS por dependientes — Util.BFS; Prepatcher Reloader.PropagateNeedsReload).
    /// </summary>
    private static void PropagateNeedsReload(AssemblySet set)
    {
        var dependants = set.AllAssembliesToDependants();

        foreach (var asm in Util.BFS(
            set.AllAssemblies.Where(a => a.NeedsReload),
            a => dependants.TryGetValue(a, out var d) ? d : Enumerable.Empty<ModifiableAssembly>()))
        {
            asm.SetNeedsReload();
        }
    }
}
