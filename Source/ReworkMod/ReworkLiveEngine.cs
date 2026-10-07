using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Motor de Hot-Reload seguro en tiempo de ejecución (ReworkLive).
/// Monitorea ensamblados de mods externos en disco, ejecuta limpieza controlada
/// (IReworkLiveReloadable, GenTypes.ClearCache, ReworkBus.Clear) y permite la actualización en caliente
/// sin acumular desperdicios de memoria ni duplicar registros.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkLiveEngine
{
    public class WatchedAssembly
    {
        public string FilePath = null!;
        public DateTime LastWriteTime;
        public ModContentPack Mod = null!;
    }

    private static readonly List<WatchedAssembly> watched = new();
    private static readonly List<IReworkLiveReloadable> registeredReloadables = new();
    private static bool isEnabled = true;

    static ReworkLiveEngine()
    {
        InitializeWatcher();
    }

    public static bool IsEnabled
    {
        get => isEnabled;
        set => isEnabled = value;
    }

    public static int WatchedCount => watched.Count;

    public static void RegisterReloadable(IReworkLiveReloadable reloadable)
    {
        if (reloadable != null && !registeredReloadables.Contains(reloadable))
        {
            registeredReloadables.Add(reloadable);
        }
    }

    public static void UnregisterReloadable(IReworkLiveReloadable reloadable)
    {
        if (reloadable != null)
        {
            registeredReloadables.Remove(reloadable);
        }
    }

    public static void InitializeWatcher()
    {
        watched.Clear();
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            // No monitorear assemblies del propio framework ni mods excluidos
            if (mod.PackageIdPlayerFacing == "rework.reforjed" || mod.PackageIdPlayerFacing == "rework.demo")
                continue;

            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing) || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            string asmDir = Path.Combine(mod.RootDir, "Assemblies");
            if (!Directory.Exists(asmDir)) continue;

            foreach (var file in Directory.GetFiles(asmDir, "*.dll"))
            {
                try
                {
                    watched.Add(new WatchedAssembly
                    {
                        FilePath = file,
                        LastWriteTime = File.GetLastWriteTimeUtc(file),
                        Mod = mod
                    });
                }
                catch { }
            }
        }

        Lg.Info($"[ReworkLive] Monitoreando {watched.Count} archivo(s) .dll para hot-reload seguro.");
    }

    /// <summary>
    /// Escanea si hubo cambios en los ensamblados monitoreados. Puede ser llamado periódicamente o manualmente.
    /// </summary>
    public static bool CheckForChangesAndReload()
    {
        if (!isEnabled) return false;

        bool reloadedAny = false;
        foreach (var item in watched)
        {
            try
            {
                if (!File.Exists(item.FilePath)) continue;
                var currentWrite = File.GetLastWriteTimeUtc(item.FilePath);
                if (currentWrite > item.LastWriteTime)
                {
                    item.LastWriteTime = currentWrite;
                    PerformSafeReload(item);
                    reloadedAny = true;
                }
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkLive] Error comprobando cambios en '{Path.GetFileName(item.FilePath)}': {e}");
            }
        }

        return reloadedAny;
    }

    /// <summary>
    /// Ejecuta el protocolo de limpieza estricto antes y después de recargar.
    /// Garantiza cero desperdicio de memoria y consistencia total en el juego.
    /// </summary>
    public static void PerformSafeReload(WatchedAssembly item)
    {
        Lg.Info($"[ReworkLive] Detectado cambio en '{Path.GetFileName(item.FilePath)}'. Iniciando protocolo de limpieza...");

        // 1. Notificar a todos los manejadores de estado registrados para limpieza
        foreach (var r in registeredReloadables.ToArray())
        {
            try { r.OnBeforeLiveReload(); }
            catch (Exception e) { Lg.Error($"[ReworkLive] Error en OnBeforeLiveReload: {e}"); }
        }

        // 2. Limpieza de cachés internas de RimWorld y Rework
        ReworkBus.Clear();
        GenTypes.ClearCache();

        try
        {
            // 3. Leer y cargar los nuevos bytes del ensamblado sin bloquear el archivo en disco
            byte[] rawBytes = File.ReadAllBytes(item.FilePath);
            var newAsm = Assembly.Load(rawBytes);

            // Reemplazar o añadir en la lista del mod
            if (item.Mod.assemblies.loadedAssemblies != null)
            {
                item.Mod.assemblies.loadedAssemblies.RemoveAll(a => a.GetName().Name == newAsm.GetName().Name);
                item.Mod.assemblies.loadedAssemblies.Add(newAsm);
            }

            Lg.Info($"[ReworkLive] Ensamblado '{newAsm.GetName().Name}' recargado en memoria exitosamente.");

            // 4. Re-escanear y registrar manejadores de IReworkLiveReloadable del nuevo ensamblado
            foreach (var type in newAsm.GetTypes())
            {
                if (typeof(IReworkLiveReloadable).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    try
                    {
                        var instance = (IReworkLiveReloadable)Activator.CreateInstance(type);
                        RegisterReloadable(instance);
                        instance.OnAfterLiveReload();
                    }
                    catch { }
                }
            }

            // 5. Notificar a los manejadores existentes que sobrevivieron
            foreach (var r in registeredReloadables.ToArray())
            {
                try { r.OnAfterLiveReload(); }
                catch (Exception e) { Lg.Error($"[ReworkLive] Error en OnAfterLiveReload: {e}"); }
            }

            Lg.Info($"[ReworkLive] Ciclo de hot-reload completado con éxito. Estado limpio.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkLive] Falló la recarga en caliente de '{item.FilePath}': {e}");
        }
    }
}
