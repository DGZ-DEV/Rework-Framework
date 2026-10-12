using System;
using Rework;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Despacho de desvío ([ReworkDeflector]).
///
/// Lo invoca la cabeza del parche sobre Verse.Pawn.PreApplyDamage (el mismo
/// paso 18b del soak): ANTES de que vanilla procese el golpe, si la víctima
/// empuña un arma deflectora y el golpe es un proyectil desviable, se tira el
/// dado. Devuelve true si el golpe quedó desviado (vanilla no lo procesa).
///
/// Reglas (las de JecsTools, sin el comp):
///  - Solo proyectiles: bombas, llamas y explosivos NO se desvían (igual que
///    el original: `Bomb/Flame/isExplosive` se saltan).
///  - Solo melee del defensor... no: el DEFENSOR desvía con arma melee. Si el
///    golpe viene de arma melee enemiga, no hay desvío (para eso está el
///    bloqueo, futuro; JecsTools lo deja a `TrySpecialMeleeBlock` virtual que
///    por defecto es false: aquí igual, false por defecto).
///  - Chance = base + nivel×tasa (si hay skill) × manipulación (si se pide),
///    limitada a [0,1]. Tirada 1-100.
///  - Reflect: si `CanReflect` y la tirada de precisión (d100 + nivel×tasa vs
///    dificultad 80; ≥90 crítico) tiene éxito, el tirador recibe su propio
///    daño de vuelta por la vía directa. Crítico fallido (≤30): el defensor
///    recibe el golpe (ya estaba en camino: no se hace nada extra).
///  - Skills: XP al desviar (deflect) y al devolver (reflect), con sus tasas.
/// </summary>
public static class ReworkDeflectorRuntime
{
    /// <summary>¿Hay deflectores registrados? (para early-out en el golpe).</summary>
    public static bool HasActive => ReworkDeflectorRegistry.Count > 0;

    /// <summary>
    /// Intenta el desvío. Devuelve true si el golpe quedó desviado.
    /// </summary>
    public static bool TryDeflect(Pawn victim, ref DamageInfo dinfo)
    {
        if (victim == null || victim.Dead)
            return false;
        if (ReworkDeflectorRegistry.Count == 0)
            return false;

        DamageDef damageDef;
        try { damageDef = dinfo.Def; }
        catch { return false; }
        if (damageDef == null)
            return false;

        // Explosivos y llamas no se desvían (igual que JecsTools).
        try
        {
            if (damageDef == DamageDefOf.Bomb || damageDef == DamageDefOf.Flame || damageDef.isExplosive)
                return false;
        }
        catch { return false; }

        // Solo proyectiles: el golpe melee enemigo no se desvía aquí.
        ThingDef? weaponDef = null;
        try { weaponDef = dinfo.Weapon; }
        catch { return false; }
        if (weaponDef != null && weaponDef.IsMeleeWeapon)
            return false;

        // El defensor debe empuñar un arma deflectora.
        ThingWithComps? primary = null;
        try { primary = victim.equipment?.Primary; }
        catch { return false; }
        if (primary?.def == null)
            return false;

        ReworkDeflectorRegistry.DeflectorData? data;
        try { data = ReworkDeflectorRegistry.Get(primary.def.defName); }
        catch { return false; }
        if (data == null)
            return false;

        try
        {
            float chance = DeflectionChance(victim, data);
            if (Rand.Range(1, 100) > (int)(chance * 100f))
                return false;

            // Desviado: XP, sonido, y quizás reflect.
            Learn(victim, data.DeflectSkillDef, data.DeflectSkillLearnRate);
            PlayDeflectSound(victim, data);

            if (data.CanReflect)
                TryReflect(victim, dinfo, data);

            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDeflector] Desvío falló: {e.Message}");
            return false;
        }
    }

    private static float DeflectionChance(Pawn victim, ReworkDeflectorRegistry.DeflectorData data)
    {
        float calc = data.BaseDeflectChance;
        try
        {
            if (data.DeflectSkillDef is SkillDef skill && victim.skills != null)
            {
                var record = victim.skills.GetSkill(skill);
                if (record != null)
                    calc += record.Level * data.DeflectRatePerSkillPoint;
            }
            if (data.UseManipulationInCalc && victim.health?.capacities != null)
            {
                try
                {
                    if (victim.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                        calc *= victim.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
                }
                catch { }
            }
        }
        catch { }
        return UnityEngine.Mathf.Clamp01(calc);
    }

    private static void TryReflect(Pawn victim, DamageInfo dinfo, ReworkDeflectorRegistry.DeflectorData data)
    {
        Pawn? shooter = null;
        try { shooter = dinfo.Instigator as Pawn; }
        catch { return; }
        if (shooter == null || shooter.Dead || shooter == victim)
            return;

        int modifier = 0;
        const int difficulty = 80;
        SkillRecord? skill = null;
        try
        {
            if (data.ReflectSkillDef is SkillDef reflectSkill && victim.skills != null)
            {
                skill = victim.skills.GetSkill(reflectSkill);
                if (skill != null)
                    modifier += (int)(data.ReflectRatePerSkillPoint * skill.Level);
            }
        }
        catch { }

        int roll = Rand.Range(1, 100);
        int subtotal = roll + modifier;
        bool reflected;
        if (subtotal >= 90)
            reflected = true;
        else if (subtotal > difficulty)
            reflected = true;
        else if (subtotal <= 30)
            reflected = false; // fallo crítico: el golpe sigue su curso (ya absorbido arriba: nada extra)
        else
            reflected = false;

        if (!reflected)
            return;

        try
        {
            if (skill != null)
                Learn(victim, data.ReflectSkillDef, data.ReflectSkillLearnRate);

            DamageDef damageDef = dinfo.Def;
            float amount = dinfo.Amount;
            if (amount <= 0f) return;
            var back = new DamageInfo(damageDef, amount, dinfo.ArmorPenetrationInt, -1f, victim);
            shooter.TakeDamage(back);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDeflector] Reflect falló: {e.Message}");
        }
    }

    private static void Learn(Pawn pawn, object? skillDef, float xp)
    {
        try
        {
            if (skillDef is SkillDef sd && pawn.skills != null && xp > 0f)
                pawn.skills.Learn(sd, xp, false);
        }
        catch { }
    }

    private static void PlayDeflectSound(Pawn victim, ReworkDeflectorRegistry.DeflectorData data)
    {
        try
        {
            if (data.DeflectSoundDef is SoundDef sd && victim.MapHeld != null)
                Verse.Sound.SoundStarter.PlayOneShotOnCamera(sd, victim.MapHeld);
        }
        catch { }
    }
}
