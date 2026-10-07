using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Motor de Mutación Dinámica en Caliente.
/// Permite modificar campos de Defs cargados durante la partida (ej: por tecnologías o eventos),
/// manteniendo un historial completo de cambios y permitiendo revertirlos al valor original en cualquier momento.
/// </summary>
public static class ReworkDynamicMutate
{
    public class DynamicMutationEntry
    {
        public object TargetDef { get; set; } = null!;
        public FieldInfo Field { get; set; } = null!;
        public object? OriginalValue { get; set; }
        public object? CurrentValue { get; set; }
    }

    private static readonly Dictionary<string, DynamicMutationEntry> activeMutations = new();

    /// <summary>
    /// Aplica una mutación dinámica en caliente a un campo de un Def cargado.
    /// Devuelve true si la mutación fue exitosa.
    /// </summary>
    public static bool ApplyMutation(object targetDef, string fieldName, object? newValue, string? mutationKey = null)
    {
        if (targetDef == null || string.IsNullOrEmpty(fieldName)) return false;

        try
        {
            var type = targetDef.GetType();
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return false;

            string key = mutationKey ?? $"{type.FullName}_{fieldName}_{targetDef.GetHashCode()}";
            if (!activeMutations.ContainsKey(key))
            {
                activeMutations[key] = new DynamicMutationEntry
                {
                    TargetDef = targetDef,
                    Field = field,
                    OriginalValue = field.GetValue(targetDef),
                    CurrentValue = newValue
                };
            }
            else
            {
                activeMutations[key].CurrentValue = newValue;
            }

            field.SetValue(targetDef, newValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Revierte una mutación dinámica volviendo al valor original del Def.
    /// </summary>
    public static bool RevertMutation(string mutationKey)
    {
        if (activeMutations.TryGetValue(mutationKey, out var entry))
        {
            try
            {
                entry.Field.SetValue(entry.TargetDef, entry.OriginalValue);
                activeMutations.Remove(mutationKey);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Revierte todas las mutaciones activas restaurando los Defs al estado original.
    /// </summary>
    public static void RevertAll()
    {
        foreach (var entry in activeMutations.Values)
        {
            try
            {
                entry.Field.SetValue(entry.TargetDef, entry.OriginalValue);
            }
            catch { }
        }
        activeMutations.Clear();
    }
}
