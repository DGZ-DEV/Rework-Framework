using Rework.Core;
using Verse;

namespace Rework;

public static class ReworkFrameworkLifecycleHooks
{
	[ReworkHook(ReworkHookPoint.PawnCreated)]
	public static void OnPawnCreated(Pawn pawn)
	{
		ReworkRuntime.StatusEffectRuntime.TryApplyNewbornAuto(pawn);
	}
}
