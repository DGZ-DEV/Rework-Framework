using System;

namespace Rework;

/// <summary>
/// Registra automáticamente un IncidentDef de RimWorld en runtime para la clase IncidentWorker decorada.
/// Permite crear eventos e incidentes en C# sin escribir archivos XML de Defs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkIncidentAttribute : Attribute
{
    /// <summary>Nombre único del IncidentDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Categoría del incidente (ej: "Misc", "ThreatSmall", "ThreatBig", "Special"). Por defecto "Misc".</summary>
    public string Category { get; set; } = "Misc";

    /// <summary>Probabilidad base del incidente (baseChance). Por defecto 1.0f.</summary>
    public float BaseChance { get; set; } = 1.0f;

    /// <summary>Target tag principal (ej: "Map_PlayerHome", "World"). Por defecto "Map_PlayerHome".</summary>
    public string TargetTag { get; set; } = "Map_PlayerHome";

    /// <summary>Título de la carta de notificación si el incidente envía carta.</summary>
    public string? LetterLabel { get; set; }

    /// <summary>Texto descriptivo de la carta.</summary>
    public string? LetterText { get; set; }

    public ReworkIncidentAttribute(string defName)
    {
        DefName = defName;
    }
}
