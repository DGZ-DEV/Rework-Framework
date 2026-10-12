using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// JobGiver genérico de casteo para enemigos ([ReworkAbility] + IA).
///
/// El modder lo cuelga de un ThinkTree (p. ej. con un PatchOperation o un
/// ThinkTreeDef propio): cada vez que el pawn piensa, si tiene una habilidad
/// concedida por el framework, lista para lanzar y con objetivo válido, emite
/// el job de casteo vanilla.
///
/// Selección (simple y predecible, no el árbol de JecsTools):
///  - Enemigo a rango con línea de visión → primera habilidad ofensiva
///    (con daño o stun en sus efectos).
///  - Aliado herido a rango → primera habilidad de cura (con hediff no-malo).
///  - Si no hay objetivo, no emite (el pawn sigue su IA normal).
///
/// La mejora futura es el árbol de decisión por tags (AbilityUserAI); esto
/// cubre el 80% (raiders que usan su magia) con el 10% del código.
/// </summary>
public class ReworkJobGiver_AICast : ThinkNode_JobGiver
{
    public override Job TryGiveJob(Pawn pawn)
    {
        try
        {
            if (pawn?.abilities == null || pawn.Dead || pawn.Downed) return null;
            if (ReworkAbilityRegistry.AbilityCount == 0) return null;

            Ability? offensive = null;
            Ability? heal = null;
            foreach (var ab in pawn.abilities.AllAbilitiesForReading)
            {
                if (ab?.def == null || !ab.CanCast) continue;
                if (ReworkAbilityRegistry.GetAbility(ab.def.defName) == null) continue;
                if (IsOffensive(ab)) offensive ??= ab;
                else if (IsHeal(ab)) heal ??= ab;
                if (offensive != null && heal != null) break;
            }

            // Cura primero (aliado herido visible a rango).
            if (heal != null)
            {
                var ally = FindHurtAlly(pawn, heal);
                if (ally != null)
                    return CastJob(heal, ally);
            }
            // Ofensiva (enemigo a rango con LoS).
            if (offensive != null)
            {
                var enemy = FindEnemy(pawn, offensive);
                if (enemy != null)
                    return CastJob(offensive, enemy);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsOffensive(Ability ab)
    {
        try
        {
            foreach (var comp in ab.def.comps)
            {
                if (comp is ReworkEffect_Damage || comp is ReworkEffect_Stun)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsHeal(Ability ab)
    {
        try
        {
            foreach (var comp in ab.def.comps)
            {
                if (comp is ReworkEffect_Hediff) return true;
            }
        }
        catch { }
        return false;
    }

    private static Pawn? FindEnemy(Pawn caster, Ability ab)
    {
        try
        {
            float range = ab.def.verbProperties?.range ?? 24f;
            foreach (var p in caster.MapHeld?.mapPawns?.AllPawnsSpawned ?? new List<Pawn>())
            {
                if (p == null || p.Dead || p == caster) continue;
                if (!p.HostileTo(caster)) continue;
                if (caster.Position.DistanceTo(p.Position) > range) continue;
                if (ab.def.verbProperties?.requireLineOfSight == true
                    && !GenSight.LineOfSight(caster.Position, p.Position, caster.MapHeld))
                    continue;
                return p;
            }
        }
        catch { }
        return null;
    }

    private static Pawn? FindHurtAlly(Pawn caster, Ability ab)
    {
        try
        {
            float range = ab.def.verbProperties?.range ?? 24f;
            foreach (var p in caster.MapHeld?.mapPawns?.AllPawnsSpawned ?? new List<Pawn>())
            {
                if (p == null || p.Dead || p == caster) continue;
                if (p.HostileTo(caster)) continue;
                if (p.health?.hediffSet?.hediffs == null) continue;
                bool hurt = false;
                foreach (var h in p.health.hediffSet.hediffs)
                {
                    if (h is Hediff_Injury) { hurt = true; break; }
                }
                if (!hurt) continue;
                if (caster.Position.DistanceTo(p.Position) > range) continue;
                return p;
            }
        }
        catch { }
        return null;
    }

    private static Job? CastJob(Ability ab, Pawn target)
    {
        try
        {
            var job = JobMaker.MakeJob(ab.def.jobDef ?? JobDefOf.CastAbilityOnThing);
            job.targetA = target;
            job.verbToUse = ab.verb;
            return job;
        }
        catch { return null; }
    }
}
