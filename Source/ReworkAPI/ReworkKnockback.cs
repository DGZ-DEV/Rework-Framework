using System;

namespace Rework;

/// <summary>
/// Declara empuje de knockback en la clase Hediff que lleva el atributo: cuando
/// el portador golpea en MELEE a un pawn, la víctima puede salir volando con
/// física de distancia (curvas por daño absorbido y por masa), daño al chocar,
/// stun y explosión opcional.
///
/// Equivale al HediffComp_Knockback de JecsTools (basado en el ForcePush de
/// ProjectJedi), reimplementado sobre el pipeline Rework sin XML ni comps.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkKnockbackAttribute : Attribute
{
    /// <summary>Nombre único del HediffDef (defName) que empuja al golpear.</summary>
    public string DefName { get; }

    /// <summary>Probabilidad de empuje por golpe que conecta (0..1).</summary>
    public float KnockbackChance { get; set; } = 0.2f;

    /// <summary>Distancia mínima de empuje en celdas.</summary>
    public float KnockDistanceMin { get; set; } = 2f;

    /// <summary>Distancia máxima de empuje en celdas.</summary>
    public float KnockDistanceMax { get; set; } = 3f;

    /// <summary>Velocidad de vuelo (celdas por segundo visual).</summary>
    public float KnockbackSpeed { get; set; } = 30f;

    /// <summary>defName del SoundDef al empujar (vacío = sin sonido).</summary>
    public string? KnockbackSound { get; set; }

    /// <summary>defName del ThoughtDef al chocar (vacío = sin pensamiento).</summary>
    public string? KnockbackThought { get; set; }

    /// <summary>Daño mínimo al chocar contra obstáculos.</summary>
    public float KnockImpactDamageMin { get; set; } = 8f;

    /// <summary>Daño máximo al chocar contra obstáculos.</summary>
    public float KnockImpactDamageMax { get; set; } = 10f;

    /// <summary>defName del DamageDef del impacto (vacío = Blunt).</summary>
    public string? KnockImpactDamageType { get; set; }

    /// <summary>Probabilidad de stun al empujar (0 = nunca).</summary>
    public float StunChance { get; set; } = 0f;

    /// <summary>Ticks de stun (60 = 1 segundo).</summary>
    public int StunTicks { get; set; } = 60;

    /// <summary>Radio de explosión al golpear (0 = sin explosión).</summary>
    public float ExplosiveRadius { get; set; } = 0f;

    /// <summary>defName del DamageDef de la explosión (vacío = Flame).</summary>
    public string? ExplosiveDamageType { get; set; }

    /// <summary>Nombre visible en la pestaña de salud del colono.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción del empuje.</summary>
    public string? Description { get; set; }

    public ReworkKnockbackAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro de knockbacks declarativos ([ReworkKnockback]).
///
/// El registrador resuelve sonidos, pensamientos y daños una vez; el runtime
/// del golpe solo compara por referencia y tira dados. Sin DefDatabase en el
/// camino caliente.
/// </summary>
public static class ReworkKnockbackRegistry
{
    public class KnockbackData
    {
        public string DefName = "";
        public Type HandlerType = null!;
        public float KnockbackChance = 0.2f;
        public float KnockDistanceMin = 2f;
        public float KnockDistanceMax = 3f;
        public float KnockbackSpeed = 30f;
        public object? KnockbackSoundDef;
        public object? KnockbackThoughtDef;
        public float KnockImpactDamageMin = 8f;
        public float KnockImpactDamageMax = 10f;
        public object? KnockImpactDamageDef;
        public float StunChance;
        public int StunTicks = 60;
        public float ExplosiveRadius;
        public object? ExplosiveDamageDef;
        public string Label = "";
        public string Description = "";
    }

    private static readonly System.Collections.Generic.Dictionary<string, KnockbackData> knockbacks =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra los datos de un knockback (lo llama el registrador del framework).
    /// </summary>
    public static void Register(KnockbackData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        knockbacks[data.DefName] = data;
    }

    /// <summary>Datos del knockback por defName del Hediff, o null si no empuja.</summary>
    public static KnockbackData? Get(string defName)
    {
        knockbacks.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Número de knockbacks registrados (para early-out en el golpe).</summary>
    public static int Count => knockbacks.Count;
}
