using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Despacho de knockback ([ReworkKnockback]).
///
/// Lo invoca el parche post-golpe sobre Verse.Pawn.PreApplyDamage (ver
/// GameProcessing): DESPUÉS de que vanilla procese el daño (escudos y
/// absorción incluidos), si el atacante melee lleva un hediff de knockback,
/// la víctima puede salir volando.
///
/// Reglas del golpe (las mismas que JecsTools, verificadas en su código):
///  - Solo melee: el arma del golpe no es a distancia y el instigador es un pawn.
///  - Anti-doble-empuje: si el mismo atacante ya empujó a la misma víctima en
///    este tick (daños extra generan varios golpes), el segundo no empuja. La
///    caché se limpia en el tick global: sin ConcurrentDictionary (todo corre
///    en el hilo principal).
///  - % absorbido = 1 si vanilla absorbió; si no, 1 - clamp(dañoFinal/dañoInicial).
///    Absorbido del todo = no hay empuje (no tiene sentido lanzar a alguien
///    que no sintió el golpe).
///
/// Física (curvas idénticas a las de JecsTools, con los mismos defaults):
///  - distancia = rand(min,max) × curvaAbsorción(%absorbido) × curvaMasa(masaSinInventario)
///  - curvaAbsorción: 100% absorbido → 0% distancia; 0% → 100%
///  - curvaMasa: 0kg → ×2; 60kg → ×1; 120kg → ×0.5; 240kg → ×0.25
///  - El destino se recorta por línea de visión (puertas cerradas y muros
///    paran; salirse del mapa también): si hay colisión, daño de impacto
///    proporcional a la distancia recorrida.
/// </summary>
public static class ReworkKnockbackRuntime
{
    // (atacante, víctima) -> tick del último empuje. Se limpia en el tick global.
    private static readonly Dictionary<(Thing, Thing), int> lastKnockTicks = new();

    private static readonly SimpleCurve AbsorbedPercentCurve = new SimpleCurve
    {
        new CurvePoint(1f, 0f),
        new CurvePoint(0f, 1f),
    };

    private static readonly SimpleCurve MassCurve = new SimpleCurve
    {
        new CurvePoint(0f, 2f),
        new CurvePoint(60f, 1f),
        new CurvePoint(120f, 0.5f),
        new CurvePoint(240f, 0.25f),
    };

    private static readonly SimpleCurve ImpactDistanceCurve = new SimpleCurve
    {
        new CurvePoint(0f, 0f),
        new CurvePoint(0.5f, 0.75f),
        new CurvePoint(1f, 1f),
    };

    /// <summary>¿Hay knockbacks registrados? (para early-out en el golpe).</summary>
    public static bool HasActive => ReworkKnockbackRegistry.Count > 0;

    /// <summary>
    /// ThingDef del objeto volador. Lo crea el registrador (ReworkMod) una vez
    /// vía EnsureFlyingDef; el runtime solo lo lee.
    /// </summary>
    public static ThingDef? FlyingDef { get; set; }

    /// <summary>Limpia la caché anti-doble-empuje (desde el tick global).</summary>
    public static void ClearTickCache()
    {
        if (lastKnockTicks.Count > 0)
            lastKnockTicks.Clear();
    }

    /// <summary>
    /// Cola post-golpe: la llama el parche al final de Pawn.PreApplyDamage, con
    /// el daño YA procesado por vanilla. Extrae atacante/arma del golpe y
    /// delega en TryKnockback. Si no hay knockbacks, retorna sin tocar nada.
    /// </summary>
    public static void AfterHit(Pawn victim, ref DamageInfo dinfo, float damageBefore, bool absorbed)
    {
        if (victim == null)
            return;
        if (ReworkKnockbackRegistry.Count == 0 && !ReworkSlotLoadableRuntime.HasActive)
            return;

        Pawn? attacker = null;
        ThingDef? weaponDef = null;
        try
        {
            attacker = dinfo.Instigator as Pawn;
            weaponDef = dinfo.Weapon;
        }
        catch { return; }
        if (attacker == null)
            return;

        float damageAfter;
        try { damageAfter = dinfo.Amount; }
        catch { return; }

        if (ReworkKnockbackRegistry.Count > 0)
            TryKnockback(attacker, victim, damageBefore, damageAfter, absorbed, weaponDef);

        // Curas de slots: el arma del atacante cura al golpear (sin early-out
        // propio: OnPostHit ya comprueba registro por arma).
        if (ReworkSlotLoadableRuntime.HasActive)
        {
            try
            {
                var weapon = attacker.equipment?.Primary;
                if (weapon != null)
                    ReworkSlotLoadableRuntime.OnPostHit(attacker, weapon);
            }
            catch { }
        }
    }

