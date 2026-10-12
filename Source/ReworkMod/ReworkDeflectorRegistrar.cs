using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de deflectores ([ReworkDeflector]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo valida que el arma exista y resuelve
/// skills y sonido. El arma NO se crea (es vanilla o de otro mod): solo se
/// marca su defName en el registro.
///
/// La clase que lleva el atributo es solo el portador (puede ser cualquiera).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkDeflectorRegistrar
{
    static ReworkDeflectorRegistrar()
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
                    foreach (var attr in type.GetCustomAttributes<ReworkDeflectorAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkDeflectorRegistrar: {count} deflector(es) declarativo(s) [ReworkDeflector] registrado(s).");
    }

    private static bool Register(ReworkDeflectorAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.WeaponDefName))
            {
                Lg.Error($"[ReworkDeflector] '{carrierType.FullName}': WeaponDefName vacío; se ignora.");
                return false;
            }

            var weapon = DefDatabase<ThingDef>.GetNamedSilentFail(attr.WeaponDefName.Trim());
            if (weapon == null)
            {
                Lg.Error($"[ReworkDeflector] '{carrierType.FullName}': arma '{attr.WeaponDefName}' no existe; el deflector NO funcionará hasta corregirlo.");
                return false;
            }

            object? deflectSkill = ResolveSkill(attr.DeflectSkill, attr.WeaponDefName, "desvío");
            object? reflectSkill = attr.CanReflect
                ? ResolveSkill(attr.ReflectSkill, attr.WeaponDefName, "reflect")
                : null;
            object? sound = null;
            if (!string.IsNullOrWhiteSpace(attr.DeflectSound))
            {
                sound = DefDatabase<SoundDef>.GetNamedSilentFail(attr.DeflectSound.Trim());
                if (sound == null)
                    Lg.Error($"[ReworkDeflector] '{attr.WeaponDefName}': sonido '{attr.DeflectSound}' no existe; sin sonido.");
            }

            if (attr.BaseDeflectChance < 0f || attr.BaseDeflectChance > 1f)
                Lg.Error($"[ReworkDeflector] '{attr.WeaponDefName}': BaseDeflectChance debe estar entre 0 y 1.");

            ReworkDeflectorRegistry.Register(new ReworkDeflectorRegistry.DeflectorData
            {
                WeaponDefName = weapon.defName,
                BaseDeflectChance = attr.BaseDeflectChance,
                DeflectSkillDef = deflectSkill,
                DeflectRatePerSkillPoint = attr.DeflectRatePerSkillPoint,
                DeflectSkillLearnRate = attr.DeflectSkillLearnRate,
                CanReflect = attr.CanReflect,
                ReflectSkillDef = reflectSkill,
                ReflectRatePerSkillPoint = attr.ReflectRatePerSkillPoint,
                ReflectSkillLearnRate = attr.ReflectSkillLearnRate,
                UseManipulationInCalc = attr.UseManipulationInCalc,
                DeflectSoundDef = sound,
            });
            Lg.Info($"[ReworkDeflector] Arma '{weapon.defName}' marcada como deflectora.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDeflector] Error registrando '{attr.WeaponDefName}': {e.Message}");
            return false;
        }
    }

    private static object? ResolveSkill(string? name, string owner, string role)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sd = DefDatabase<SkillDef>.GetNamedSilentFail(name.Trim());
        if (sd == null)
            Lg.Error($"[ReworkDeflector] '{owner}': skill de {role} '{name}' no existe; sin bonus de skill.");
        return sd;
    }
}
