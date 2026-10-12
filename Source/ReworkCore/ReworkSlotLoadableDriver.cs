using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// Driver de recoger para slots ([ReworkSlotLoadable]).
/// TargetA = item a recoger. El slot viaja en `job.count - 1` (el driver del
/// framework lo pone así: count = slotIndex + 1).
///
/// Al llegar: parte 1 del stack y lo carga en el primer slot compatible del
/// arma equipada (o en el slot pedido si sigue libre y acepta el item).
/// </summary>
public class ReworkJobDriver_GatherSlotItem : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return true;
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);

        yield return Toils_Reserve.Reserve(TargetIndex.A, 1);

        var gotoThing = new Toil
        {
            initAction = () =>
            {
                try { pawn.pather.StartPath(TargetThingA, PathEndMode.ClosestTouch); } catch { }
            },
            defaultCompleteMode = ToilCompleteMode.PatherArrival,
        };
        gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        yield return gotoThing;

        yield return new Toil
        {
            initAction = () =>
            {
                try
                {
                    var item = job.targetA.Thing;
                    if (item == null) return;
                    var weapon = pawn.equipment?.Primary;
                    if (weapon?.def == null) return;
                    var data = Rework.ReworkSlotLoadableRegistry.Get(weapon.def.defName);
                    if (data == null) return;

                    int wantSlot = job.count - 1;
                    if (wantSlot >= 0 && wantSlot < data.Slots.Count)
                    {
                        var slot = data.Slots[wantSlot];
                        if (ReworkSlotLoadableRuntime.LoadedInSlot(weapon, wantSlot) == null
                            && SlotAccepts(slot, item.def.defName))
                        {
                            ReworkSlotLoadableRuntime.TryLoadSlot(weapon, wantSlot, item);
                            return;
                        }
                    }
                    // Si el pedido no vale, primer slot libre compatible.
                    for (int i = 0; i < data.Slots.Count; i++)
                    {
                        if (ReworkSlotLoadableRuntime.LoadedInSlot(weapon, i) != null) continue;
                        if (!SlotAccepts(data.Slots[i], item.def.defName)) continue;
                        ReworkSlotLoadableRuntime.TryLoadSlot(weapon, i, item);
                        return;
                    }
                    Messages.Message($"{weapon.LabelShort} no tiene slot libre para {item.LabelShort}.",
                        MessageTypeDefOf.RejectInput, false);
                }
                catch { }
            },
            defaultCompleteMode = ToilCompleteMode.Instant,
        };
    }

    private static bool SlotAccepts(Rework.ReworkSlotSpec slot, string defName)
    {
        foreach (var s in slot.SlottableDefNames)
        {
            if (string.Equals(s, defName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
