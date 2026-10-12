using System;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Rework.Core;

/// <summary>
/// Despacho de bloqueo con escudo ([ReworkShield]).
///
/// Lo invoca la cabeza del parche sobre Verse.Pawn.PreApplyDamage (paso 18b),
/// DESPUÉS del deflector y ANTES del soak: el escudo es lo segundo que para
/// un golpe (primero se desvía, luego se bloquea, luego se absorbe).
/// Devuelve true si el golpe quedó bloqueado (vanilla no lo procesa).
///
/// Reglas (las de JecsTools, sin el comp):
///  - El portador debe llevar equipado un Thing cuyo defName esté registrado
///    como escudo (se busca en todo el equipo, no solo en Primary: el escudo
///    no es el arma).
///  - Ranged vs melee: el golpe es ranged si el instigador NO está adyacente
///    (8 celdas) a la víctima, o si el daño es explosivo. Igual que JecsTools
///    (su condición: `canBlockRanged && (no adyacente || explosivo)`).
///  - Chance = base(modo) + nivel×tasa (si hay skill), limitada a [0,1].
///  - Al bloquear: fatiga (fracción del daño a severidad del Hediff, si se
///    pide), daño al escudo (fracción con sus propios HP vía TakeDamage) y,
///    si sigue intacto, efecto (chispas + sonido) y absorbed.
///  - Escudo roto (HP bajo el umbral): se suelta a los pies sin prohibir.
///    Fatiga al máximo: también se suelta (el brazo no da más).
/// </summary>
public static class ReworkShieldRuntime
{
    /// <summary>defName del HediffDef de fatiga (lo crea el registrador).</summary>
    public const string FatigueDefName = "Rework_ShieldFatigue";

    /// <summary>¿Hay escudos registrados? (para early-out en el golpe).</summary>
    public static bool HasActive => ReworkShieldRegistry.Count > 0;

    /// <summary>
    /// Intenta el bloqueo. Devuelve true si el golpe quedó bloqueado.
    /// </summary>
    public static bool TryBlock(Pawn victim, ref DamageInfo dinfo)
    {
        if (victim == null || victim.Dead)
            return false;
        if (ReworkShieldRegistry.Count == 0)
            return false;

        try
        {
            // Buscar el escudo en el equipo (cualquiera que esté registrado).
            ThingWithComps? shield = FindShield(victim);
            if (shield == null)
                return false;

            var data = ReworkShieldRegistry.Get(shield.def.defName);
            if (data == null)
                return false;

            bool ranged = IsRanged(victim, dinfo);
            float chance;
            if (ranged)
            {
                if (!data.CanBlockRanged) return false;
                chance = BlockChance(victim, data, data.BaseRangedBlockChance);
            }
            else
            {
                if (!data.CanBlockMelee) return false;
                chance = BlockChance(victim, data, data.BaseMeleeBlockChance);
            }

            if (!Rand.Chance(chance))
                return false;

            // Fatiga.
            if (data.UseFatigue)
                ApplyFatigue(victim, dinfo, data);

            // Daño al escudo.
            if (data.ShieldTakeDamage)
                DamageShield(shield, dinfo, data);

            // ¿Sigue intacto?
            if (shield.HitPoints <= 0)
            {
                DiscardShield(victim, shield);
                return false;
            }

            MakeBlockEffect(victim, data);

            // ¿Toca soltarlo (por HP o por fatiga al máximo)?
            if (ShouldDiscard(victim, shield, data))
                DiscardShield(victim, shield);

            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkShield] Bloqueo falló: {e.Message}");
            return false;
        }
    }

    private static ThingWithComps? FindShield(Pawn victim)
    {
        try
        {
            var equipment = victim.equipment?.AllEquipmentListForReading;
            if (equipment == null) return null;
            for (int i = 0; i < equipment.Count; i++)
            {
                var eq = equipment[i];
                if (eq?.def == null) continue;
                if (ReworkShieldRegistry.Get(eq.def.defName) != null)
                    return eq;
            }
        }
        catch { }
        return null;
    }

    private static bool IsRanged(Pawn victim, DamageInfo dinfo)
    {
        try
        {
            if (dinfo.Def != null && dinfo.Def.isExplosive)
                return true;
            var instigator = dinfo.Instigator;
            if (instigator == null)
                return true;
            return !GenAdj.AdjacentTo8WayOrInside(instigator.Position, victim.Position);
        }
        catch { return true; }
    }

    private static float BlockChance(Pawn victim, ReworkShieldRegistry.ShieldData data, float baseChance)
    {
        float calc = baseChance;
        try
        {
            if (data.BlockSkillDef is SkillDef skill && victim.skills != null)
            {
                var record = victim.skills.GetSkill(skill);
                if (record != null)
                    calc += record.Level * data.BlockRatePerSkillPoint;
            }
        }
        catch { }
        return Mathf.Clamp01(calc);
    }

    private static void ApplyFatigue(Pawn victim, DamageInfo dinfo, ReworkShieldRegistry.ShieldData data)
    {
        try
        {
            var fatigueDef = DefDatabase<HediffDef>.GetNamedSilentFail(FatigueDefName);
            if (fatigueDef == null) return;
            float sev = dinfo.Amount * data.DamageToFatigueFactor;
            if (sev <= 0f) return;
            HealthUtility.AdjustSeverity(victim, fatigueDef, sev);
        }
        catch { }
    }

    private static void DamageShield(ThingWithComps shield, DamageInfo dinfo, ReworkShieldRegistry.ShieldData data)
    {
        try
        {
            int amount = Mathf.CeilToInt(dinfo.Amount * data.ShieldDamageAbsorbed);
            if (amount <= 0) return;
            var shieldDamage = new DamageInfo(dinfo);
            shieldDamage.SetAmount(amount);
            shield.TakeDamage(shieldDamage);
        }
        catch { }
    }

    private static void MakeBlockEffect(Pawn victim, ReworkShieldRegistry.ShieldData data)
    {
        try
        {
            if (victim.MapHeld != null)
                FleckMaker.Static(victim.Position, victim.MapHeld, FleckDefOf.MicroSparks, 1f);
        }
        catch { }
        try
        {
            var sound = data.BlockSoundDef as SoundDef ?? SoundDefOf.MetalHitImportant;
            SoundStarter.PlayOneShotOnCamera(sound, victim.MapHeld);
        }
        catch { }
    }

    private static bool ShouldDiscard(Pawn victim, ThingWithComps shield, ReworkShieldRegistry.ShieldData data)
    {
        if (!data.CanBeAutoDiscarded)
            return false;
        try
        {
            if (shield.MaxHitPoints > 0
                && (shield.HitPoints / (float)shield.MaxHitPoints) <= data.HealthAutoDiscardThreshold)
                return true;
        }
        catch { }

        if (data.UseFatigue)
        {
            try
            {
                var hediff = victim.health?.hediffSet?.GetFirstHediffOfDef(
                    DefDatabase<HediffDef>.GetNamedSilentFail(FatigueDefName), false);
                if (hediff != null && hediff.Severity >= hediff.def.maxSeverity)
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static void DiscardShield(Pawn victim, ThingWithComps shield)
    {
        try
        {
            if (victim.equipment.TryDropEquipment(shield, out var dropped, victim.Position, true))
            {
                try { ForbidUtility.SetForbidden(dropped, false, true); }
                catch { }
            }
            else
                Lg.Error($"[ReworkShield] {victim.LabelShort} no pudo soltar el escudo {shield.LabelShort}.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkShield] Descarte falló: {e.Message}");
        }
    }
}
