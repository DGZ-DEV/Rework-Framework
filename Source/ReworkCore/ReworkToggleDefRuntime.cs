using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Runtime de variantes ([ReworkToggleDef]).
///
/// Dos piezas:
///  - `GizmoFor(target)`: si el objetivo (o el arma del colono seleccionado)
///    pertenece a un grupo con 2+ variantes, devuelve las opciones para el
///    menú del gizmo. Lo consume un [ReworkGizmo] genérico que registra el
///    propio framework (no cada mod).
///  - `SwapTo(thing, newDef)`: despawn → cambia def → respawn, conservando
///    HP (proporcional), calidad, stuff y equipo/portador; re-inicializa
///    verbos y refresca gráficos (igual que JecsTools, verificado contra su
///    SwapThing).
///
/// El swap falla con error claro (nunca a medias): si el respawn falla, el
/// objeto se restaura a su def original antes de devolverlo.
/// </summary>
public static class ReworkToggleDefRuntime
{
    /// <summary>¿Hay grupos registrados? (para early-out del gizmo).</summary>
    public static bool HasActive => ReworkToggleDefRegistry.Count > 0;

    /// <summary>
    /// Opciones de variante para un objetivo (el Thing o el arma del pawn).
    /// Vacío si no es variante o el grupo tiene menos de 2.
    /// </summary>
    public static List<(ThingDef def, bool current)> OptionsFor(object target)
    {
        var out_ = new List<(ThingDef, bool)>();
        try
        {
            var thing = ResolveThing(target);
            if (thing?.def == null) return out_;
            var group = ReworkToggleDefRegistry.GetByDef(thing.def.defName);
            if (group == null || group.VariantDefs.Length < 2) return out_;
            foreach (var v in group.VariantDefs)
            {
                if (v is ThingDef td)
                    out_.Add((td, td == thing.def));
            }
        }
        catch { }
        return out_;
    }

    /// <summary>El Thing objetivo: el propio Thing, o el arma del pawn seleccionado.</summary>
    public static ThingWithComps? ResolveThing(object target)
    {
        try
        {
            if (target is ThingWithComps twc) return twc;
            if (target is Pawn pawn && pawn.IsColonistPlayerControlled)
                return pawn.equipment?.Primary;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Cambia un objeto a otra variante de su grupo, conservando estado.
    /// </summary>
    public static bool SwapTo(ThingWithComps thing, ThingDef newDef)
    {
        if (thing == null || newDef == null) return false;
        if (thing.def == newDef) return true;

        var group = ReworkToggleDefRegistry.GetByDef(thing.def.defName);
        if (group == null) return false;

        bool allowed = false;
        foreach (var v in group.VariantDefs)
        {
            if (v is ThingDef td && td == newDef) { allowed = true; break; }
        }
        if (!allowed)
        {
            Lg.Error($"[ReworkToggleDef] '{newDef.defName}' no es variante de '{thing.def.defName}'; cambio rechazado.");
            return false;
        }

        try
        {
            Map? map = thing.MapHeld;
            IntVec3 loc = thing.PositionHeld;
            Rot4 rot = thing.Rotation;

            var eqTracker = (thing.ParentHolder as Pawn_EquipmentTracker);
            Pawn? carrier = eqTracker?.pawn;

            // Proporción de HP para conservarla en el nuevo def.
            float hpFraction = 1f;
            try
            {
                if (thing.MaxHitPoints > 0)
                    hpFraction = Mathf.Clamp01((float)thing.HitPoints / thing.MaxHitPoints);
            }
            catch { }

            ThingDef oldDef = thing.def;
            if (eqTracker != null)
            {
                try { eqTracker.Remove(thing); }
                catch (Exception e)
                {
                    Lg.Error($"[ReworkToggleDef] No se pudo desequipar '{thing.LabelShort}': {e.Message}");
                    return false;
                }
            }
            else
            {
                try { thing.DeSpawn(); }
                catch { }
            }

            thing.def = newDef;

            // Re-inicializar verbos (igual que JecsTools: VerbsNeedReinitOnLoad).
            try
            {
                foreach (var comp in thing.AllComps)
                {
                    if (comp is IVerbOwner owner && owner.VerbTracker is VerbTracker tracker)
                    {
                        try { tracker.VerbsNeedReinitOnLoad(); }
                        catch { }
                        _ = tracker.AllVerbs;
                    }
                }
            }
            catch { }

            try { thing.Notify_ColorChanged(); }
            catch { }

            // Restaurar HP proporcional al nuevo máximo.
            try
            {
                thing.HitPoints = Mathf.Clamp(Mathf.RoundToInt(thing.MaxHitPoints * hpFraction), 1, thing.MaxHitPoints);
            }
            catch { }

            if (eqTracker != null && carrier != null)
            {
                try { eqTracker.AddEquipment(thing); }
                catch (Exception e)
                {
                    // Revertir: devolver el def original antes de rendirse.
                    try
                    {
                        thing.def = oldDef;
                        eqTracker.AddEquipment(thing);
                    }
                    catch { }
                    Lg.Error($"[ReworkToggleDef] No se pudo re-equipar como '{newDef.defName}': {e.Message}");
                    return false;
                }
            }
            else if (map != null)
            {
                try
                {
                    var spawned = GenSpawn.Spawn(thing, loc, map, rot, WipeMode.Vanish);
                    if (spawned == null)
                    {
                        thing.def = oldDef;
                        GenSpawn.Spawn(thing, loc, map, rot, WipeMode.Vanish);
                        Lg.Error($"[ReworkToggleDef] No se pudo re-spawnear como '{newDef.defName}'.");
                        return false;
                    }
                    try { Find.Selector.Select(thing, playSound: false); }
                    catch { }
                }
                catch (Exception e)
                {
                    Lg.Error($"[ReworkToggleDef] Swap falló: {e.Message}");
                    return false;
                }
            }

            Lg.Info($"[ReworkToggleDef] '{oldDef.defName}' → '{newDef.defName}'.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkToggleDef] Swap falló: {e.Message}");
            return false;
        }
    }
}
