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
                }
                catch { }
            }
        }
    }
}
