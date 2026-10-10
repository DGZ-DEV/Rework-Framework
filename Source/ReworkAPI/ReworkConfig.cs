using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>Perfiles de rendimiento (bloque 18.2). Puro BCL (enum).</summary>
public enum PerfProfile
{
    Equilibrado,
    MaximoRendimiento,
    AhorroMemoria
}

/// <summary>Perfiles de compatibilidad (bloque 18.3). Puro BCL (enum).</summary>
public enum CompatProfile
{
    Estandar,
    Estricto,
    Permisivo
}

/// <summary>18.8 — configuración por mod externo: lista de mods excluidos del parcheo.
/// Se expone como un contenedor puro (BCL) para que tanto el framework como los mods
/// externos la lean/escriban sin depender de Verse.</summary>
public sealed class ReworkExcludedMods : List<string>
{
    public void Toggle(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return;
        var i = IndexOf(modId);
        if (i >= 0) RemoveAt(i);
        else Add(modId);
    }
}

/// <summary>
/// Configuración en memoria accesible globalmente por mods y el framework (bloque 18).
/// Puro BCL (sin dependencias de Verse ni Unity).
/// </summary>
public static class ReworkConfig
{
    public static PerfProfile PerfProfile { get; set; } = PerfProfile.Equilibrado;
    public static CompatProfile CompatProfile { get; set; } = CompatProfile.Estandar;
    public static bool MultithreadingEnabled { get; set; } = true;
    public static bool AutoScribeEnabled { get; set; } = true;
    public static bool SafeModeEnabled { get; set; } = true;
    public static bool VerboseLogs { get; set; } = false;
    public static bool XmlBackupEnabled { get; set; } = true;
    public static int XmlBackupIntervalMinutes { get; set; } = 15;
    public static ReworkExcludedMods ExcludedMods { get; } = new();

    public static void Reset()
    {
        PerfProfile = PerfProfile.Equilibrado;
        CompatProfile = CompatProfile.Estandar;
        MultithreadingEnabled = true;
        AutoScribeEnabled = true;
        SafeModeEnabled = true;
        VerboseLogs = false;
        ExcludedMods.Clear();
    }

    public static void ApplyProfile(PerfProfile profile)
    {
        PerfProfile = profile;
        switch (profile)
        {
            case PerfProfile.MaximoRendimiento:
                MultithreadingEnabled = true;
                VerboseLogs = false;
                break;
            case PerfProfile.AhorroMemoria:
                MultithreadingEnabled = false;
                VerboseLogs = false;
                break;
            case PerfProfile.Equilibrado:
                MultithreadingEnabled = true;
                break;
        }
    }

    public static void ApplyProfile(CompatProfile profile)
    {
        CompatProfile = profile;
        switch (profile)
        {
            case CompatProfile.Estricto:
                SafeModeEnabled = true;
                break;
            case CompatProfile.Permisivo:
                SafeModeEnabled = false;
                break;
            case CompatProfile.Estandar:
                SafeModeEnabled = true;
                break;
        }
    }
}