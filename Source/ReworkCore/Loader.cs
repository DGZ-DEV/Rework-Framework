using System;
using System.Linq;
using System.Reflection;
using Rework.Data;
using Verse;

namespace Rework.Core;

/// <summary>
/// Orquestación del reload. Diseño equivalente a Prepatcher Loader.cs
/// (misma arquitectura; sin Harmony y sin Prestarter por ahora).
///
/// FLUJO DE DOS PASADAS (modelo Prepatcher):
///   Pasada 1 → Loader.Reload() (este archivo) + Thread.CurrentThread.Abort()
///              en ReworkMod; el juego re-arranca su secuencia de arranque.
///   Pasada 2 → DataStore.startedOnce == true → ReworkMod no reescribe nada.
/// </summary>
internal static class Loader
{
    internal static Assembly? origAsm;
    internal static Assembly? newAsm;
    internal static volatile bool restartGame;

    internal static void Reload()
    {
        try
        {
            Lg.Info("Pasada 1: recogiendo ensamblados");
            DoReload();
            Lg.Info("Reload completado. El juego re-arrancará la secuencia de arranque.");
            restartGame = true;
        }
        catch (Exception e)
        {
            // MODO SEGURO (mejora #5 de Rework sobre Prepatcher):
            // revertimos las flags ref_only y NO abortamos el hilo; el juego
            // continúa arrancando con los ensamblados ORIGINALES.
            // Prepatcher, en su lugar, abre el gestor de mods
            // (Loader.cs → catch → UnsetRefonlys + MinimalInit).
            Lg.Error($"Error fatal durante el reload: {e}");
            UnsafeAssembly.UnsetRefonlys();
            restartGame = false;
        }
    }

    private static void DoReload()
    {
        origAsm = typeof(Game).Assembly;

        var set = new AssemblySet();

        // Assembly-CSharp: el objetivo del rewriter. Ya está cargada
        // (typeof(Game).Assembly) pero todavía no usada: por eso el constructor
        // de Mod es el punto de enganche válido (igual que Prepatcher).
        set.AddAssembly("RimWorld", AssemblyCollector.AssemblyCSharp, null, typeof(Game).Assembly);

        // Ensamblados de sistema: se añaden solo para resolver referencias;
        // nunca se parchean ni se recargan (AllowPatches=false).
        foreach (var (friendlyName, path) in AssemblyCollector.SystemAssemblyPaths())
        {
            try
            {
                if (AssemblyName.GetAssemblyName(path).Name == AssemblyCollector.AssemblyCSharp)
                    continue; // ya está en el set

                var added = set.AddAssembly("System", friendlyName, path, null);
                added.AllowPatches = false;
            }
            catch (Exception e)
            {
                Lg.Error($"No se pudo añadir el ensamblado de sistema {friendlyName}: {e.Message}");
            }
        }

        // Ensamblados de mods: se añaden todos los activos (para resolver referencias).
        // El gate de ProcessAttributes (Fase 3, Caso A) marca para PROCESAR ATRIBUTOS
        // a los mods que se suben al ecosistema Rework — es decir, los que referencian
        // la API (0ReworkAPI) — además de los propios de Rework. Así cualquier mod puede
        // declarar [ReworkField]/[ReworkPatch] y Rework los procesa igual que los suyos.
        // Un mod que no use la API no se toca (solo resuelve referencias).
        foreach (var (mod, asm) in AssemblyCollector.ModAssemblies())
        {
            try
            {
                var name = asm.GetName().Name;

                // Caso B: NO se descartan duplicados por nombre. Si dos mods traen un
                // ensamblado con el mismo nombre simple, ambos entran al set (el
                // primero es el "principal" para el resolver) y se procesan/recargan
                // por su propia ruta. Sin este cambio, el duplicado se perdía en silencio.
                if (set.HasAssembly(name))
                    Lg.Info($"Aviso (Caso B): múltiples mods con el ensamblado '{name}'; se procesan ambos.");

                var added = set.AddAssembly(mod.Name, $"(mod {mod.Name}) {name}", null, asm);

                var isExcluded = ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                    || ReworkConfig.ExcludedMods.Contains(mod.Name)
                    || ReworkConfig.ExcludedMods.Contains(name);

                if (isExcluded || name.StartsWith("0Harmony", StringComparison.Ordinal) || name.StartsWith("HarmonySharedState", StringComparison.Ordinal))
                {
                    added.ProcessAttributes = false;
                    added.AllowPatches = false;
                    Lg.Info($"Mod/Librería {mod.Name} ({name}) protegida en modo solo-lectura (aislamiento de compatibilidad).");
                    continue;
                }

                var isReworkOwned =
                    name.StartsWith("Rework", StringComparison.Ordinal) && !name.EndsWith("Data");
                var referencesReworkApi =
                    asm.GetReferencedAssemblies().Any(ra => ra.Name == AssemblyCollector.ReworkApiAssembly);
                added.ProcessAttributes = isReworkOwned || referencesReworkApi;
                if (referencesReworkApi && !isReworkOwned)
                    Lg.Info($"Mod integrado {mod.Name} ({name}): procesando sus [ReworkField]/[ReworkPatch].");
            }
            catch (Exception e)
            {
                Lg.Error($"No se pudo añadir el ensamblado del mod {mod.Name}: {e.Message}");
            }
        }

        // Pipeline de reescritura (MVP: adición de campos)
        GameProcessing.Process(set);

        // Swap: serializar → refonly originales → Assembly.Load(bytes)
        Reloader.Reload(set, LoadAssembly);
    }

    private static void LoadAssembly(ModifiableAssembly asm)
    {
        Lg.Verbose($"Cargando: {asm.FriendlyName}");

        var loadedAssembly = Assembly.Load(asm.Bytes!);
        if (loadedAssembly.GetName().Name == AssemblyCollector.AssemblyCSharp)
        {
            newAsm = loadedAssembly;
            // Pegamento: cualquier resolución por nombre de Assembly-CSharp devuelve
            // el ensamblado NUEVO (el original quedó marcado refonly).
            // (Prepatcher Loader.LoadAssembly.)
            AppDomain.CurrentDomain.AssemblyResolve += (_, _) => loadedAssembly;
        }
        else if (asm.SourceAssembly!.Location is var loc && !string.IsNullOrEmpty(loc))
        {
            // Registrar la copia NUEVA bajo la ruta del original para que la pasada 2
            // (ModAssemblyHandler.ReloadAll → ReworkLoader.LoadFile) la devuelva a ella
            // y no al original refonly.
            DataStore.AssembliesByPath[loc] = loadedAssembly;
        }
    }
}
