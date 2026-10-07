using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método booleano para definir una alerta visual en la esquina superior derecha del juego
/// sin requerir XML (AlertDef) ni Alert subclasificado manualmente.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkAlertAttribute : Attribute
{
    public string Name { get; }
    public string Label { get; set; } = "";
    public string Explanation { get; set; } = "";
    public int Priority { get; set; } = 0; // 0 = Medio, 1 = Alto, 2 = Crítico

    public ReworkAlertAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Registro y despachador de Alertas Declarativas de Rework.
/// </summary>
public static class ReworkAlertRegistry
{
    public class AlertEntry
    {
        public string Name = "";
        public string Label = "";
        public string Explanation = "";
        public int Priority;
        public MethodInfo ConditionMethod = null!;
    }

    private static readonly List<AlertEntry> alerts = new();

    public static void Register(string name, string label, string explanation, int priority, MethodInfo condition)
    {
        alerts.Add(new AlertEntry
        {
            Name = name,
            Label = label,
            Explanation = explanation,
            Priority = priority,
            ConditionMethod = condition
        });
    }

    public static IReadOnlyList<AlertEntry> AllAlerts => alerts;
}
