using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de cleaves ([ReworkCleave]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo crea el DamageDef (si no existe) con
/// `workerClass = ReworkDamageWorker_Cleave` y resuelve el daño del salpicado.
///
/// La clase que lleva el atributo solo es el portador (igual que los
/// portadores de [ReworkUnlock]): el comportamiento vive en el worker, que
/// vanilla instancia por golpe. La clase puede ser cualquiera (se valida que
/// exista, nada más).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkCleaveRegistrar
{
    private static readonly Dictionary<string, DamageDef> registered = new();

    static ReworkCleaveRegistrar()
    {
        RegisterAll();
    }

    public static DamageDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<DamageDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkCleaveAttribute>();
                    if (attr == null) continue;

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkCleaveRegistrar: {count} cleave(s) declarativo(s) [ReworkCleave] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkCleaveAttribute attr, Type carrierType)
    {
        try
        {
            var existing = DefDatabase<DamageDef>.GetNamedSilentFail(attr.DefName);
            if (existing == null)
            {
                existing = new DamageDef
                {
                    defName = attr.DefName,
                    label = attr.Label ?? attr.DefName,
                    workerClass = typeof(ReworkDamageWorker_Cleave),
                };
                DefDatabase<DamageDef>.Add(existing);
                registered[attr.DefName] = existing;
                ReworkContentSelfCheck.RegisterDef(attr.DefName, "DamageDef", true);
                Lg.Info($"[ReworkCleave] Registrado DamageDef '{attr.DefName}' con worker cleave.");
            }
            else
            {
                registered[attr.DefName] = existing;
                if (existing.workerClass != typeof(ReworkDamageWorker_Cleave))
                {
                    Lg.Error($"[ReworkCleave] '{attr.DefName}' ya existe con otro worker ({existing.workerClass?.Name}); el cleave NO se aplicará a ese daño.");
                    return false;
                }
            }

            object? splash = null;
            if (!string.IsNullOrWhiteSpace(attr.CleaveDamage))
            {
                var dd = DefDatabase<DamageDef>.GetNamedSilentFail(attr.CleaveDamage.Trim());
                if (dd == null)
                    Lg.Error($"[ReworkCleave] '{attr.DefName}': CleaveDamage '{attr.CleaveDamage}' no existe; el salpicado usará el mismo daño.");
                else
                    splash = dd;
            }

            if (attr.CleaveFactor < 0f || attr.CleaveFactor > 1f)
                Lg.Error($"[ReworkCleave] '{attr.DefName}': CleaveFactor debe estar entre 0 y 1.");

            ReworkCleaveRegistry.Register(new ReworkCleaveRegistry.CleaveData
            {
                DefName = attr.DefName,
                CleaveTargets = attr.CleaveTargets,
                CleaveFactor = attr.CleaveFactor,
                CleaveDamageDef = splash,
                ArmorPenetration = attr.ArmorPenetration,
                Label = attr.Label ?? attr.DefName,
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkCleave] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}
