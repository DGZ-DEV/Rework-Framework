using RimWorld;

using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Props serializables para los efectos simples del framework. Llevan los
/// parámetros del CSV (el comp los lee de sus props al disparar).
/// Viven en Core (junto a los comps) para no crear referencia circular
/// Core→Mod. El registrador (Mod) las instancia por tipo.
/// </summary>
public class ReworkEffectProps_Hediff : AbilityCompProperties
{
    public string? hediffDefName;
    public float severity = 1f;
}

public class ReworkEffectProps_Damage : AbilityCompProperties
{
    public string? damageDefName = "Burn";
    public float amount = 10f;
}

public class ReworkEffectProps_Stun : AbilityCompProperties
{
    public int ticks = 120;
}

public class ReworkEffectProps_Spawn : AbilityCompProperties
{
    public string? pawnKindDefName;
    public int count = 1;
}
