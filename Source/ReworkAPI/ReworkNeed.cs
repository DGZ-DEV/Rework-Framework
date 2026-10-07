using System;
using System.Collections.Generic;

namespace Rework;


/// <summary>
/// Clase base opcional para necesidades personalizadas de Rework.
/// </summary>
public abstract class ReworkNeedBase
{
    public float CurLevel { get; set; }
    public abstract void NeedInterval();
}

/// <summary>
/// Registro y despachador de Necesidades declarativas de Rework.
/// </summary>
public static class ReworkNeedRegistry
{
    public class NeedEntry
    {
        public string DefName = "";
        public string Label = "";
        public string Description = "";
        public float StartLevel;
        public float FallPerDay;
        public Type HandlerType = null!;
    }

    private static readonly Dictionary<string, NeedEntry> needs = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string defName, string label, string desc, float startLevel, float fallPerDay, Type handlerType)
    {
        needs[defName] = new NeedEntry
        {
            DefName = defName,
            Label = label,
            Description = desc,
            StartLevel = startLevel,
            FallPerDay = fallPerDay,
            HandlerType = handlerType
        };
    }

    public static IReadOnlyDictionary<string, NeedEntry> AllNeeds => needs;
}
