using System;

namespace Rework;

/// <summary>
/// Declara desvío de proyectiles en el arma indicada: mientras el pawn empuñe
/// esa arma, los disparos que le alcancen pueden ser desviados (y con suerte,
/// devueltos al tirador).
///
/// Equivale al CompDeflector de JecsTools (estilo Jedi/sable láser),
/// reimplementado sin comps ni XML: el arma se marca por defName y el
/// framework resuelve la tirada en el despacho de daño.
///
/// <b>Simplificación declarada vs JecsTools:</b> el reflect devuelve el daño
/// al tirador por la vía directa (`TakeDamage` con su propio proyectil), sin
/// emitir un job de casteo con verbo. Mismo feeling (el tirador recibe su
/// disparo), sin la parte más frágil (verbos construidos a mano + jobs
/// forzados). El job con verbo queda como mejora futura.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkDeflectorAttribute : Attribute
{
    /// <summary>defName del ThingDef del arma que desvía (debe existir).</summary>
    public string WeaponDefName { get; }

    /// <summary>Probabilidad base de desvío (0..1) sin skills.</summary>
    public float BaseDeflectChance { get; set; } = 0.3f;

    /// <summary>defName del SkillDef que mejora el desvío (vacío = sin skill).</summary>
    public string? DeflectSkill { get; set; }

    /// <summary>Puntos de chance por nivel de skill.</summary>
    public float DeflectRatePerSkillPoint { get; set; } = 0.015f;

    /// <summary>XP de aprendizaje por desvío conseguido.</summary>
    public float DeflectSkillLearnRate { get; set; } = 250f;

    /// <summary>Si true, puede devolver el disparo al tirador.</summary>
    public bool CanReflect { get; set; } = false;

    /// <summary>defName del SkillDef que mejora el reflect (vacío = sin skill).</summary>
    public string? ReflectSkill { get; set; }

    /// <summary>Puntos de precisión por nivel al devolver.</summary>
    public float ReflectRatePerSkillPoint { get; set; } = 3f;

    /// <summary>XP de aprendizaje por reflect conseguido.</summary>
    public float ReflectSkillLearnRate { get; set; } = 500f;

    /// <summary>Si true, la manipulación del pawn multiplica la chance.</summary>
    public bool UseManipulationInCalc { get; set; } = false;

    /// <summary>defName del SoundDef al desviar (vacío = sin sonido).</summary>
    public string? DeflectSound { get; set; }

    public ReworkDeflectorAttribute(string weaponDefName)
    {
        WeaponDefName = weaponDefName;
    }
}

/// <summary>
/// Registro de deflectores ([ReworkDeflector]), por defName del arma.
/// El registrador resuelve skills y sonido una vez.
/// </summary>
public static class ReworkDeflectorRegistry
{
    public class DeflectorData
    {
        public string WeaponDefName = "";
        public float BaseDeflectChance = 0.3f;
        public object? DeflectSkillDef;
        public float DeflectRatePerSkillPoint = 0.015f;
        public float DeflectSkillLearnRate = 250f;
        public bool CanReflect;
        public object? ReflectSkillDef;
        public float ReflectRatePerSkillPoint = 3f;
        public float ReflectSkillLearnRate = 500f;
        public bool UseManipulationInCalc;
        public object? DeflectSoundDef;
    }

    private static readonly System.Collections.Generic.Dictionary<string, DeflectorData> deflectors =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra un deflector (lo llama el registrador del framework).
    /// </summary>
    public static void Register(DeflectorData data)
    {
        if (data == null || string.IsNullOrEmpty(data.WeaponDefName)) return;
        deflectors[data.WeaponDefName] = data;
    }

    /// <summary>Datos por defName del arma, o null si no desvía.</summary>
    public static DeflectorData? Get(string weaponDefName)
    {
        deflectors.TryGetValue(weaponDefName, out var data);
        return data;
    }

    /// <summary>Número de deflectores registrados (para early-out en el golpe).</summary>
    public static int Count => deflectors.Count;
}
