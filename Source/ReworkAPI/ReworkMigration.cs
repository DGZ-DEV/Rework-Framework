using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca un método para migrar esquemas de datos o campos [ReworkField] obsoletos entre versiones de un mod.
/// Evita que partidas guardadas antiguas se corrompan tras actualizaciones de mods.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkMigrationAttribute : Attribute
{
    public string ModId { get; }
    public string FromVersion { get; }
    public string ToVersion { get; }

    public ReworkMigrationAttribute(string modId, string fromVersion, string toVersion)
    {
        ModId = modId;
        FromVersion = fromVersion;
        ToVersion = toVersion;
    }
}

/// <summary>
/// Motor de migraciones seguras de partidas de Rework.
/// </summary>
public static class ReworkMigration
{
    public class MigrationStep
    {
        public string ModId = "";
        public string FromVersion = "";
        public string ToVersion = "";
        public Action<object, Dictionary<string, object>> StepAction = null!;
    }

    private static readonly List<MigrationStep> steps = new();

    public static void RegisterStep(string modId, string fromVer, string toVer, Action<object, Dictionary<string, object>> action)
    {
        steps.Add(new MigrationStep { ModId = modId, FromVersion = fromVer, ToVersion = toVer, StepAction = action });
    }

    public static void ExecuteMigrations(string modId, string currentVersion, string targetVersion, object context, Dictionary<string, object> data)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (string.Equals(s.ModId, modId, StringComparison.OrdinalIgnoreCase) && s.FromVersion == currentVersion)
            {
                try
                {
                    s.StepAction?.Invoke(context, data);
                    currentVersion = s.ToVersion;
                    SetStoredVersion(modId, currentVersion);
                }
                catch { }
            }
        }
    }

    /// <summary>Clave en ReworkWorldStore con la versión de migración aplicada por mod.</summary>
    private const string VersionKeyPrefix = "rework:migration:";

    /// <summary>Devuelve la versión de migración persistida del mod, o "0" si aún no se migró.</summary>
    public static string GetStoredVersion(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return "0";
        return ReworkWorldStore.Get(VersionKeyPrefix + modId, "0") ?? "0";
    }

    /// <summary>Persiste la versión de migración alcanzada por el mod (en ReworkWorldStore → .rwbin).</summary>
    public static void SetStoredVersion(string modId, string version)
    {
        if (string.IsNullOrEmpty(modId)) return;
        ReworkWorldStore.Set(VersionKeyPrefix + modId, version ?? "0");
    }

    /// <summary>
    /// Callback de log opcional (ReworkMod lo conecta a Lg.Info). La API pura no
    /// puede depender de Rework.Core → se notifica por delegado.
    /// </summary>
    public static Action<string, string, string>? LogHook { get; set; }

    /// <summary>
    /// Ejecuta la cadena de pasos pendientes de TODOS los mods que registraron
    /// migraciones (desde su versión persistida hasta el último ToVersion declarado).
    /// Se invoca automáticamente al iniciar/cargar partida (ver RuntimeHooks
    /// PublishLifecycleEvent → ReworkMigrationRun).
    /// </summary>
    public static void RunAllMigrations(object context, Dictionary<string, object> data)
    {
        var modIds = new List<string>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (!modIds.Contains(steps[i].ModId))
                modIds.Add(steps[i].ModId);
        }

        foreach (var modId in modIds)
        {
            string current = GetStoredVersion(modId);
            string target = "0";
            for (int i = 0; i < steps.Count; i++)
                if (string.Equals(steps[i].ModId, modId, StringComparison.OrdinalIgnoreCase))
                    target = steps[i].ToVersion;

            if (current == target && target != "0")
                continue;

            ExecuteMigrations(modId, current, target, context, data);
            string after = GetStoredVersion(modId);
            if (after != current)
                LogHook?.Invoke(modId, current, after);
        }
    }
}
