using System;

namespace Rework;

/// <summary>
/// Declara un tipo de daño con cleave: al impactar a una víctima, el golpe
/// salpica a enemigos adyacentes (las 8 celdas alrededor) con una fracción del
/// daño. Sin XML: el framework crea el DamageDef con su worker.
///
/// Equivale al DamageWorker_Cleave + DamageDefCleave de JecsTools.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkCleaveAttribute : Attribute
{
    /// <summary>Nombre único del DamageDef (defName) con cleave.</summary>
    public string DefName { get; }

    /// <summary>Número de objetivos extra (0 = auto: masa del arma o BodySize).</summary>
    public int CleaveTargets { get; set; } = 0;

    /// <summary>Fracción del daño que recibe cada objetivo extra (0..1).</summary>
    public float CleaveFactor { get; set; } = 0.7f;

    /// <summary>defName del DamageDef del salpicado (vacío = el mismo).</summary>
    public string? CleaveDamage { get; set; }

    /// <summary>Penetración de armadura del salpicado.</summary>
    public float ArmorPenetration { get; set; } = 0f;

    /// <summary>Nombre visible del daño.</summary>
    public string? Label { get; set; }

    public ReworkCleaveAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro de cleaves declarativos ([ReworkCleave]).
/// El registrador resuelve el DamageDef del salpicado una vez.
/// </summary>
public static class ReworkCleaveRegistry
{
    public class CleaveData
    {
        public string DefName = "";
        public int CleaveTargets;
        public float CleaveFactor = 0.7f;
        public object? CleaveDamageDef;
        public float ArmorPenetration;
        public string Label = "";
    }

    private static readonly System.Collections.Generic.Dictionary<string, CleaveData> cleaves =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra los datos de un cleave (lo llama el registrador del framework).
    /// </summary>
    public static void Register(CleaveData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        cleaves[data.DefName] = data;
    }

    /// <summary>Datos del cleave por defName del DamageDef, o null.</summary>
    public static CleaveData? Get(string defName)
    {
        cleaves.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Número de cleaves registrados.</summary>
    public static int Count => cleaves.Count;
}
