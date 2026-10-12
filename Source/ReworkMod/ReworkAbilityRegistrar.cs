using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador del puente de habilidades ([ReworkAbility] + [ReworkPassive]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con atributo crea el AbilityDef vanilla (verbo + efectos
/// simples del CSV + CompClasses vanilla si se piden) y los HediffDefs de
/// pasivos. Además engancha la concesión al generar ([ReworkHook(PawnCreated)]
/// vía método propio (el hook lo procesa el pipeline como cualquier mod).
///
/// Los efectos simples se mapean a comps del framework (ReworkEffect_*):
/// vanilla los ejecuta al disparar sin código custom por habilidad.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkAbilityRegistrar
{
    static ReworkAbilityRegistrar()
    {
        RegisterAll();
    }

    public static void RegisterAll()
    {
        int abilities = 0, passives = 0;
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
                    foreach (var attr in type.GetCustomAttributes<ReworkAbilityAttribute>())
                    {
                        if (RegisterAbility(attr, type)) abilities++;
                    }
                    foreach (var attr in type.GetCustomAttributes<ReworkPassiveAttribute>())
                    {
                        if (RegisterPassive(attr, type)) passives++;
                    }
                }
            }
        }

        Lg.Info($"ReworkAbilityRegistrar: {abilities} habilidad(es) [ReworkAbility] y {passives} pasivo(s) [ReworkPassive] registrado(s).");
    }

    private static bool RegisterAbility(ReworkAbilityAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.DefName))
            {
                Lg.Error($"[ReworkAbility] '{carrierType.FullName}': DefName vacío; se ignora.");
                return false;
            }

            var existing = DefDatabase<AbilityDef>.GetNamedSilentFail(attr.DefName.Trim());
            AbilityDef def;
            if (existing != null)
            {
                def = existing;
            }
            else
            {
                Type abilityClass = typeof(Ability);
                if (!string.IsNullOrWhiteSpace(attr.AbilityClass))
                {
                    var t = GenTypes.GetTypeInAnyAssembly(attr.AbilityClass.Trim());
                    if (t == null || !typeof(Ability).IsAssignableFrom(t))
                        Lg.Error($"[ReworkAbility] '{attr.DefName}': AbilityClass '{attr.AbilityClass}' no existe o no hereda de Ability; vanilla.");
                    else
                        abilityClass = t;
                }

                def = new AbilityDef
                {
                    defName = attr.DefName.Trim(),
                    label = attr.Label ?? attr.DefName,
                    description = attr.Description ?? "Habilidad registrada por Rework.",
                    abilityClass = abilityClass,
                    verbProperties = new VerbProperties
                    {
                        range = Math.Max(1f, attr.Range),
                        warmupTime = Math.Max(0f, attr.WarmupTime),
                        requireLineOfSight = attr.RequireLineOfSight,
                    },
                    cooldownTicksRange = new IntRange(
                        (int)Math.Max(0f, attr.CooldownTime * 60f),
                        (int)Math.Max(0f, attr.CooldownTime * 60f)),
                    targetRequired = true,
                };
                def.comps = BuildEffectComps(attr, def);
                DefDatabase<AbilityDef>.Add(def);
                ReworkContentSelfCheck.RegisterDef(def.defName, "AbilityDef", true);
                Lg.Info($"[ReworkAbility] AbilityDef '{def.defName}' creado ({def.comps?.Count ?? 0} efecto(s)).");
            }

            var kinds = new List<string>();
            if (!string.IsNullOrWhiteSpace(attr.GrantTo))
            {
                foreach (var k in attr.GrantTo.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = k.Trim();
                    if (DefDatabase<PawnKindDef>.GetNamedSilentFail(name) == null)
                        Lg.Error($"[ReworkAbility] '{attr.DefName}': GrantTo '{name}' no existe; se ignora.");
                    else
                        kinds.Add(name);
                }
            }

            ReworkAbilityRegistry.RegisterAbility(new ReworkAbilityRegistry.AbilityData
            {
                DefName = def.defName,
                Label = def.label,
                Description = def.description,
                Range = attr.Range,
                WarmupTime = attr.WarmupTime,
                CooldownTime = attr.CooldownTime,
                RequireLineOfSight = attr.RequireLineOfSight,
                EffectsCsv = attr.Effects,
                EffectClassesCsv = attr.EffectClasses,
                GrantToColonists = attr.GrantToColonists,
                GrantToKinds = kinds.ToArray(),
                AbilityClassName = attr.AbilityClass,
                AbilityDef = def,
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkAbility] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }

    private static List<AbilityCompProperties> BuildEffectComps(ReworkAbilityAttribute attr, AbilityDef def)
    {
        var out_ = new List<AbilityCompProperties>();
        if (!string.IsNullOrWhiteSpace(attr.Effects))
        {
            foreach (var part in attr.Effects.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split(':');
                if (f.Length < 2)
                {
                    Lg.Error($"[ReworkAbility] '{attr.DefName}': efecto '{part}' mal formado; se ignora.");
                    continue;
                }
                string kind = f[0].Trim().ToLowerInvariant();
                if (kind.StartsWith("hediff") && f.Length >= 3)
                {
                    if (DefDatabase<HediffDef>.GetNamedSilentFail(f[1].Trim()) == null)
                    {
                        Lg.Error($"[ReworkAbility] '{attr.DefName}': hediff '{f[1]}' no existe; se ignora.");
                        continue;
                    }
                    if (!float.TryParse(f[2].Trim(), out float sev)) sev = 1f;
                    out_.Add(new Rework.Core.ReworkEffectProps_Hediff { compClass = typeof(Rework.Core.ReworkEffect_Hediff), hediffDefName = f[1].Trim(), severity = sev });
                }
                else if (kind.StartsWith("damage") && f.Length >= 3)
                {
                    if (DefDatabase<DamageDef>.GetNamedSilentFail(f[1].Trim()) == null)
                    {
                        Lg.Error($"[ReworkAbility] '{attr.DefName}': daño '{f[1]}' no existe; se ignora.");
                        continue;
                    }
                    if (!float.TryParse(f[2].Trim(), out float amt)) amt = 10f;
                    out_.Add(new Rework.Core.ReworkEffectProps_Damage { compClass = typeof(Rework.Core.ReworkEffect_Damage), damageDefName = f[1].Trim(), amount = amt });
                }
                else if (kind.StartsWith("stun"))
                {
                    int ticks = 120;
                    if (f.Length >= 2 && int.TryParse(f[1].Trim(), out int t)) ticks = t;
                    out_.Add(new Rework.Core.ReworkEffectProps_Stun { compClass = typeof(Rework.Core.ReworkEffect_Stun), ticks = ticks });
                }
                else if (kind.StartsWith("spawn") && f.Length >= 2)
                {
                    if (DefDatabase<PawnKindDef>.GetNamedSilentFail(f[1].Trim()) == null)
                    {
                        Lg.Error($"[ReworkAbility] '{attr.DefName}': spawn '{f[1]}' no existe; se ignora.");
                        continue;
                    }
                    int n = 1;
                    if (f.Length >= 3 && int.TryParse(f[2].Trim(), out int c)) n = Math.Max(1, c);
                    out_.Add(new Rework.Core.ReworkEffectProps_Spawn { compClass = typeof(Rework.Core.ReworkEffect_Spawn), pawnKindDefName = f[1].Trim(), count = n });
                }
                else
                {
                    Lg.Error($"[ReworkAbility] '{attr.DefName}': efecto '{part}' desconocido (hediff/damage/stun/spawn); se ignora.");
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(attr.EffectClasses))
        {
            foreach (var c in attr.EffectClasses.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = GenTypes.GetTypeInAnyAssembly(c.Trim());
                if (t == null || !typeof(CompAbilityEffect).IsAssignableFrom(t))
                {
                    Lg.Error($"[ReworkAbility] '{attr.DefName}': EffectClass '{c}' no existe o no es CompAbilityEffect; se ignora.");
                    continue;
                }
                try
                {
                    var props = (AbilityCompProperties)Activator.CreateInstance(typeof(AbilityCompProperties));
                    props.compClass = t;
                    out_.Add(props);
                }
                catch { }
            }
        }
        return out_;
    }

    private static bool RegisterPassive(ReworkPassiveAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.Id))
            {
                Lg.Error($"[ReworkPassive] '{carrierType.FullName}': Id vacío; se ignora.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(attr.Stats))
            {
                Lg.Error($"[ReworkPassive] '{attr.Id}': Stats vacío; se ignora.");
                return false;
            }

            var stats = new List<(string, float)>();
            foreach (var part in attr.Stats.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split(':');
                if (f.Length < 2)
                {
                    Lg.Error($"[ReworkPassive] '{attr.Id}': stat '{part}' mal formado (Stat:offset); se ignora.");
                    continue;
                }
                if (DefDatabase<StatDef>.GetNamedSilentFail(f[0].Trim()) == null)
                {
                    Lg.Error($"[ReworkPassive] '{attr.Id}': stat '{f[0]}' no existe; se ignora.");
                    continue;
                }
                if (!float.TryParse(f[1].Trim(), out float off))
                {
                    Lg.Error($"[ReworkPassive] '{attr.Id}': offset '{f[1]}' inválido; se ignora.");
                    continue;
                }
                stats.Add((f[0].Trim(), off));
            }
            if (stats.Count == 0) return false;

            if (!string.IsNullOrWhiteSpace(attr.RequiresHediff)
                && DefDatabase<HediffDef>.GetNamedSilentFail(attr.RequiresHediff.Trim()) == null)
                Lg.Error($"[ReworkPassive] '{attr.Id}': RequiresHediff '{attr.RequiresHediff}' no existe; el pasivo será incondicional.");

            // HediffDef del pasivo (con statOffsets fijos: los stats ya validados).
            string hediffName = "Rework_Passive_" + attr.Id;
            if (DefDatabase<HediffDef>.GetNamedSilentFail(hediffName) == null)
            {
                var def = new HediffDef
                {
                    defName = hediffName,
                    label = "pasivo: " + attr.Id,
                    description = "Buff pasivo registrado por Rework.",
                    initialSeverity = 1f,
                    maxSeverity = 1f,
                };
                def.stages = new List<HediffStage>
                {
                    new HediffStage
                    {
                        minSeverity = 0f,
                        statOffsets = new List<StatModifier>(),
                    },
                };
                foreach (var (stat, off) in stats)
                {
                    var sd = DefDatabase<StatDef>.GetNamedSilentFail(stat);
                    if (sd != null)
                        def.stages[0].statOffsets.Add(new StatModifier { stat = sd, value = off });
                }
                DefDatabase<HediffDef>.Add(def);
            }

            ReworkAbilityRegistry.RegisterPassive(new ReworkAbilityRegistry.PassiveData
            {
                Id = attr.Id,
                RequiresHediff = string.IsNullOrWhiteSpace(attr.RequiresHediff) ? null : attr.RequiresHediff.Trim(),
                Stats = stats,
            });
            Lg.Info($"[ReworkPassive] '{attr.Id}' registrado ({stats.Count} stat(s)).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkPassive] Error registrando '{attr.Id}': {e.Message}");
            return false;
        }
    }
}
