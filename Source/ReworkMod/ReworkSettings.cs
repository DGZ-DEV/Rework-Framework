using System;
using System.Collections.Generic;
using Verse;

namespace Rework;

/// <summary>
/// Configuración persistente de Rework Reforjed (bloque 18).
/// Se guarda automáticamente en Config/Mod_*.xml de RimWorld mediante ModSettings.
/// Los enums y el contenedor de mods excluidos son puros BCL (ReworkAPI) para que los
/// mods externos (Caso A) los puedan usar sin depender de Verse.
/// </summary>
public class ReworkSettings : ModSettings
{
    // 18.2 Perfil de rendimiento (enum puro BCL en ReworkAPI)
    public PerfProfile perfProfile = PerfProfile.Equilibrado;

    // 18.3 Perfil de compatibilidad (enum puro BCL en ReworkAPI)
    public CompatProfile compatProfile = CompatProfile.Estandar;

    // 18.4 Activar/desactivar multihilo
    public bool enableMultithreading = true;

    // 18.5 Activar/desactivar backend de serialización automática
    public bool enableAutoScribe = true;

    // 18.6 Activar/desactivar modo seguro proactivo
    public bool enableSafeMode = true;

    // 18.7 Activar/desactivar logs detallados
    public bool verboseLogs = false;

    // 18.8 Configuración por mod externo (mods excluidos)
    public ReworkExcludedMods excludedMods = new();

    // 18.11 XML de Respaldo de Tiempo (ReworkBinaryScribe)
    public bool enableXmlBackup = true;
    public int xmlBackupIntervalMinutes = 15;

    /// <summary>18.9 Guardar / Cargar configuración.</summary>
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref perfProfile, "perfProfile", PerfProfile.Equilibrado);
        Scribe_Values.Look(ref compatProfile, "compatProfile", CompatProfile.Estandar);
        Scribe_Values.Look(ref enableMultithreading, "enableMultithreading", true);
        Scribe_Values.Look(ref enableAutoScribe, "enableAutoScribe", true);
        Scribe_Values.Look(ref enableSafeMode, "enableSafeMode", true);
        Scribe_Values.Look(ref verboseLogs, "verboseLogs", false);
        Scribe_Values.Look(ref enableXmlBackup, "enableXmlBackup", true);
        Scribe_Values.Look(ref xmlBackupIntervalMinutes, "xmlBackupIntervalMinutes", 15);

        List<string>? list = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            list = new List<string>(excludedMods);
        }

        Scribe_Collections.Look(ref list, "excludedMods", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            excludedMods = new ReworkExcludedMods();
            if (list != null)
                excludedMods.AddRange(list);
        }

        if (excludedMods == null)
            excludedMods = new ReworkExcludedMods();

        SyncToConfig();
    }

    /// <summary>Sincroniza los valores de ModSettings con ReworkConfig de ReworkAPI.</summary>
    public void SyncToConfig()
    {
        ReworkConfig.PerfProfile = perfProfile;
        ReworkConfig.CompatProfile = compatProfile;
        ReworkConfig.MultithreadingEnabled = enableMultithreading;
        ReworkConfig.AutoScribeEnabled = enableAutoScribe;
        ReworkConfig.SafeModeEnabled = enableSafeMode;
        ReworkConfig.VerboseLogs = verboseLogs;
        ReworkConfig.XmlBackupEnabled = enableXmlBackup;
        ReworkConfig.XmlBackupIntervalMinutes = xmlBackupIntervalMinutes;
        ReworkConfig.ExcludedMods.Clear();
        if (excludedMods != null)
            ReworkConfig.ExcludedMods.AddRange(excludedMods);
    }

    /// <summary>Sincroniza los valores desde ReworkConfig a ModSettings.</summary>
    public void SyncFromConfig()
    {
        perfProfile = ReworkConfig.PerfProfile;
        compatProfile = ReworkConfig.CompatProfile;
        enableMultithreading = ReworkConfig.MultithreadingEnabled;
        enableAutoScribe = ReworkConfig.AutoScribeEnabled;
        enableSafeMode = ReworkConfig.SafeModeEnabled;
        verboseLogs = ReworkConfig.VerboseLogs;
        enableXmlBackup = ReworkConfig.XmlBackupEnabled;
        xmlBackupIntervalMinutes = ReworkConfig.XmlBackupIntervalMinutes;
        excludedMods.Clear();
        excludedMods.AddRange(ReworkConfig.ExcludedMods);
    }

    /// <summary>18.10 Resetear configuración a valores por defecto.</summary>
    public void Reset()
    {
        ReworkConfig.Reset();
        SyncFromConfig();
    }

    public void ApplyProfile(PerfProfile profile)
    {
        ReworkConfig.ApplyProfile(profile);
        SyncFromConfig();
    }

    public void ApplyProfile(CompatProfile profile)
    {
        ReworkConfig.ApplyProfile(profile);
        SyncFromConfig();
    }
}