using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca un método para definir una pestaña de inspección en el panel inferior del colono sin ITab ni XML.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkTabAttribute : Attribute
{
    public string Id { get; }
    public string Label { get; set; } = "";

    public ReworkTabAttribute(string id)
    {
        Id = id;
    }
}

/// <summary>
/// Registro y despachador de Pestañas de Inspección declarativas.
/// </summary>
public static class ReworkTabRegistry
{
    public class TabEntry
    {
        public string Id = "";
        public string Label = "";
        public Action<object, object> DrawAction = null!; // (rect, pawn)
    }

    private static readonly List<TabEntry> tabs = new();

    public static void Register(string id, string label, Action<object, object> drawAction)
    {
        tabs.Add(new TabEntry { Id = id, Label = label, DrawAction = drawAction });
    }

    public static IReadOnlyList<TabEntry> AllTabs => tabs;
}
