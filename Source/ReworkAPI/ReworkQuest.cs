using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca una clase como una misión o Quest interactivo declarado completamente en C# sin XML ni QuestScript.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkQuestAttribute : Attribute
{
    public string DefName { get; }
    public string Title { get; set; } = "";

    public ReworkQuestAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Clase base para misiones de Rework.
/// </summary>
public abstract class ReworkQuestBase
{
    public abstract bool CanTrigger();
    public abstract void OnAccepted();
    public abstract void OnSuccess();
    public abstract void OnFailed();
}

/// <summary>
/// Registro y despachador de Quests declarativos de Rework.
/// </summary>
public static class ReworkQuestRegistry
{
    private static readonly Dictionary<string, Type> registeredQuests = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string defName, Type questType)
    {
        registeredQuests[defName] = questType;
    }

    public static IReadOnlyDictionary<string, Type> AllQuests => registeredQuests;
}
