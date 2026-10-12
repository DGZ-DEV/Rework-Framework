using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// JobDrivers de instalar/desinstalar ([ReworkInstalledPart]).
///
/// Los JobDefs los crea el registrador (`Rework_InstallPart`,
/// `Rework_UninstallPart`); estos drivers son del framework (no por mod).
/// TargetA = pieza, TargetB = objetivo, TargetC = celda de trabajo.
///
/// Instalar: ir a la pieza → llevarla al objetivo → trabajar (Construction,
/// con velocidad del pawn) → `Notify_Installed` (vestir si es prenda, equipar
/// si es arma, guardar en el contenedor si el objetivo lo admite).
/// Desinstalar: ir al objetivo → trabajar → soltar la pieza al suelo.
///
/// Las cantidades de trabajo salen del registro por defName de la pieza.
/// </summary>
public class ReworkJobDriver_InstallPart : JobDriver
{
    private float workLeft = -1f;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return true;
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOnDestroyedOrNull(TargetIndex.B);

        yield return Toils_Reserve.Reserve(TargetIndex.A);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
        yield return Toils_Haul.StartCarryThing(TargetIndex.A);
        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);

        var work = new Toil
        {
            initAction = () =>
            {
                workLeft = WorkNeeded(job.targetA.Thing?.def?.defName);
            },
            tickAction = () =>
            {
                var actor = pawn;
                var target = job.targetB.Thing;
                if (target is Pawn pawnTarget && pawnTarget.pather != null && !pawnTarget.Dead)
                {
                    try { pawnTarget.pather.StopDead(); } catch { }
                }
                try { actor.rotationTracker.FaceCell(TargetB.Cell); } catch { }
                try { actor.skills?.Learn(SkillDefOf.Construction, 0.275f); } catch { }

                float speed = 1f;
                try { speed = actor.GetStatValue(StatDefOf.ConstructionSpeed); } catch { }
                workLeft -= speed;
                if (workLeft <= 0f)
                {
                    try { actor.records?.Increment(RecordDefOf.ThingsInstalled); } catch { }
                    var part = job.targetA.Thing;
                    ReworkInstalledPartRuntime.Notify_Installed(actor, part, target);
                    actor.jobs?.EndCurrentJob(JobCondition.Succeeded, true);
                }
            },
            defaultCompleteMode = ToilCompleteMode.Never,
        };
        work.FailOnDestroyedOrNull(TargetIndex.A);
        work.FailOnDestroyedOrNull(TargetIndex.B);
        yield return work;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref workLeft, "workLeft", -1f);
    }

    private static int WorkNeeded(string? partDefName)
    {
        if (!string.IsNullOrEmpty(partDefName))
        {
            var data = ReworkInstalledPartRegistry.Get(partDefName);
            if (data != null)
                return Mathf.Clamp(data.WorkToInstall, 20, 3000);
        }
        return 500;
    }
}

/// <summary>
/// Driver de desinstalar: ir al objetivo → trabajar → la pieza cae al suelo.
/// </summary>
public class ReworkJobDriver_UninstallPart : JobDriver
{
    private float workLeft = -1f;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return true;
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOnDestroyedOrNull(TargetIndex.B);

        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);

        var work = new Toil
        {
            initAction = () =>
            {
                workLeft = WorkNeeded(job.targetA.Thing?.def?.defName);
            },
            tickAction = () =>
            {
                var actor = pawn;
                try { actor.rotationTracker.FaceCell(TargetB.Cell); } catch { }
                try { actor.skills?.Learn(SkillDefOf.Construction, 0.275f); } catch { }

                float speed = 1f;
                try { speed = actor.GetStatValue(StatDefOf.ConstructionSpeed); } catch { }
                workLeft -= speed;
                if (workLeft <= 0f)
                {
                    var part = job.targetA.Thing;
                    var origin = job.targetB.Thing;
                    ReworkInstalledPartRuntime.Notify_Uninstalled(actor, part, origin);
                    actor.jobs?.EndCurrentJob(JobCondition.Succeeded, true);
                }
            },
            defaultCompleteMode = ToilCompleteMode.Never,
        };
        work.FailOnDestroyedOrNull(TargetIndex.A);
        work.FailOnDestroyedOrNull(TargetIndex.B);
        yield return work;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref workLeft, "workLeft", -1f);
    }

    private static int WorkNeeded(string? partDefName)
    {
        if (!string.IsNullOrEmpty(partDefName))
        {
            var data = ReworkInstalledPartRegistry.Get(partDefName);
            if (data != null)
                return Mathf.Clamp(data.WorkToUninstall, 20, 3000);
        }
        return 500;
    }
}
