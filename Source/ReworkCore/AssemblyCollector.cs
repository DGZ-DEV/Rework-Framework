using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Recolección de los ensamblados que participan en el reload.
/// (Equivalente a Prepatcher Source/Implementation/Process/AssemblyCollector.cs.)
/// </summary>
internal static class AssemblyCollector
{
    internal const string AssemblyCSharp = "Assembly-CSharp";

    /// <summary>Nombre del ensamblado de la API (los mods que lo referencian "se suben"
    /// al ecosistema Rework y se procesan sus [ReworkField]/[ReworkPatch]).</summary>
    internal const string ReworkApiAssembly = "0ReworkAPI";

    /// <summary>
    /// Ensamblados de sistema de la carpeta Managed del juego (UnityEngine*, etc.).
    /// Se añaden al set solo para resolver referencias; nunca se parchean.
    /// </summary>
    internal static IEnumerable<(string friendlyName, string path)> SystemAssemblyPaths()
    {
        var managed = Path.Combine(Application.dataPath, Util.ManagedFolderOS());
        foreach (var dll in Directory.GetFiles(managed, "*.dll"))
            yield return ($"(System) {Path.GetFileName(dll)}", dll);
    }

    /// <summary>
    /// Ensamblados de los mods ACTIVOS con su ModContentPack de origen.
    /// Se leen de LoadedModManager (no de ModsConfig.xml a mano — misma decisión
    /// que Prepatcher: usar la API de mods del juego).
    /// </summary>
    internal static IEnumerable<(ModContentPack mod, Assembly asm)> ModAssemblies()
    {
        foreach (var m in LoadedModManager.RunningModsListForReading)
            foreach (var a in m.assemblies.loadedAssemblies)
                yield return (m, a);
    }
}
