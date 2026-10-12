using System;

namespace Rework;

/// <summary>
/// Declara una habilidad sobre el sistema vanilla de Abilities (Royalty) sin
/// XML: el framework crea el AbilityDef con su verbo y efectos, y se lo
/// concede a quien se indique.
///
/// NO reinventa la magia (vanilla 1.6 ya trae 70+ CompAbilityEffect): el
/// puente cubre lo que vanilla no da sin XML (registro + concesión masiva) y
/// lo que JecsTools aportaba y vanilla no tiene (pasivos por ticker con
/// [ReworkPassive], IA de casteo enemiga con el JobGiver del framework).
///
/// Efectos simples por CSV (`Effects = "hediff:HediffDef:2.5,damage:Burn:10"`):
/// hediff (al objetivo), damage (daño directo), stun (ticks) y spawn
/// (PawnKind cerca del objetivo). Para efectos complejos, usa CompClasses
/// vanilla vía `EffectClasses` (nombres de CompAbilityEffect, separados por
/// coma; sus props por defecto).
///
/// Concesión: `GrantToColonists` (todos los colonos al generar), `GrantTo`
/// (PawnKinds CSV) o manual (`ReworkAbilityRuntime.Grant(pawn, defName)`).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkAbilityAttribute : Attribute
{
    /// <summary>Nombre único del AbilityDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Etiqueta visible.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción.</summary>
    public string? Description { get; set; }

    /// <summary>Rango en celdas.</summary>
    public float Range { get; set; } = 24f;

    /// <summary>Segundos de espera antes de lanzar.</summary>
    public float WarmupTime { get; set; } = 1f;

    /// <summary>Segundos de recarga.</summary>
    public float CooldownTime { get; set; } = 60f;

    /// <summary>Si necesita línea de visión.</summary>
    public bool RequireLineOfSight { get; set; } = true;

    /// <summary>Efectos: "hediff:Def:sev,damage:Def:cant,stun:ticks,spawn:PawnKind" separados por ';'.</summary>
    public string? Effects { get; set; }

    /// <summary>Clases CompAbilityEffect vanilla extra, separadas por coma.</summary>
    public string? EffectClasses { get; set; }

    /// <summary>Si se concede a todos los colonos al generar.</summary>
    public bool GrantToColonists { get; set; } = false;

    /// <summary>PawnKinds que la reciben al generar, separados por coma.</summary>
    public string? GrantTo { get; set; }

    /// <summary>Subclase de Ability (vacío = vanilla).</summary>
    public string? AbilityClass { get; set; }

    public ReworkAbilityAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Declara un buff pasivo: mientras el portador tenga el hediff/gen indicado,
/// recibe statOffsets (vía hediff oculto generado, como los slots). Es lo que
/// vanilla NO da (sus pasivos son comps por habilidad concreta).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkPassiveAttribute : Attribute
{
    /// <summary>Id único del pasivo.</summary>
    public string Id { get; }

    /// <summary>defName del HediffDef que activa el pasivo (vacío = siempre).</summary>
    public string? RequiresHediff { get; set; }

    /// <summary>Stats: "StatDef:offset" separados por coma.</summary>
    public string Stats { get; set; } = "";

    public ReworkPassiveAttribute(string id)
    {
        Id = id;
    }
}

/// <summary>
/// Registro de habilidades y pasivos declarativos.
/// </summary>
public static class ReworkAbilityRegistry
{
    public class AbilityData
    {
        public string DefName = "";
        public string Label = "";
        public string Description = "";
        public float Range = 24f;
        public float WarmupTime = 1f;
        public float CooldownTime = 60f;
        public bool RequireLineOfSight = true;
        public string? EffectsCsv;
        public string? EffectClassesCsv;
        public bool GrantToColonists;
        public string[] GrantToKinds = Array.Empty<string>();
        public string? AbilityClassName;
        public object? AbilityDef;
    }

    public class PassiveData
    {
        public string Id = "";
        public string? RequiresHediff;
        public System.Collections.Generic.List<(string stat, float offset)> Stats = new();
    }

    private static readonly System.Collections.Generic.Dictionary<string, AbilityData> abilities =
        new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Dictionary<string, PassiveData> passives =
        new(System.StringComparer.OrdinalIgnoreCase);

    public static void RegisterAbility(AbilityData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        abilities[data.DefName] = data;
    }

    public static void RegisterPassive(PassiveData data)
    {
        if (data == null || string.IsNullOrEmpty(data.Id)) return;
        passives[data.Id] = data;
    }

    public static AbilityData? GetAbility(string defName)
    {
        abilities.TryGetValue(defName, out var d);
        return d;
    }

    public static System.Collections.Generic.IReadOnlyDictionary<string, AbilityData> AllAbilities => abilities;
    public static System.Collections.Generic.IReadOnlyDictionary<string, PassiveData> AllPassives => passives;
    public static int AbilityCount => abilities.Count;
    public static int PassiveCount => passives.Count;
}
