using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Declara dependencias entre mods basados en Rework para resolver el orden óptimo de inicialización.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public sealed class ReworkRequiresAttribute : Attribute
{
    public string[] RequiredModIds { get; }

    public ReworkRequiresAttribute(params string[] requiredModIds)
    {
        RequiredModIds = requiredModIds;
    }
}

/// <summary>
/// Resolutor de dependencias y orden de carga de mods en el ecosistema Rework.
/// </summary>
public static class ReworkDepGraph
{
    private static readonly Dictionary<string, List<string>> graph = new(StringComparer.OrdinalIgnoreCase);

    public static void AddDependency(string modId, string dependsOn)
    {
        if (!graph.TryGetValue(modId, out var list))
        {
            list = new List<string>();
            graph[modId] = list;
        }
        if (!list.Contains(dependsOn)) list.Add(dependsOn);
    }

    public static List<string> ResolveLoadOrder(IEnumerable<string> allMods)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        void Visit(string mod)
        {
            if (visited.Contains(mod)) return;
            visited.Add(mod);

            if (graph.TryGetValue(mod, out var deps))
            {
                foreach (var dep in deps) Visit(dep);
            }
            result.Add(mod);
        }

        foreach (var m in allMods) Visit(m);
        return result;
    }
}