    /// <summary>
    /// Intenta el empuje tras un golpe melee. Se llama con el daño YA procesado.
    /// </summary>
    public static void TryKnockback(Pawn attacker, Pawn victim, float damageBefore, float damageAfter, bool absorbed, ThingDef? weaponDef)
    {
        if (attacker == null || victim == null)
            return;
        if (ReworkKnockbackRegistry.Count == 0)
            return;
        if (victim.Dead || victim.Downed || !victim.Spawned || victim.MapHeld == null)
            return;
        if (victim == attacker)
            return;
        if (weaponDef != null && weaponDef.IsRangedWeapon)
            return;

        try
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            var pair = (attacker as Thing, victim as Thing);
            if (lastKnockTicks.TryGetValue(pair, out int last) && last == now)
                return;
            lastKnockTicks[pair] = now;

            float absorbedPercent = absorbed ? 1f : 1f - Mathf.Clamp01(damageAfter / Math.Max(0.001f, damageBefore));
            if (absorbedPercent >= 1f)
                return;

            var hediffs = attacker.health?.hediffSet?.hediffs;
            if (hediffs == null || hediffs.Count == 0)
                return;

            for (int i = 0; i < hediffs.Count; i++)
            {
                var def = hediffs[i]?.def;
                if (def == null) continue;
                var kb = ReworkKnockbackRegistry.Get(def.defName);
                if (kb == null) continue;

                try { ApplyKnockback(attacker, victim, kb, absorbedPercent); }
                catch (Exception e)
                {
                    Lg.Error($"[ReworkKnockback] '{kb.DefName}' falló contra {victim.LabelShort}: {e.Message}");
                }
                // Un atacante, un empuje: el primer hediff que empuja gana.
                break;
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkKnockback] Fallo en el golpe: {e.Message}");
        }
    }

    private static void ApplyKnockback(Pawn attacker, Pawn victim, ReworkKnockbackRegistry.KnockbackData kb, float absorbedPercent)
    {
        if (!Rand.Chance(kb.KnockbackChance))
            return;

        // Explosión primero (igual que JecsTools: detona aunque luego no vuele).
        if (kb.ExplosiveRadius > 0f)
            Detonate(attacker, victim, kb);

        if (Rand.Chance(kb.StunChance) && victim.stances?.stunner != null)
        {
            try { victim.stances.stunner.StunFor(kb.StunTicks, attacker); }
            catch { }
        }

        PushEffect(attacker, victim, kb, absorbedPercent);
    }

    private static void Detonate(Pawn attacker, Pawn victim, ReworkKnockbackRegistry.KnockbackData kb)
    {
        try
        {
            var map = victim.MapHeld;
            if (map == null) return;
            IntVec3 pos = victim.PositionHeld;
            var damageDef = kb.ExplosiveDamageDef as DamageDef ?? DamageDefOf.Flame;
            GenExplosion.DoExplosion(pos, map, kb.ExplosiveRadius, damageDef, attacker);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkKnockback] Explosión falló: {e.Message}");
        }
    }

    private static void PushEffect(Pawn attacker, Pawn victim, ReworkKnockbackRegistry.KnockbackData kb, float absorbedPercent)
    {
        float origDistance = Rand.Range(kb.KnockDistanceMin, kb.KnockDistanceMax);
        if (origDistance <= 0f)
            return;

        float absorbFactor = AbsorbedPercentCurve.Evaluate(absorbedPercent);
        float mass;
        try { mass = victim.GetStatValue(StatDefOf.Mass) - MassUtility.InventoryMass(victim); }
        catch { mass = 60f; }
        float massFactor = MassCurve.Evaluate(Math.Max(0f, mass));
        float distance = origDistance * absorbFactor * massFactor;
        if (distance <= 0f)
            return;

        Vector3 dest = PushDestination(attacker, victim, distance, out float actual, out bool collision);
        if (actual <= 0f && !collision)
            return;

        if (kb.KnockbackThoughtDef is ThoughtDef thought && (collision || actual > 0f) && victim.RaceProps?.Humanlike == true)
        {
            try { victim.needs?.mood?.thoughts?.memories?.TryGainMemory(thought); }
            catch { }
        }

        if (kb.KnockbackSoundDef is SoundDef sound)
        {
            try { Verse.Sound.SoundStarter.PlayOneShotOnCamera(sound, victim.MapHeld); }
            catch { }
        }

        DamageInfo? impact = null;
        if (collision || actual > 0f)
        {
            float percent = distance > 0f ? actual / distance : 0f;
            float raw = Rand.Range(kb.KnockImpactDamageMin, kb.KnockImpactDamageMax);
            float dmg = raw * ImpactDistanceCurve.Evaluate(Mathf.Clamp01(percent));
            if (dmg > 0f)
            {
                var dmgDef = kb.KnockImpactDamageDef as DamageDef ?? DamageDefOf.Blunt;
                float angle = (dest - victim.TrueCenter()).Yto0().normalized is Vector3 dir && dir != Vector3.zero
                    ? Quaternion.LookRotation(dir).eulerAngles.y
                    : -1f;
                impact = new DamageInfo(dmgDef, dmg, 0f, angle, attacker);
            }
        }

        if (actual > 0f)
        {
            LaunchFlying(attacker, victim, dest, kb, impact);
        }
        else if (impact is DamageInfo immediate && !victim.Destroyed)
        {
            try { victim.TakeDamage(immediate); }
            catch { }
        }
    }

    private static Vector3 PushDestination(Thing caster, Thing target, float distance, out float actual, out bool collision)
    {
        actual = 0f;
        collision = false;
        Vector3 originLoc = target.TrueCenter();
        Map map = target.MapHeld;
        if (map == null || caster.MapHeld != map)
            return originLoc;

        Vector3 casterLoc = caster.TrueCenter();
        Vector3 direction = (originLoc - casterLoc).Yto0().normalized;
        if (direction == Vector3.zero)
            return originLoc;

        CellRect originRect = target.OccupiedRect();
        IntVec3 originCell = originLoc.ToIntVec3();
        Vector3 targetLoc = originLoc + direction * distance;
        IntVec3 targetCell = targetLoc.ToIntVec3();

        List<IntVec3> cells;
        try { cells = new List<IntVec3>(GenSight.PointsOnLineOfSight(originCell, targetCell)); }
        catch { cells = new List<IntVec3>(); }
        if (cells.Count == 0 || cells[cells.Count - 1] != targetCell)
            cells.Add(targetCell);

        var pathGrid = (target is Pawn p ? map.pathing.For(p) : map.pathing.Normal).pathGrid;
        IntVec3 prev = originCell;
        foreach (var cell in cells)
        {
            if (originRect.Contains(cell))
                continue;

            bool blocked = true;
            bool outOfBounds = false;
            if (!cell.InBounds(map))
            {
                outOfBounds = true;
            }
            else
            {
                try
                {
                    if (pathGrid.WalkableFast(cell) && !cell.Impassable(map))
                    {
                        if (cell.GetEdifice(map) is Building_Door door && !door.Open)
                        {
                            blocked = true;
                        }
                        else
                        {
                            blocked = false;
                        }
                    }
                }
                catch { blocked = true; }
            }

            if (blocked)
            {
                collision = !outOfBounds;
                float cellDist = prev.DistanceTo(originCell);
                Vector3 loc = ClampToCell(originLoc + direction * cellDist, prev);
                actual = Vector3.Distance(loc, originLoc);
                return loc;
            }
            prev = cell;
        }

        actual = Vector3.Distance(targetLoc, originLoc);
        return targetLoc;
    }

    private static Vector3 ClampToCell(Vector3 loc, IntVec3 cell)
    {
        return new Vector3(Mathf.Clamp(loc.x, cell.x, cell.x + 1), loc.y, Mathf.Clamp(loc.z, cell.z, cell.z + 1));
    }

    private static void LaunchFlying(Pawn attacker, Pawn victim, Vector3 dest, ReworkKnockbackRegistry.KnockbackData kb, DamageInfo? impact)
    {
        try
        {
            var map = victim.MapHeld;
            if (map == null) return;
            IntVec3 destCell = new IntVec3(dest);
            if (!destCell.InBounds(map))
                destCell = victim.PositionHeld;

            // El def del objeto volador lo crea el registrador (una vez).
            var def = FlyingDef;
            if (def == null) return;

            var fly = (ReworkFlyingObject)ThingMaker.MakeThing(def);
            Vector3 originPos = victim.TrueCenter();
            try
            {
                if (victim.Spawned)
                    victim.DeSpawn();
            }
            catch { return; }

            try { GenSpawn.Spawn(fly, victim.PositionHeld, map, WipeMode.Vanish); }
            catch
            {
                try { GenSpawn.Spawn(victim, victim.PositionHeld, map, WipeMode.Vanish); }
                catch { }
                return;
            }

            fly.Launch(attacker, originPos, destCell, victim, kb.KnockbackSpeed, impact);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkKnockback] Lanzamiento falló: {e.Message}");
            try
            {
                if (victim.Destroyed || !victim.Spawned)
                    GenSpawn.Spawn(victim, victim.PositionHeld, victim.MapHeld, WipeMode.Vanish);
            }
            catch { }
        }
    }
}
