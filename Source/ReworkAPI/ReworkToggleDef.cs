using System;

namespace Rework;

/// <summary>
/// Declara variantes intercambiables para los ThingDefs indicados: el jugador
/// elige la variante desde un gizmo (menú flotante) y el objeto cambia de def
/// conservando HP, calidad y equipo/portador.
///
/// Equivale al CompToggleDef de JecsTools, sin comps ni ITab: el grupo se
/// declara por lista explícita (no por nomenclatura `_TOGGLEDEF_`, frágil
/// ante renombres) y la UI es un gizmo con menú (no una tarjeta con radio
/// buttons: menos código, mismo efecto).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkToggleDefAttribute : Attribute
{
    /// <summary>Id único del grupo de variantes.</summary>
    public string GroupId { get; }

    /// <summary>defNames de los ThingDefs del grupo, separados por coma (2+).</summary>
    public string Variants { get; set; } = "";

    /// <summary>Etiqueta del gizmo (default "Cambiar variante").</summary>
    public string? GizmoLabel { get; set; }

    public ReworkToggleDefAttribute(string groupId)
    {
        GroupId = groupId;
    }
}

/// <summary>
/// Registro de grupos de variantes ([ReworkToggleDef]).
/// </summary>
public static class ReworkToggleDefRegistry
{
    public class ToggleGroup
    {
        public string GroupId = "";
        public string[] VariantDefNames = Array.Empty<string>();
        public object[] VariantDefs = Array.Empty<object>();
        public string GizmoLabel = "Cambiar variante";
    }

    private static readonly System.Collections.Generic.Dictionary<string, ToggleGroup> groups =
        new(System.StringComparer.OrdinalIgnoreCase);

    // Índice inverso: defName -> grupo (para encontrar el grupo desde el objeto).
    private static readonly System.Collections.Generic.Dictionary<string, ToggleGroup> byDef =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra un grupo (lo llama el registrador del framework).
    /// El índice inverso se construye con VariantDefNames (strings): la API
    /// no referencia tipos del juego (0ReworkAPI es BCL pura).
    /// </summary>
    public static void Register(ToggleGroup group)
    {
        if (group == null || string.IsNullOrEmpty(group.GroupId)) return;
        groups[group.GroupId] = group;
        foreach (var name in group.VariantDefNames)
        {
            if (!string.IsNullOrEmpty(name))
                byDef[name] = group;
        }
    }

    /// <summary>Grupo por id, o null.</summary>
    public static ToggleGroup? Get(string groupId)
    {
        groups.TryGetValue(groupId, out var g);
        return g;
    }

    /// <summary>Grupo al que pertenece un defName, o null si no es variante.</summary>
    public static ToggleGroup? GetByDef(string defName)
    {
        byDef.TryGetValue(defName, out var g);
        return g;
    }

    /// <summary>Número de grupos registrados.</summary>
    public static int Count => groups.Count;
}
