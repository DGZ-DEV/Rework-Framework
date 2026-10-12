using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de daños periódicos ([ReworkDamageOverTime]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo crea el HediffDef (si no existe) y resuelve
/// el DamageDef del ciclo en ReworkDamageOverTimeRegistry.
///
/// El daño por ciclo usa el camino vanilla completo (Thing.TakeDamage:
/// armadura, absorción, notificaciones). La cuenta de ciclos por hediff la
/// lleva ReworkDamageOverTimeRuntime, enganchado al tick global.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkDamageOverTimeRegistrar
{
    private static readonly Dictionary<string, HediffDef> registered = new();

    static ReworkDamageOverTimeRegistrar()
    {
        RegisterAll();
    }

    public static HediffDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<HediffDef>.GetNamedSilentFail(defName);
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
                    if (type == null || !type.IsClass || type.IsAbstract) continue;
                    var attr = type.GetCustomAttribute<ReworkDamageOverTimeAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Hediff).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkDamageOverTime] '{type.FullName}' tiene [ReworkDamageOverTime] pero no hereda de Verse.Hediff.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkDamageOverTimeRegistrar: {count} DoT(s) declarativo(s) [ReworkDamageOverTime] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkDamageOverTimeAttribute attr, Type hediffType)
    {
        try
        {
            var existing = DefDatabase<HediffDef>.GetNamedSilentFail(attr.DefName);
            if (existing == null)
            {
                existing = new HediffDef
                {
                    defName = attr.DefName,
                    label = attr.Label ?? attr.DefName,
                    description = attr.Description ?? "Daño periódico registrado por Rework.",
                    hediffClass = hediffType,
                    initialSeverity = 1f,
                };
                DefDatabase<HediffDef>.Add(existing);
                registered[attr.DefName] = existing;
                ReworkContentSelfCheck.RegisterDef(attr.DefName, "HediffDef", true);
                Lg.Info($"[ReworkDamageOverTime] Registrado HediffDef '{attr.DefName}' -> {hediffType.Name}.");
            }
            else
            {
                registered[attr.DefName] = existing;
            }

            object? cycleDef = null;
            string cycleName = string.IsNullOrWhiteSpace(attr.CycleDamage) ? "Bite" : attr.CycleDamage.Trim();
            var dd = DefDatabase<DamageDef>.GetNamedSilentFail(cycleName);
            if (dd == null)
            {
                Lg.Error($"[ReworkDamageOverTime] '{attr.DefName}': CycleDamage '{cycleName}' no existe; el DoT NO aplicará daño hasta corregirlo.");
            }
            else
            {
                cycleDef = dd;
            }

            if (attr.CycleInTicks <= 0)
            {
                Lg.Error($"[ReworkDamageOverTime] '{attr.DefName}': CycleInTicks debe ser positivo; se usará 30000.");
                attr.CycleInTicks = 30000;
            }

            ReworkDamageOverTimeRegistry.Register(new ReworkDamageOverTimeRegistry.CycleData
            {
                DefName = attr.DefName,
                HandlerType = hediffType,
                CycleDamageName = cycleName,
                CycleDamageAmt = attr.CycleDamageAmt,
                CycleInTicks = attr.CycleInTicks,
                ArmorPenetration = attr.ArmorPenetration,
                CycleDamageDef = cycleDef,
                Label = attr.Label ?? attr.DefName,
                Description = attr.Description ?? "Daño periódico registrado por Rework.",
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDamageOverTime] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}
