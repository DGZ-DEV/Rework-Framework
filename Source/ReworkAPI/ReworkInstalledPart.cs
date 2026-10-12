using System;

namespace Rework;

/// <summary>
/// Declara una pieza instalable en los ThingDefs indicados: el jugador ordena
/// instalarla sobre un objetivo (pawn, edificio con slots o contenedor) con
/// trabajo de Construction, y desinstalarla de vuelta al suelo.
///
/// Equivale al CompInstalledPart de JecsTools (WIP en el original), sin comps
/// ni XML: la pieza se marca por defName y el framework pone los JobDrivers,
/// los jobs y el gizmo.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkInstalledPartAttribute : Attribute
{
    /// <summary>defName del ThingDef de la pieza (debe existir).</summary>
    public string PartDefName { get; }

    /// <summary>defNames de ThingDefs donde se puede instalar, separados por coma (vacío = cualquiera).</summary>
    public string? AllowedOn { get; set; }

    /// <summary>Trabajo de Construction para instalar (ticks base).</summary>
    public int WorkToInstall { get; set; } = 500;

    /// <summary>Trabajo de Construction para desinstalar (ticks base).</summary>
    public int WorkToUninstall { get; set; } = 500;

    public ReworkInstalledPartAttribute(string partDefName)
    {
        PartDefName = partDefName;
    }
}

/// <summary>
/// Registro de piezas instalables ([ReworkInstalledPart]).
/// </summary>
public static class ReworkInstalledPartRegistry
{
    public class PartData
    {
        public string PartDefName = "";
        public string[] AllowedOnDefNames = Array.Empty<string>();
        public int WorkToInstall = 500;
        public int WorkToUninstall = 500;
    }

    private static readonly System.Collections.Generic.Dictionary<string, PartData> parts =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra una pieza (lo llama el registrador del framework).
    /// </summary>
    public static void Register(PartData data)
    {
        if (data == null || string.IsNullOrEmpty(data.PartDefName)) return;
        parts[data.PartDefName] = data;
    }

    /// <summary>Datos por defName de la pieza, o null si no es instalable.</summary>
    public static PartData? Get(string partDefName)
    {
        parts.TryGetValue(partDefName, out var data);
        return data;
    }

    /// <summary>Número de piezas registradas.</summary>
    public static int Count => parts.Count;
}
