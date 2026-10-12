using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// Runtime de piezas instalables ([ReworkInstalledPart]).
///
///  - `JobsFor(target)`: si el objetivo admite piezas (o es un pawn), devuelve
///    las piezas instalables cercanas + piezas ya instaladas para quitar.
///    Lo consume el gizmo genérico del framework.
///  - `GiveInstallJob/GiveUninstallJob`: emiten los jobs del framework
///    (misma facción exigida, como JecsTools).
///  - `Notify_Installed/Uninstalled`: visten/equipan/guardas según el tipo
///    (prenda → Wear, arma → equipo, resto → contenedor del objetivo), con
///    mensajes al jugador. Igual que JecsTools, sin el comp.
/// </summary>
public static class ReworkInstalledPartRuntime
{
    /// <summary>¿Hay piezas registradas? (para early-out del gizmo).</summary>
    public static bool HasActive => ReworkInstalledPartRegistry.Count > 0;

    /// <summary>JobDefs del framework (los crea el registrador una vez).</summary>
    public static JobDef? InstallJobDef { get; set; }
    public static JobDef? UninstallJobDef { get; set; }

    public static void GiveInstallJob(Pawn actor, Thing part, Thing target)
    {
        try
        {
            if (actor?.Faction == null || target?.Faction == null || InstallJobDef == null) return;
            if (actor.Faction != target.Faction)
            {
                Messages.Message("No puedes instalar en algo de otra facción.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var job = JobMaker.MakeJob(InstallJobDef, part, target, target.Position);
            actor.jobs?.TryTakeOrderedJob(job);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] No se pudo ordenar instalar: {e.Message}");
        }
    }

    public static void GiveUninstallJob(Pawn actor, Thing part, Thing origin)
    {
        try
        {
            if (actor?.Faction == null || origin?.Faction == null || UninstallJobDef == null) return;
            if (actor.Faction != origin.Faction)
            {
                Messages.Message("No puedes desinstalar de algo de otra facción.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var job = JobMaker.MakeJob(UninstallJobDef, part, origin, origin.Position);
            actor.jobs?.TryTakeOrderedJob(job);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] No se pudo ordenar desinstalar: {e.Message}");
        }
    }

    public static void Notify_Installed(Pawn installer, Thing? part, Thing? target)
    {
        if (part == null || target == null) return;
        try
        {
            if (target is Pawn targetPawn && part.def != null)
            {
                if (part.def.IsApparel && part is Apparel apparel && targetPawn.apparel != null)
                {
                    try { part.DeSpawn(); } catch { }
                    try { targetPawn.apparel.Wear(apparel); } catch { }
                }
                else if (part.def.IsWeapon && part is ThingWithComps weapon)
                {
                    try { part.DeSpawn(); } catch { }
                    try
                    {
                        targetPawn.equipment?.MakeRoomFor(weapon);
                        targetPawn.equipment?.AddEquipment(weapon);
                    }
                    catch { }
                }
                else
                {
                    DropNear(part, target);
                }
            }
            else
            {
                ThingOwner? owner = null;
                try { owner = target.TryGetInnerInteractableThingOwner(); } catch { }
                if (owner != null)
                {
                    try { part.DeSpawn(); } catch { }
                    try { owner.TryAdd(part); } catch { DropNear(part, target); }
                }
                else
                {
                    DropNear(part, target);
                }
            }

            Messages.Message($"{installer.LabelShort} instaló {part.LabelShort} en {target.LabelShort}.",
                MessageTypeDefOf.PositiveEvent, false);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] Notify_Installed falló: {e.Message}");
        }
    }

    public static void Notify_Uninstalled(Pawn uninstaller, Thing? part, Thing? origin)
    {
        if (part == null || origin == null) return;
        try
        {
            if (origin is Pawn targetPawn && part.def != null)
            {
                if (part.def.IsApparel && part is Apparel apparel && targetPawn.apparel != null)
                {
                    try { targetPawn.apparel.TryDrop(apparel); } catch { }
                }
                else if (part.def.IsWeapon && part is ThingWithComps weapon && targetPawn.equipment != null)
                {
                    try { targetPawn.equipment.TryDropEquipment(weapon, out _, targetPawn.Position); } catch { }
                }
                else
                {
                    DropNear(part, origin);
                }
            }
            else
            {
                ThingOwner? owner = null;
                try { owner = origin.TryGetInnerInteractableThingOwner(); } catch { }
                if (owner != null)
                {
                    try { owner.Remove(part); } catch { }
                }
                DropNear(part, origin);
            }

            Messages.Message($"{uninstaller.LabelShort} desinstaló {part.LabelShort} de {origin.LabelShort}.",
                MessageTypeDefOf.PositiveEvent, false);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] Notify_Uninstalled falló: {e.Message}");
        }
    }

    private static void DropNear(Thing part, Thing near)
    {
        try
        {
            Map? map = near.MapHeld;
            if (map == null) return;
            if (part.Spawned)
            {
                try { part.DeSpawn(); } catch { }
            }
            GenPlace.TryPlaceThing(part, near.PositionHeld, map, ThingPlaceMode.Near);
        }
        catch { }
    }
}
