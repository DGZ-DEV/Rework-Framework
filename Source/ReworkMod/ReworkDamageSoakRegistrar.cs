using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de soaks declarativos ([ReworkDamageSoak]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo crea el HediffDef (si no existe) y resuelve
/// los defNames de DamageDef a referencias reales en ReworkDamageSoakRegistry.
///
/// La resolución se hace UNA vez aquí, no en el golpe: el despacho de daño
/// compara por referencia y jamás toca DefDatabase en el camino caliente.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkDamageSoakRegistrar
{
    private static readonly Dictionary<string, HediffDef> registered = new();

    static ReworkDamageSoakRegistrar()
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
                    var attr = type.GetCustomAttribute<ReworkDamageSoakAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Hediff).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkDamageSoak] '{type.FullName}' tiene [ReworkDamageSoak] pero no hereda de Verse.Hediff.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkDamageSoakRegistrar: {count} soak(s) declarativo(s) [ReworkDamageSoak] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkDamageSoakAttribute attr, Type hediffType)
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
                    description = attr.Description ?? "Absorción de daño registrada por Rework.",
                    hediffClass = hediffType,
                    initialSeverity = 1f,
                };
                DefDatabase<HediffDef>.Add(existing);
                registered[attr.DefName] = existing;
                ReworkContentSelfCheck.RegisterDef(attr.DefName, "HediffDef", true);
                Lg.Info($"[ReworkDamageSoak] Registrado HediffDef '{attr.DefName}' -> {hediffType.Name}.");
            }
            else
            {
                registered[attr.DefName] = existing;
            }

            // Resolver el filtro de tipo de daño (null = todos).
            object? damageTypeDef = null;
            if (!string.IsNullOrWhiteSpace(attr.DamageType))
            {
                var dd = DefDatabase<DamageDef>.GetNamedSilentFail(attr.DamageType.Trim());
                if (dd == null)
                {
                    Lg.Error($"[ReworkDamageSoak] '{attr.DefName}': DamageType '{attr.DamageType}' no existe; el soak NO absorberá nada hasta corregirlo.");
                }
                else
                {
                    damageTypeDef = dd;
                }
            }

            // Resolver exclusiones.
            var excluded = new List<object>();
            if (!string.IsNullOrWhiteSpace(attr.DamageTypesToExclude))
            {
                var parts = attr.DamageTypesToExclude.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var name = part.Trim();
                    if (name.Length == 0) continue;
                    var dd = DefDatabase<DamageDef>.GetNamedSilentFail(name);
                    if (dd == null)
                    {
                        Lg.Error($"[ReworkDamageSoak] '{attr.DefName}': exclusión '{name}' no existe; se ignora.");
                        continue;
                    }
                    excluded.Add(dd);
                }
            }

            ReworkDamageSoakRegistry.Register(new ReworkDamageSoakRegistry.SoakData
            {
                DefName = attr.DefName,
                HandlerType = hediffType,
                DamageToSoak = attr.DamageToSoak,
                DamageTypeName = string.IsNullOrWhiteSpace(attr.DamageType) ? null : attr.DamageType.Trim(),
                ExcludedTypeNames = SplitNames(attr.DamageTypesToExclude),
                ShowAbsorbedMote = attr.ShowAbsorbedMote,
                DamageTypeDef = damageTypeDef,
                ExcludedTypeDefs = excluded.ToArray(),
                Label = attr.Label ?? attr.DefName,
                Description = attr.Description ?? "Absorción de daño registrada por Rework.",
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDamageSoak] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }

    private static string[] SplitNames(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var out_ = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) out_.Add(t);
        }
        return out_.ToArray();
    }
}
