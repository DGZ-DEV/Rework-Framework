using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca una clase para definir un efecto de estado o buff/debuff sin requerir XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkStatusEffectAttribute : Attribute
{
    public string DefName { get; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public int DurationTicks { get; set; } = 60000;

    /// <summary>
    /// Si true, el efecto se aplica automáticamente a los nuevos colonos de la
    /// facción del jugador al ser creados (ReworkHook PawnCreated). Si false,
    /// la aplicación queda a cargo del mod (Rework.Core.StatusEffectRuntime.Apply).
    /// </summary>
    public bool AutoApply { get; set; } = false;

    public ReworkStatusEffectAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro y despachador de Efectos de Estado (Buffs/Debuffs) de Rework.
/// </summary>
public static class ReworkStatusEffectRegistry
{
    public class StatusEffectData
    {
        public string DefName = "";
        public string Label = "";
        public string Description = "";
        public int DurationTicks;
        public Type HandlerType = null!;
        public bool AutoApply;
    }

    private static readonly Dictionary<string, StatusEffectData> effects = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string defName, string label, string desc, int durationTicks, Type handlerType, bool autoApply = false)
    {
        effects[defName] = new StatusEffectData
        {
            DefName = defName,
            Label = label,
            Description = desc,
            DurationTicks = durationTicks,
            HandlerType = handlerType,
            AutoApply = autoApply
        };
    }

    public static StatusEffectData? Get(string defName)
    {
        effects.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Todos los efectos registrados.</summary>
    public static IReadOnlyCollection<StatusEffectData> AllEffects => effects.Values;

    /// <summary>Solo los efectos con AutoApply=true (los aplica el framework al crear colonos).</summary>
    public static IEnumerable<StatusEffectData> AllAutoApply
    {
        get
        {
            foreach (var e in effects.Values)
                if (e.AutoApply)
                    yield return e;
        }
    }
}
