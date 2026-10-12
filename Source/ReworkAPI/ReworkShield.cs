using System;

namespace Rework;

/// <summary>
/// Declara un escudo físico en el ThingDef indicado: mientras el pawn lo lleve
/// equipado, los golpes (melee o a distancia) pueden quedar bloqueados. El
/// escudo absorbe daño con sus propios HP, fatiga al portador y se descarta
/// solo al romperse.
///
/// Equivale al sistema PawnShields de JecsTools (CompShield + stats + fatiga),
/// reimplementado sin comps ni XML: el escudo se marca por defName y el
/// framework resuelve el bloqueo en el despacho de daño.
///
/// <b>Simplificaciones declaradas vs JecsTools:</b> sin render propio del
/// escudo en el pawn (requiere el render de equipo de 1.6: mejora futura), sin
/// StatDefs en la UI (la chance vive en el registro: base + skill×tasa, como
/// el deflector), sin sonidos por material (un solo sonido de bloqueo),
/// sin generador de pawns con escudo (mejora futura). Lo que se conserva:
/// bloqueo melee/ranged por separado, daño al escudo, dscarte por HP, fatiga
/// con 4 stages, efecto de chispas y sonido.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkShieldAttribute : Attribute
{
    /// <summary>defName del ThingDef del escudo (debe existir y ser equipable).</summary>
    public string ShieldDefName { get; }

    /// <summary>Si bloquea ataques melee.</summary>
    public bool CanBlockMelee { get; set; } = true;

    /// <summary>Si bloquea ataques a distancia.</summary>
    public bool CanBlockRanged { get; set; } = true;

    /// <summary>Probabilidad base de bloqueo melee (0..1).</summary>
    public float BaseMeleeBlockChance { get; set; } = 0.5f;

    /// <summary>Probabilidad base de bloqueo ranged (0..1).</summary>
    public float BaseRangedBlockChance { get; set; } = 0.25f;

    /// <summary>defName del SkillDef que mejora el bloqueo (vacío = sin skill).</summary>
    public string? BlockSkill { get; set; }

    /// <summary>Puntos de chance por nivel de skill.</summary>
    public float BlockRatePerSkillPoint { get; set; } = 0.015f;

    /// <summary>Si el escudo recibe daño al bloquear (con sus propios HP).</summary>
    public bool ShieldTakeDamage { get; set; } = true;

    /// <summary>Fracción del daño que pasa al escudo (0..1).</summary>
    public float ShieldDamageAbsorbed { get; set; } = 0.8f;

    /// <summary>Si se descarta solo al bajar del umbral de HP.</summary>
    public bool CanBeAutoDiscarded { get; set; } = true;

    /// <summary>Umbral de HP (fracción) para el descarte automático.</summary>
    public float HealthAutoDiscardThreshold { get; set; } = 0.19f;

    /// <summary>Si bloquear fatiga al portador (Hediff con 4 stages).</summary>
    public bool UseFatigue { get; set; } = false;

    /// <summary>Fracción del daño convertida en severidad de fatiga.</summary>
    public float DamageToFatigueFactor { get; set; } = 0.05f;

    /// <summary>defName del SoundDef al bloquear (vacío = golpe metálico).</summary>
    public string? BlockSound { get; set; }

    public ReworkShieldAttribute(string shieldDefName)
    {
        ShieldDefName = shieldDefName;
    }
}

/// <summary>
/// Registro de escudos ([ReworkShield]), por defName del escudo.
/// </summary>
public static class ReworkShieldRegistry
{
    public class ShieldData
    {
        public string ShieldDefName = "";
        public bool CanBlockMelee = true;
        public bool CanBlockRanged = true;
        public float BaseMeleeBlockChance = 0.5f;
        public float BaseRangedBlockChance = 0.25f;
        public object? BlockSkillDef;
        public float BlockRatePerSkillPoint = 0.015f;
        public bool ShieldTakeDamage = true;
        public float ShieldDamageAbsorbed = 0.8f;
        public bool CanBeAutoDiscarded = true;
        public float HealthAutoDiscardThreshold = 0.19f;
        public bool UseFatigue;
        public float DamageToFatigueFactor = 0.05f;
        public object? BlockSoundDef;
    }

    private static readonly System.Collections.Generic.Dictionary<string, ShieldData> shields =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra un escudo (lo llama el registrador del framework).
    /// </summary>
    public static void Register(ShieldData data)
    {
        if (data == null || string.IsNullOrEmpty(data.ShieldDefName)) return;
        shields[data.ShieldDefName] = data;
    }

    /// <summary>Datos por defName del escudo, o null si no es escudo.</summary>
    public static ShieldData? Get(string shieldDefName)
    {
        shields.TryGetValue(shieldDefName, out var data);
        return data;
    }

    /// <summary>Número de escudos registrados (para early-out en el golpe).</summary>
    public static int Count => shields.Count;
}
