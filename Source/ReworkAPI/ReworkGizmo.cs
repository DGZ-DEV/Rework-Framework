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
        public MethodInfo Method = null!;
    }

    private static readonly List<GizmoEntry> gizmos = new();

    public static void Register(string targetType, string label, string desc, MethodInfo method)
    {
        gizmos.Add(new GizmoEntry
        {
            TargetTypeName = targetType,
            Label = label,
            Description = desc,
            Method = method
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
