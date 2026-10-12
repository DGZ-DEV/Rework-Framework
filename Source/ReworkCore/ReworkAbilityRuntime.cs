using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// Runtime del puente de habilidades ([ReworkAbility] + [ReworkPassive]).
///
///  - `Grant(pawn, defName)`: concede la habilidad vanilla al pawn (si no la
///    tiene). Lo llama el hook de generación (colonos/PawnKinds) y los mods.
///  - `OnPawnGenerated(pawn)`: concede según registro (colonos + kinds).
///    Lo invoca un [ReworkHook(PawnCreated)] del propio framework.
///  - `TickPassives()`: cada 250 ticks, por cada pawn con habilidades: si
///    tiene el hediff requerido (o el pasivo es incondicional), aplica el
///    hediff de stats generado; si no, lo retira. Enganchado al tick global.
///  - Efectos simples: se implementan como `CompAbilityEffect` propios del
///    framework (`ReworkEffect_Hediff/Damage/Stun/Spawn`), instanciados por
///    nombre desde el CSV. Vanilla los ejecuta: cero código custom por disparo.
/// </summary>
public static class ReworkAbilityRuntime
{
    /// <summary>¿Hay habilidades o pasivos? (para early-out).</summary>
    public static bool HasActive => ReworkAbilityRegistry.AbilityCount > 0 || ReworkAbilityRegistry.PassiveCount > 0;

    public static bool Grant(Pawn pawn, string defName)
    {
        try
        {
            if (pawn?.abilities == null || string.IsNullOrEmpty(defName)) return false;
            var data = ReworkAbilityRegistry.GetAbility(defName);
            if (data?.AbilityDef is not AbilityDef def) return false;
            if (pawn.abilities.GetAbility(def) != null) return false;
            pawn.abilities.GainAbility(def);
            Lg.Info($"[ReworkAbility] '{defName}' concedida a {pawn.LabelShort}.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkAbility] No se pudo conceder '{defName}': {e.Message}");
            return false;
        }
    }

    public static void OnPawnGenerated(Pawn pawn)
    {
        try
        {
            if (pawn?.abilities == null) return;
            foreach (var kvp in ReworkAbilityRegistry.AllAbilities)
            {
                var data = kvp.Value;
                if (data.AbilityDef is not AbilityDef) continue;
                if (data.GrantToColonists && pawn.IsColonist)
                {
                    Grant(pawn, data.DefName);
                    continue;
                }
                if (data.GrantToKinds.Length > 0 && pawn.kindDef != null)
                {
                    foreach (var kind in data.GrantToKinds)
                    {
                        if (string.Equals(pawn.kindDef.defName, kind, StringComparison.OrdinalIgnoreCase))
                        {
                            Grant(pawn, data.DefName);
                            break;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static int tickCounter;

    public static void TickPassives()
    {
        if (ReworkAbilityRegistry.PassiveCount == 0) return;
        if ((++tickCounter % 250) != 0) return;

        try
        {
            var maps = Find.Maps;
            if (maps == null || maps.Count == 0) return;
            foreach (var kvp in ReworkAbilityRegistry.AllPassives)
            {
                var passive = kvp.Value;
                if (passive.Stats.Count == 0) continue;
                for (int mi = 0; mi < maps.Count; mi++)
                {
                    var pawns = maps[mi]?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null) continue;
                    for (int pi = 0; pi < pawns.Count; pi++)
                    {
                        try { TickPassiveFor(pawns[pi], passive); }
                        catch { }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkPassive] Tick falló: {e.Message}");
        }
    }

    private static void TickPassiveFor(Pawn pawn, ReworkAbilityRegistry.PassiveData passive)
    {
        if (pawn?.health?.hediffSet == null) return;

        bool active = string.IsNullOrEmpty(passive.RequiresHediff);
        if (!active)
        {
            var need = DefDatabase<HediffDef>.GetNamedSilentFail(passive.RequiresHediff!);
            if (need != null && pawn.health.hediffSet.GetFirstHediffOfDef(need, false) != null)
                active = true;
        }

        string hediffName = "Rework_Passive_" + passive.Id;
        var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
        var existing = hediffDef != null
            ? pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef, false)
            : null;

        if (active && existing == null && hediffDef != null)
        {
            try
            {
                var h = HediffMaker.MakeHediff(hediffDef, pawn);
                pawn.health.AddHediff(h);
            }
            catch { }
        }
        else if (!active && existing != null)
        {
            try { pawn.health.RemoveHediff(existing); }
            catch { }
        }
    }
}

/// <summary>
/// Efectos simples del framework para habilidades (instanciados por nombre
/// desde el CSV de [ReworkAbility]). Vanilla los ejecuta al disparar: el
/// modder no escribe ni un verbo.
/// </summary>
public class ReworkEffect_Hediff : CompAbilityEffect
{
    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        base.Apply(target, dest);
        try
        {
            var props = this.props as ReworkEffectProps_Hediff;
            string? name = props?.hediffDefName;
            float sev = props?.severity ?? 1f;
            if (string.IsNullOrEmpty(name)) return;
            var hd = DefDatabase<HediffDef>.GetNamedSilentFail(name);
            if (hd == null || parent?.pawn == null) return;
            Pawn? victim = (target.Thing as Pawn) ?? parent.pawn;
            if (victim?.health == null) return;
            var hediff = HediffMaker.MakeHediff(hd, victim);
            hediff.Severity = sev;
            victim.health.AddHediff(hediff);
        }
        catch { }
    }
}

public class ReworkEffect_Damage : CompAbilityEffect
{
    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        base.Apply(target, dest);
        try
        {
            var props = this.props as ReworkEffectProps_Damage;
            var dd = DefDatabase<DamageDef>.GetNamedSilentFail(props?.damageDefName ?? "Burn");
            float amt = props?.amount ?? 10f;
            if (dd == null || parent?.pawn == null) return;
            Thing? victim = target.Thing ?? parent.pawn;
            if (victim == null || victim.Destroyed) return;
            victim.TakeDamage(new DamageInfo(dd, amt, 0f, -1f, parent.pawn));
        }
        catch { }
    }
}

public class ReworkEffect_Stun : CompAbilityEffect
{
    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        base.Apply(target, dest);
        try
        {
            int ticks = (this.props as ReworkEffectProps_Stun)?.ticks ?? 120;
            if (target.Thing is Pawn victim && !victim.Dead && victim.stances?.stunner != null)
                victim.stances.stunner.StunFor(ticks, parent?.pawn);
        }
        catch { }
    }
}

public class ReworkEffect_Spawn : CompAbilityEffect
{
    public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
    {
        base.Apply(target, dest);
        try
        {
            var props = this.props as ReworkEffectProps_Spawn;
            if (string.IsNullOrEmpty(props?.pawnKindDefName)) return;
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(props.pawnKindDefName);
            if (kind == null) return;
            Map? map = target.Thing?.MapHeld ?? parent?.pawn?.MapHeld;
            if (map == null) return;
            IntVec3 at = target.Cell.IsValid ? target.Cell : parent.pawn.PositionHeld;
            for (int i = 0; i < Math.Max(1, props?.count ?? 1); i++)
            {
                var pawn = PawnGenerator.GeneratePawn(kind, parent?.pawn?.Faction);
                GenPlace.TryPlaceThing(pawn, at, map, ThingPlaceMode.Near);
            }
        }
        catch { }
    }
}
