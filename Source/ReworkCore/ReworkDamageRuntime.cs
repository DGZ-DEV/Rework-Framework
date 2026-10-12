using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Despacho de daño declarativo ([ReworkDamageSoak]).
///
/// Lo invoca el parche sobre Verse.Pawn.PreApplyDamage (ver GameProcessing):
/// antes de que vanilla toque el golpe, recorre los hediffs del pawn y resta
/// la absorción de cada soak aplicable. Si el daño llega a 0 o menos, marca
/// absorbed=true y vanilla no procesa nada más (sin culpa de prisionero, sin
/// pensamientos, sin registro de cuentos: igual que si el daño nunca existió).
///
/// Diseño a propósito:
///  - Sin estados estáticos frágiles entre parches (el tempDamageAmount de
///    JecsTools rompía si otro mod tocaba ArmorUtility por otro camino).
///  - El registro ya trae los DamageDefs resueltos; aquí solo se compara por
///    referencia: el camino caliente no toca DefDatabase.
///  - Un soak roto jamás rompe el golpe: cada hediff se protege por separado.
/// </summary>
public static class ReworkDamageRuntime
{
    /// <summary>
    /// Aplica los soaks del pawn al golpe. Devuelve true si el daño quedó
    /// absorbido del todo (vanilla debe saltarse el procesado).
    /// </summary>
    public static bool ApplyDamageSoakers(Pawn pawn, ref DamageInfo dinfo)
    {
        if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            return false;
        if (ReworkDamageSoakRegistry.Count == 0)
            return false;

        float amount;
        try { amount = dinfo.Amount; }
        catch { return false; }
        if (amount <= 0f)
            return false;

        DamageDef damageDef;
        try { damageDef = dinfo.Def; }
        catch { return false; }

        float totalSoaked = 0f;
        List<Hediff>? hediffs;
        try { hediffs = pawn.health.hediffSet.hediffs; }
        catch { return false; }
        if (hediffs == null || hediffs.Count == 0)
            return false;

        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff;
            try { hediff = hediffs[i]; }
            catch { continue; }
            if (hediff?.def == null)
                continue;

            ReworkDamageSoakRegistry.SoakData? soak;
            try { soak = ReworkDamageSoakRegistry.Get(hediff.def.defName); }
            catch { continue; }
            if (soak == null)
                continue;

            try
            {
                // Filtro por tipo: null = todos los daños.
                if (soak.DamageTypeDef is DamageDef only && damageDef != only)
                    continue;

                // Exclusiones: este daño no se absorbe.
                bool excluded = false;
                var excl = soak.ExcludedTypeDefs;
                for (int e = 0; e < excl.Length; e++)
                {
                    if (excl[e] is DamageDef exDef && damageDef == exDef)
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded)
                    continue;

                float room = dinfo.Amount;
                if (room <= 0f)
                    break;
                float soaked = soak.DamageToSoak < room ? soak.DamageToSoak : room;
                if (soaked <= 0f)
                    continue;

                dinfo.SetAmount(room - soaked);
                totalSoaked += soaked;

                if (dinfo.Amount <= 0f)
                    break;
            }
            catch
            {
                // Un soak roto no rompe el golpe: se sigue con el siguiente.
            }
        }

        if (totalSoaked > 0f)
        {
            try { ThrowSoakedMote(pawn, totalSoaked, damageDef); }
            catch { }
        }

        try { return dinfo.Amount <= 0f; }
        catch { return false; }
    }

    private static void ThrowSoakedMote(Pawn pawn, float soakedDamage, DamageDef damageDef)
    {
        if (!pawn.Spawned || pawn.MapHeld == null)
            return;
        Vector3 drawPos = pawn.DrawPos;
        if (!drawPos.InBounds(pawn.MapHeld))
            return;

        // El mote solo sale si ALGÚN soak del pawn lo permite (OR, no AND).
        bool show = false;
        var hediffs = pawn.health?.hediffSet?.hediffs;
        if (hediffs != null)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                var def = hediffs[i]?.def;
                if (def == null) continue;
                var soak = ReworkDamageSoakRegistry.Get(def.defName);
                if (soak != null && soak.ShowAbsorbedMote)
                {
                    show = true;
                    break;
                }
            }
        }
        if (!show)
            return;

        int rounded = Mathf.RoundToInt(soakedDamage);
        MoteMaker.ThrowText(drawPos, pawn.MapHeld, "-" + rounded, Color.gray, 1.5f);
    }
}
