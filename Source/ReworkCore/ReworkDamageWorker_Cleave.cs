using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Worker de daño con cleave ([ReworkCleave]).
///
/// Hereda el Apply vanilla de heridas (`DamageWorker_AddInjury`: el golpe
/// principal es un golpe normal) y ANTES salpica a enemigos adyacentes a la
/// víctima (las 8 celdas, a menos de 2 celdas del atacante).
///
/// Reglas (las mismas que JecsTools):
///  - Nº de salpicados: `CleaveTargets` si es > 0; si es 0, masa del arma
///    (redondeada) o BodySize del atacante desarmado; 1 para no-pawns.
///  - Solo pawns enemigos del atacante (misma facción = a salvo; el atacante
///    nunca se golpea a sí mismo).
///  - El salpicado NO vuelve a salpicar: usa el DamageDef indicado (o el
///    mismo si no se indicó... pero aplicado directo con TakeDamage, sin pasar
///    por este worker otra vez).
///  - Sin `InstantPermanentInjury` (ejecuciones y admin-kills no barren).
///
/// El worker se engancha al DamageDef que crea el registrador vía
/// `workerClass = typeof(ReworkDamageWorker_Cleave)`: vanilla lo instancia
/// solo (`workerInt`) y le pasa su `def`. Los datos del atributo se leen del
/// registro por defName: el worker no guarda estado.
/// </summary>
public class ReworkDamageWorker_Cleave : DamageWorker_AddInjury
{
    private const int MaxDistSquared = 4;

    public override DamageResult Apply(DamageInfo dinfo, Thing victim)
    {
        try
        {
            if (!dinfo.InstantPermanentInjury && dinfo.Instigator != null && victim != null)
                Cleave(dinfo, victim);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkCleave] Salpicado falló: {e.Message}");
        }
        return base.Apply(dinfo, victim);
    }

    private void Cleave(DamageInfo dinfo, Thing victim)
    {
        var data = ReworkCleaveRegistry.Get(def?.defName ?? "");
        if (data == null)
            return;

        IntVec3 victimPos;
        Map map;
        try
        {
            victimPos = victim.PositionHeld;
            map = victim.MapHeld;
            if (map == null || victimPos == default(IntVec3))
                return;
        }
        catch { return; }

        int attacks = NumTargets(dinfo.Instigator, data);
        if (attacks <= 0)
            return;

        IntVec3 attackerPos;
        try { attackerPos = dinfo.Instigator.Position; }
        catch { return; }

        for (int i = 0; i < 8 && attacks > 0; i++)
        {
            IntVec3 cell;
            try { cell = victimPos + GenAdj.AdjacentCells[i]; }
            catch { continue; }
            if ((attackerPos - cell).LengthHorizontalSquared >= MaxDistSquared)
                continue;

            List<Thing> things;
            try { things = cell.GetThingList(map); }
            catch { continue; }

            for (int k = 0; k < things.Count && attacks > 0; k++)
            {
                if (!(things[k] is Pawn pawn))
                    continue;
                if (pawn == dinfo.Instigator)
                    continue;
                try
                {
                    if (pawn.Faction != null && dinfo.Instigator.Faction != null
                        && pawn.Faction == dinfo.Instigator.Faction)
                        continue;
                }
                catch { continue; }

                attacks--;
                try
                {
                    var splashDef = data.CleaveDamageDef as DamageDef ?? dinfo.Def;
                    float splashAmount = dinfo.Amount * data.CleaveFactor;
                    if (splashAmount <= 0f)
                        continue;
                    var splash = new DamageInfo(splashDef, splashAmount, data.ArmorPenetration, -1f, dinfo.Instigator);
                    pawn.TakeDamage(splash);
                }
                catch { }
            }
        }
    }

    private static int NumTargets(Thing instigator, ReworkCleaveRegistry.CleaveData data)
    {
        if (data.CleaveTargets > 0)
            return data.CleaveTargets;
        try
        {
            if (instigator is Pawn p)
            {
                // Masa del arma redondeada, como JecsTools (GetStatValue existe
                // en Thing vía StatExtension en 1.6: se usa el worker genérico).
                var primary = p.equipment?.Primary;
                if (primary != null)
                {
                    try { return Math.Max(1, (int)StatExtension.GetStatValue(primary, StatDefOf.Mass, true)); }
                    catch { }
                }
                return Math.Max(1, (int)p.BodySize);
            }
        }
        catch { }
        return 1;
    }
}
