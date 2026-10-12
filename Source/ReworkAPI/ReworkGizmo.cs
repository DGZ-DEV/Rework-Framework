using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método estático como un botón de acción (Gizmo) inyectable en entidades del juego
/// (Pawns, Edificios, etc.) al ser seleccionados, sin subclasificar Gizmo ni ThingComp.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkGizmoAttribute : Attribute
{
    public string TargetType { get; set; } = "Pawn";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string? IconPath { get; set; }

    public ReworkGizmoAttribute(string label)
    {
        Label = label;
    }
}

/// <summary>
/// Registro y despachador de Gizmos declarativos de Rework.
/// </summary>
public static class ReworkGizmoRegistry
{
    public class GizmoEntry
    {
        public string TargetTypeName = "Pawn";
        public string Label = "";
        public string Description = "";
        public string? IconPath;
        public MethodInfo Method = null!;

        /// <summary>
        /// Condición de visibilidad (opcional): si es null, el gizmo aparece
        /// siempre para el tipo; si no, solo cuando devuelve true para el
        /// objetivo. Lo usa el framework (p. ej. el gizmo de variantes solo
        /// sale si el objetivo es variante). Los mods declaran sin condición.
        /// </summary>
        public Func<object, bool>? VisibleIf;
    }

    private static readonly List<GizmoEntry> gizmos = new();

    /// <summary>Número de gizmos registrados (para early-out en el dispatch).</summary>
    public static int Count => gizmos.Count;

    public static void Register(string targetType, string label, string desc, MethodInfo method, string? iconPath = null)
    {
        gizmos.Add(new GizmoEntry
        {
            TargetTypeName = targetType,
            Label = label,
            Description = desc,
            IconPath = iconPath,
            Method = method
        });
    }

    /// <summary>
    /// Registra con condición de visibilidad (uso del framework).
    /// </summary>
    public static void RegisterConditional(string targetType, string label, string desc, MethodInfo method, Func<object, bool>? visibleIf, string? iconPath = null)
    {
        gizmos.Add(new GizmoEntry
        {
            TargetTypeName = targetType,
            Label = label,
            Description = desc,
            IconPath = iconPath,
            Method = method,
            VisibleIf = visibleIf,
        });
    }

    public static IReadOnlyList<GizmoEntry> GetGizmosForType(string typeName)
    {
        var res = new List<GizmoEntry>();
        for (int i = 0; i < gizmos.Count; i++)
        {
            if (string.Equals(gizmos[i].TargetTypeName, typeName, StringComparison.OrdinalIgnoreCase))
                res.Add(gizmos[i]);
        }
        return res;
    }
}
