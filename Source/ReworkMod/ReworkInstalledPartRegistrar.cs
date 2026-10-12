using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;
using Verse.AI;

namespace Rework;

/// <summary>
/// Registrador de piezas instalables ([ReworkInstalledPart]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// valida cada pieza (def existente), resuelve `AllowedOn` y crea los dos
/// JobDefs del framework (`Rework_InstallPart`, `Rework_UninstallPart`) con
/// sus drivers. Además registra el gizmo genérico (una vez): en una pieza
/// suelta ofrece instalarla en objetivos válidos cercanos; en un objetivo con
/// piezas puestas ofrece quitarlas.
///
/// `AllowedOn` vacío = cualquier objetivo (el Notify decide por tipo: pawn →
/// vestir/equipar, contenedor → guardar, resto → dejar al lado con mensaje).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkInstalledPartRegistrar
{
    private static bool gizmoRegistered;

    static ReworkInstalledPartRegistrar()
    {
        EnsureJobDefs();
        RegisterAll();
        RegisterGizmo();
    }

    public static void RegisterAll()
    {
        int count = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass) continue;
                    foreach (var attr in type.GetCustomAttributes<ReworkInstalledPartAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkInstalledPartRegistrar: {count} pieza(s) [ReworkInstalledPart] registrada(s).");
    }

    private static bool Register(ReworkInstalledPartAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.PartDefName))
            {
                Lg.Error($"[ReworkInstalledPart] '{carrierType.FullName}': PartDefName vacío; se ignora.");
                return false;
            }

            var part = DefDatabase<ThingDef>.GetNamedSilentFail(attr.PartDefName.Trim());
            if (part == null)
            {
                Lg.Error($"[ReworkInstalledPart] '{carrierType.FullName}': pieza '{attr.PartDefName}' no existe; NO funcionará hasta corregirlo.");
                return false;
            }

            var allowed = new List<string>();
            if (!string.IsNullOrWhiteSpace(attr.AllowedOn))
            {
                foreach (var a in attr.AllowedOn.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = a.Trim();
                    if (name.Length == 0) continue;
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null
                        && DefDatabase<PawnKindDef>.GetNamedSilentFail(name) == null)
                        Lg.Error($"[ReworkInstalledPart] '{attr.PartDefName}': AllowedOn '{name}' no existe; se ignora.");
                    else
                        allowed.Add(name);
                }
            }

            ReworkInstalledPartRegistry.Register(new ReworkInstalledPartRegistry.PartData
            {
                PartDefName = part.defName,
                AllowedOnDefNames = allowed.ToArray(),
                WorkToInstall = Math.Max(20, attr.WorkToInstall),
                WorkToUninstall = Math.Max(20, attr.WorkToUninstall),
            });
            Lg.Info($"[ReworkInstalledPart] Pieza '{part.defName}' registrada (instalar {attr.WorkToInstall} / desinstalar {attr.WorkToUninstall}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] Error registrando '{attr.PartDefName}': {e.Message}");
            return false;
        }
    }

    private static void EnsureJobDefs()
    {
        try
        {
            ReworkInstalledPartRuntime.InstallJobDef = EnsureJobDef(
                "Rework_InstallPart", typeof(ReworkJobDriver_InstallPart), "Instalando pieza.");
            ReworkInstalledPartRuntime.UninstallJobDef = EnsureJobDef(
                "Rework_UninstallPart", typeof(ReworkJobDriver_UninstallPart), "Desinstalando pieza.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkInstalledPart] No se pudieron crear los JobDefs: {e.Message}");
        }
    }

    private static JobDef EnsureJobDef(string defName, Type driver, string report)
    {
        var existing = DefDatabase<JobDef>.GetNamedSilentFail(defName);
        if (existing != null) return existing;
        var def = new JobDef
        {
            defName = defName,
            driverClass = driver,
            reportString = report,
            playerInterruptible = true,
            casualInterruptible = true,
            suspendable = true,
        };
        DefDatabase<JobDef>.Add(def);
        return def;
    }

    private static void RegisterGizmo()
    {
        if (gizmoRegistered) return;
        gizmoRegistered = true;
        var method = typeof(ReworkInstalledPartRegistrar).GetMethod(nameof(OnGizmo),
            BindingFlags.Public | BindingFlags.Static)!;
        // En piezas sueltas y en pawns/edificios: VisibleIf decide (pieza
        // registrada, u objetivo con piezas instaladas cerca).
        Func<object, bool> visible = target =>
        {
            if (!ReworkInstalledPartRuntime.HasActive) return false;
            try
            {
                if (target is Thing t && t.def != null
                    && ReworkInstalledPartRegistry.Get(t.def.defName) != null)
                    return true;
                if (target is Pawn || target is Building)
                    return true;
            }
            catch { }
            return false;
        };
        ReworkGizmoRegistry.RegisterConditional("Thing", "Instalar pieza", "Instala una pieza compatible en este objetivo.", method, visible);
        ReworkGizmoRegistry.RegisterConditional("Pawn", "Instalar pieza", "Instala una pieza compatible en este pawn.", method, visible);
    }

    public static void OnGizmo(object target)
    {
        try
        {
            Pawn? actor = null;
            Thing? part = null;
            Thing? dest = null;

            if (target is Thing t && t.def != null && ReworkInstalledPartRegistry.Get(t.def.defName) != null)
            {
                part = t;
                actor = Find.Selector?.SingleSelectedThing as Pawn;
                if (actor == null)
                {
                    Messages.Message("Selecciona primero al colono que instalará, con la pieza seleccionada.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                dest = PickInstallTarget(actor, part);
                if (dest == null)
                {
                    Messages.Message("No hay objetivo válido cerca para instalar.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                ReworkInstalledPartRuntime.GiveInstallJob(actor, part, dest);
            }
            else if (target is Thing d)
            {
                dest = d;
                actor = Find.Selector?.SingleSelectedThing as Pawn;
                if (actor == null)
                {
                    Messages.Message("Selecciona primero al colono que instalará.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                part = PickLoosePart(dest);
                if (part == null)
                {
                    Messages.Message("No hay piezas instalables cerca.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                ReworkInstalledPartRuntime.GiveInstallJob(actor, part, dest);
            }
        }
        catch (Exception e)
        {
            Rework.Core.Lg.Error($"[ReworkInstalledPart] Gizmo falló: {e.Message}");
        }
    }

    private static Thing? PickInstallTarget(Pawn actor, Thing part)
    {
        try
        {
            var data = ReworkInstalledPartRegistry.Get(part.def.defName);
            var map = actor.MapHeld ?? part.MapHeld;
            if (map == null) return null;
            foreach (var th in map.listerThings?.AllThings ?? new List<Thing>())
            {
                if (th == null || th == part || th.Destroyed) continue;
                if (th is Pawn p && (p.Dead || p.Faction != actor.Faction)) continue;
                if (data != null && data.AllowedOnDefNames.Length > 0)
                {
                    bool ok = false;
                    foreach (var a in data.AllowedOnDefNames)
                    {
                        if (string.Equals(th.def?.defName, a, StringComparison.OrdinalIgnoreCase))
                        { ok = true; break; }
                    }
                    if (!ok) continue;
                }
                if (th.Position.DistanceTo(actor.Position) < 60f)
                    return th;
            }
        }
        catch { }
        return null;
    }

    private static Thing? PickLoosePart(Thing dest)
    {
        try
        {
            var map = dest.MapHeld;
            if (map == null) return null;
            foreach (var th in map.listerThings?.AllThings ?? new List<Thing>())
            {
                if (th?.def == null || th.Destroyed || !th.Spawned) continue;
                if (ReworkInstalledPartRegistry.Get(th.def.defName) == null) continue;
                if (th.Position.DistanceTo(dest.Position) < 60f)
                    return th;
            }
        }
        catch { }
        return null;
    }
}
