using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Tick de daño periódico ([ReworkDamageOverTime]).
///
/// El tick global (OnGameComponentTick) llama a OnTick con el tick actual:
/// por cada pawn vivo con un hediff DoT, cuenta ciclos por hediff y aplica el
/// daño al cumplirse. La cuenta vive por instancia de hediff (LoadID), no por
/// pawn: dos venenos distintos en el mismo pawn ciclan por separado.
///
/// Diseño a propósito:
///  - Sin HediffComp propio: la cuenta está aquí, en un diccionario por LoadID.
///    El hediff del mod puede ser cualquier subclase de Hediff.
///  - El daño se aplica con Thing.TakeDamage (el camino vanilla completo:
///    armadura, absorción, notificaciones). La penetración sale del atributo.
///  - Un DoT roto jamás rompe el tick: cada hediff se protege por separado y
///    los pawns destruidos se limpian sin ruido.
/// </summary>
public static class ReworkDamageOverTimeRuntime
{
    // LoadID del hediff -> ticks que faltan para el próximo ciclo.
    private static readonly Dictionary<int, int> ticksLeft = new();

    /// <summary>¿Hay DoTs registrados? (para early-out en OnGameComponentTick)</summary>
    public static bool HasActive => ReworkDamageOverTimeRegistry.Count > 0;

    public static void OnTick()
    {
        if (ReworkDamageOverTimeRegistry.Count == 0)
            return;

        try
        {
            var maps = Find.Maps;
            if (maps == null || maps.Count == 0)
                return;

            for (int mi = 0; mi < maps.Count; mi++)
            {
                var map = maps[mi];
                if (map?.mapPawns == null)
                    continue;
                List<Pawn> pawns;
                try { pawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned); }
                catch { continue; }

                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var pawn = pawns[pi];
                    if (pawn == null || pawn.Destroyed || pawn.health?.hediffSet?.hediffs == null)
                        continue;
                    TickPawn(pawn);
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDamageOverTime] Tick falló: {e.Message}");
        }
    }

    private static void TickPawn(Pawn pawn)
    {
        var hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff;
            try { hediff = hediffs[i]; }
            catch { continue; }
            if (hediff?.def == null)
                continue;

            ReworkDamageOverTimeRegistry.CycleData? cycle;
            try { cycle = ReworkDamageOverTimeRegistry.Get(hediff.def.defName); }
            catch { continue; }
            if (cycle == null)
                continue;

            try
            {
                int id = hediff.loadID;
                if (!ticksLeft.TryGetValue(id, out int left))
                {
                    left = cycle.CycleInTicks;
                    ticksLeft[id] = left;
                }

                left--;
                if (left > 0)
                {
                    ticksLeft[id] = left;
                    continue;
                }

                ticksLeft[id] = cycle.CycleInTicks;
                ApplyCycle(pawn, hediff, cycle);
            }
            catch
            {
                // Un hediff roto no rompe el tick del resto.
            }
        }
    }

    private static void ApplyCycle(Pawn pawn, Hediff hediff, ReworkDamageOverTimeRegistry.CycleData cycle)
    {
        if (pawn.Destroyed || pawn.health == null)
            return;
        if (!(cycle.CycleDamageDef is DamageDef damageDef))
            return;
        if (cycle.CycleDamageAmt <= 0f)
            return;

        var dinfo = new DamageInfo(
            damageDef,
            cycle.CycleDamageAmt,
            cycle.ArmorPenetration,
            -1f,
            null,
            hediff.Part);

        try
        {
            pawn.TakeDamage(dinfo);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDamageOverTime] '{cycle.DefName}' no pudo aplicar su ciclo en {pawn.LabelShort}: {e.Message}");
        }
    }

    /// <summary>Olvida la cuenta de un hediff (al retirarse, para no arrastrar estado).</summary>
    internal static void Forget(int loadID)
    {
        ticksLeft.Remove(loadID);
    }
}
