using System;
using System.Collections.Generic;
using System.Linq;
using Rework;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Despacho de daños extra de melee ([ReworkExtraMelee]).
///
/// Lo invoca el parche sobre `Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget`
/// (ver GameProcessing, paso 18c): justo después de que vanilla calcule su
/// `IEnumerable` de daños, se envuelve con los daños extra de los hediffs del
/// atacante. El `foreach` vanilla itera el enumerable envuelto sin enterarse:
/// cada `DamageInfo` extra pasa por `TakeDamage` igual que los del arma.
///
/// Diseño a propósito (vs el transpiler de JecsTools):
///  - JecsTools reescribe el `MoveNext` del iterador (422 instrucciones con
///    regiones try/catch en 1.6): frágil ante cualquier cambio de vanilla.
///    Aquí no se toca el iterador: se envuelve su RESULTADO (2 instrucciones
///    en un punto lineal, fuera de regiones protegidas).
///  - Sin caché ThreadStatic: el enumerable se envuelve por golpe; el coste
///    es una lista solo si hay daños extra (si no, se devuelve el original).
///  - Cada hediff roto se salta por separado; `chance` se tira por daño.
/// </summary>
public static class ReworkExtraMeleeRuntime
{
    /// <summary>¿Hay extra-melees registrados? (para early-out en el golpe).</summary>
    public static bool HasActive => ReworkExtraMeleeRegistry.Count > 0;

    /// <summary>
    /// Envuelve los daños vanilla con los extra del atacante.
    /// Firma Cecil-amigable: (IEnumerable, object verb) para no atar el
    /// ensamblado reescrito a tipos genéricos en la firma del hook.
    /// </summary>
    public static IEnumerable<DamageInfo> AppendExtra(object vanilla, object verb)
    {
        var original = vanilla as IEnumerable<DamageInfo>;
        if (original == null)
            return Enumerable.Empty<DamageInfo>();
        if (ReworkExtraMeleeRegistry.Count == 0)
            return original;

        Pawn? attacker = null;
        try { attacker = (verb as Verb)?.CasterPawn; }
        catch { return original; }
        if (attacker?.health?.hediffSet?.hediffs == null && ReworkSlotLoadableRuntime.HasActive == false)
            return original;

        List<DamageInfo>? extra = null;
        // Daños de slots: lo cargado en el arma del atacante (sin chance: el
        // item está o no está).
        try
        {
            if (ReworkSlotLoadableRuntime.HasActive && attacker != null)
            {
                var weapon = attacker.equipment?.Primary;
                if (weapon != null)
                {
                    foreach (var dinfo in ReworkSlotLoadableRuntime.ExtraDamagesFor(weapon, attacker))
                    {
                        extra ??= new List<DamageInfo>();
                        extra.Add(dinfo);
                    }
                }
            }
        }
        catch { }

        try
        {
            if (ReworkExtraMeleeRegistry.Count == 0)
            {
                if (extra == null || extra.Count == 0)
                    return original;
                return Concat(original, extra);
            }
            var hediffs = attacker!.health!.hediffSet!.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                var def = hediffs[i]?.def;
                if (def == null) continue;
                var data = ReworkExtraMeleeRegistry.Get(def.defName);
                if (data == null || data.Damages.Count == 0) continue;

                for (int d = 0; d < data.Damages.Count; d++)
                {
                    var entry = data.Damages[d];
                    if (!(entry.DamageDef is DamageDef damageDef))
                        continue;
                    try
                    {
                        if (!Rand.Chance(entry.Chance))
                            continue;
                    }
                    catch { continue; }
                    extra ??= new List<DamageInfo>();
                    extra.Add(new DamageInfo(damageDef, entry.Amount, entry.ArmorPenetration, -1f, attacker));
                }
            }
        }
        catch
        {
            return original;
        }

        if (extra == null || extra.Count == 0)
            return original;

        return Concat(original, extra);
    }

    private static IEnumerable<DamageInfo> Concat(IEnumerable<DamageInfo> first, List<DamageInfo> second)
    {
        foreach (var d in first)
            yield return d;
        for (int i = 0; i < second.Count; i++)
            yield return second[i];
    }
}
