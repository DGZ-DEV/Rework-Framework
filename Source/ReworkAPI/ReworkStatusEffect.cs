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
    }

    private static readonly Dictionary<string, StatusEffectData> effects = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string defName, string label, string desc, int durationTicks, Type handlerType)
    {
        effects[defName] = new StatusEffectData
        {
            DefName = defName,
            Label = label,
            Description = desc,
            DurationTicks = durationTicks,
            HandlerType = handlerType
        };
    }

    public static StatusEffectData? Get(string defName)
    {
        effects.TryGetValue(defName, out var data);
        return data;
    }
}
