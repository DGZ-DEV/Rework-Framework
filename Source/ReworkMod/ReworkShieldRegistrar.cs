using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de escudos ([ReworkShield]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo valida que el escudo exista y resuelve
/// skill y sonido. Además garantiza el HediffDef de fatiga
/// (`Rework_ShieldFatigue`, 4 stages que penalizan manipulación y movimiento,
/// igual que el original) si algún escudo la usa.
///
/// El escudo NO se crea (es vanilla o de otro mod): solo se marca su defName.
/// La clase que lleva el atributo es solo el portador.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkShieldRegistrar
{
    static ReworkShieldRegistrar()
    {
        RegisterAll();
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
                    foreach (var attr in type.GetCustomAttributes<ReworkShieldAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkShieldRegistrar: {count} escudo(s) declarativo(s) [ReworkShield] registrado(s).");
    }

    private static bool Register(ReworkShieldAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.ShieldDefName))
            {
                Lg.Error($"[ReworkShield] '{carrierType.FullName}': ShieldDefName vacío; se ignora.");
                return false;
            }

            var shield = DefDatabase<ThingDef>.GetNamedSilentFail(attr.ShieldDefName.Trim());
            if (shield == null)
            {
                Lg.Error($"[ReworkShield] '{carrierType.FullName}': escudo '{attr.ShieldDefName}' no existe; el escudo NO funcionará hasta corregirlo.");
                return false;
            }

            object? skill = null;
            if (!string.IsNullOrWhiteSpace(attr.BlockSkill))
            {
                skill = DefDatabase<SkillDef>.GetNamedSilentFail(attr.BlockSkill.Trim());
                if (skill == null)
                    Lg.Error($"[ReworkShield] '{attr.ShieldDefName}': skill '{attr.BlockSkill}' no existe; sin bonus de skill.");
            }

            object? sound = null;
            if (!string.IsNullOrWhiteSpace(attr.BlockSound))
            {
                sound = DefDatabase<SoundDef>.GetNamedSilentFail(attr.BlockSound.Trim());
                if (sound == null)
                    Lg.Error($"[ReworkShield] '{attr.ShieldDefName}': sonido '{attr.BlockSound}' no existe; sonido por defecto.");
            }

            if (attr.BaseMeleeBlockChance < 0f || attr.BaseMeleeBlockChance > 1f
                || attr.BaseRangedBlockChance < 0f || attr.BaseRangedBlockChance > 1f)
                Lg.Error($"[ReworkShield] '{attr.ShieldDefName}': las chances deben estar entre 0 y 1.");

            if (attr.UseFatigue)
                EnsureFatigueDef();

            ReworkShieldRegistry.Register(new ReworkShieldRegistry.ShieldData
            {
                ShieldDefName = shield.defName,
                CanBlockMelee = attr.CanBlockMelee,
                CanBlockRanged = attr.CanBlockRanged,
                BaseMeleeBlockChance = attr.BaseMeleeBlockChance,
                BaseRangedBlockChance = attr.BaseRangedBlockChance,
                BlockSkillDef = skill,
                BlockRatePerSkillPoint = attr.BlockRatePerSkillPoint,
                ShieldTakeDamage = attr.ShieldTakeDamage,
                ShieldDamageAbsorbed = attr.ShieldDamageAbsorbed,
                CanBeAutoDiscarded = attr.CanBeAutoDiscarded,
                HealthAutoDiscardThreshold = attr.HealthAutoDiscardThreshold,
                UseFatigue = attr.UseFatigue,
                DamageToFatigueFactor = attr.DamageToFatigueFactor,
                BlockSoundDef = sound,
            });
            Lg.Info($"[ReworkShield] '{shield.defName}' marcado como escudo.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkShield] Error registrando '{attr.ShieldDefName}': {e.Message}");
            return false;
        }
    }

    private static void EnsureFatigueDef()
    {
        try
        {
            const string defName = ReworkShieldRuntime.FatigueDefName;
            if (DefDatabase<HediffDef>.GetNamedSilentFail(defName) != null)
                return;

            var def = new HediffDef
            {
                defName = defName,
                label = "fatiga de escudo",
                description = "El brazo tiembla de sostener el escudo golpe tras golpe.",
                initialSeverity = 0f,
                maxSeverity = 1f,
                isBad = true,
            };
            def.stages = new List<HediffStage>
            {
                new HediffStage { minSeverity = 0f, label = "fresco" },
                new HediffStage
                {
                    minSeverity = 0.25f,
                    label = "cansado",
                    capMods = new List<PawnCapacityModifier>
                    {
                        new PawnCapacityModifier { capacity = PawnCapacityDefOf.Manipulation, offset = -0.1f },
                    },
                },
                new HediffStage
                {
                    minSeverity = 0.5f,
                    label = "fatigado",
                    capMods = new List<PawnCapacityModifier>
                    {
                        new PawnCapacityModifier { capacity = PawnCapacityDefOf.Manipulation, offset = -0.25f },
                        new PawnCapacityModifier { capacity = PawnCapacityDefOf.Moving, offset = -0.1f },
                    },
                },
                new HediffStage
                {
                    minSeverity = 0.75f,
                    label = "exhausto",
                    capMods = new List<PawnCapacityModifier>
                    {
                        new PawnCapacityModifier { capacity = PawnCapacityDefOf.Manipulation, offset = -0.5f },
                        new PawnCapacityModifier { capacity = PawnCapacityDefOf.Moving, offset = -0.25f },
                    },
                },
            };
            DefDatabase<HediffDef>.Add(def);
            Lg.Info($"[ReworkShield] HediffDef '{defName}' de fatiga creado (4 stages).");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkShield] No se pudo crear el HediffDef de fatiga: {e.Message}");
        }
    }
}
