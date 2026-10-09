using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca un método para generar texto narrativo o lore procedural para cartas, eventos o diálogos.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkLoreAttribute : Attribute
{
    public string Key { get; }

    public ReworkLoreAttribute(string key)
    {
        Key = key;
    }
}

/// <summary>
/// Motor y registro de templates y generación de narrativa procedural de Rework.
/// </summary>
public static class ReworkLore
{
    private static readonly Dictionary<string, Func<Dictionary<string, object>, string>> loreGenerators = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Número de generadores de lore registrados (para early-out).</summary>
    public static int GeneratorCount => loreGenerators.Count;

    /// <summary>¿Hay un generador registrado para esta clave?</summary>
    public static bool HasGenerator(string key) => !string.IsNullOrEmpty(key) && loreGenerators.ContainsKey(key);

    public static void RegisterGenerator(string key, Func<Dictionary<string, object>, string> generator)
    {
        loreGenerators[key] = generator;
    }

    public static string Generate(string key, Dictionary<string, object> parameters)
    {
        if (loreGenerators.TryGetValue(key, out var gen))
        {
            try { return gen(parameters); } catch { }
        }
        return string.Empty;
    }
}
