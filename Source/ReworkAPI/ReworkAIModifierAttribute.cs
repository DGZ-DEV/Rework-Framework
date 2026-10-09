using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca un método para modificar dinámicamente la prioridad o selección de tareas (IA) de un colono.
/// Permite alterar el comportamiento cognitivo de los pawns sin reimplementar JobGivers pesados.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkAIModifierAttribute : Attribute
{
    /// <summary>
    /// Tipo de trabajo o nombre del JobDef sobre el que actúa (opcional; null = cualquier job).
    /// </summary>
    public string? TargetJobDef { get; set; }

    /// <summary>
    /// Prioridad del modificador al evaluar reglas (mayor = se evalúa primero).
    /// </summary>
    public int Priority { get; set; } = 0;

    public ReworkAIModifierAttribute(string? targetJobDef = null)
    {
        TargetJobDef = targetJobDef;
    }
}

/// <summary>
/// Registro y despachador de modificadores de IA.
/// </summary>
public static class ReworkAIRegistry
{
    public delegate float AIModifierDelegate(object pawn, string jobDefName, float currentPriority);

    private static readonly List<(string? jobDef, int priority, AIModifierDelegate handler)> modifiers = new();

    /// <summary>Número de modificadores IA registrados (para early-out en el dispatch).</summary>
    public static int ModifierCount => modifiers.Count;

    public static void RegisterModifier(string? jobDef, int priority, AIModifierDelegate handler)
    {
        modifiers.Add((jobDef, priority, handler));
        modifiers.Sort((a, b) => b.priority.CompareTo(a.priority));
    }

    /// <summary>
    /// Evalúa la prioridad ajustada de un trabajo para un pawn pasando por todos los modificadores activos.
    /// </summary>
    public static float EvaluateJobPriority(object pawn, string jobDefName, float basePriority)
    {
        float priority = basePriority;
        for (int i = 0; i < modifiers.Count; i++)
        {
            var (jobDef, _, handler) = modifiers[i];
            if (jobDef == null || string.Equals(jobDef, jobDefName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    priority = handler(pawn, jobDefName, priority);
                }
                catch
                {
                    // Protección pasiva: si un modificador falla, preserva la prioridad actual
                }
            }
        }
        return priority;
    }
}
