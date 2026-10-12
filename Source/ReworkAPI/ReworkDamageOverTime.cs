using System;

namespace Rework;

/// <summary>
/// Declara daño periódico en la clase Hediff que lleva el atributo: mientras el
/// portador tenga el hediff, cada <see cref="CycleInTicks"/> ticks recibe
/// <see cref="CycleDamageAmt"/> puntos de daño del tipo indicado.
///
/// Equivale al HediffCompDamageOverTime de JecsTools, pero sin XML ni comp: el
/// framework lleva la cuenta de ciclos por hediff y aplica el daño.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkDamageOverTimeAttribute : Attribute
{
    /// <summary>Nombre único del HediffDef (defName) que aplica daño periódico.</summary>
    public string DefName { get; }

    /// <summary>defName del DamageDef de cada ciclo (p. ej. "Toxic", "Burn").</summary>
    public string CycleDamage { get; set; } = "Bite";

    /// <summary>Cantidad de daño por ciclo.</summary>
    public float CycleDamageAmt { get; set; } = 1f;

    /// <summary>Ticks entre ciclos (30000 = medio día).</summary>
    public int CycleInTicks { get; set; } = 30000;

    /// <summary>Penetración de armadura de cada ciclo (0 = la armadura protege).</summary>
    public float ArmorPenetration { get; set; } = 0f;

    /// <summary>Nombre visible en la pestaña de salud del colono.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción del daño periódico.</summary>
    public string? Description { get; set; }

    public ReworkDamageOverTimeAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro de daños periódicos ([ReworkDamageOverTime]).
///
/// Igual que el de soaks: el escáner resuelve el DamageDef una vez y el tick
/// solo compara por referencia. Sin DefDatabase en el camino caliente.
/// </summary>
public static class ReworkDamageOverTimeRegistry
{
    public class CycleData
    {
        public string DefName = "";
        public Type HandlerType = null!;
        public string CycleDamageName = "Bite";
        public float CycleDamageAmt = 1f;
        public int CycleInTicks = 30000;
        public float ArmorPenetration;

        /// <summary>Resolución perezosa del DamageDef del ciclo.</summary>
        public object? CycleDamageDef;

        public string Label = "";
        public string Description = "";
    }

    private static readonly System.Collections.Generic.Dictionary<string, CycleData> cycles =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra los datos de un ciclo (lo llama el registrador del framework).
    /// </summary>
    public static void Register(CycleData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        cycles[data.DefName] = data;
    }

    /// <summary>Datos del ciclo por defName del Hediff, o null si no es DoT.</summary>
    public static CycleData? Get(string defName)
    {
        cycles.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Número de DoTs registrados (para early-out en el tick).</summary>
    public static int Count => cycles.Count;
}
