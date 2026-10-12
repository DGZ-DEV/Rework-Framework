using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de daños extra ([ReworkExtraMelee]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo crea el HediffDef (si no existe), parsea el
/// CSV "Def:Cantidad[:Pen[:Chance]]" y resuelve los DamageDefs.
///
/// Formato por entrada: `Toxic:3` (3 de tóxico), `Burn:2:0.5` (+0.5 pen),
/// `Cut:4:0:0.5` (4 de corte al 50%). Entradas mal formadas = error claro,
/// nunca NRE en el golpe.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkExtraMeleeRegistrar
{
    private static readonly Dictionary<string, HediffDef> registered = new();

    static ReworkExtraMeleeRegistrar()
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
                    var attr = type.GetCustomAttribute<ReworkExtraMeleeAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Hediff).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkExtraMelee] '{type.FullName}' tiene [ReworkExtraMelee] pero no hereda de Verse.Hediff.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkExtraMeleeRegistrar: {count} extra-melee(s) declarativo(s) [ReworkExtraMelee] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkExtraMeleeAttribute attr, Type hediffType)
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
                    description = attr.Description ?? "Daños extra de melee registrados por Rework.",
                    hediffClass = hediffType,
                    initialSeverity = 1f,
                };
                DefDatabase<HediffDef>.Add(existing);
                registered[attr.DefName] = existing;
                ReworkContentSelfCheck.RegisterDef(attr.DefName, "HediffDef", true);
                Lg.Info($"[ReworkExtraMelee] Registrado HediffDef '{attr.DefName}' -> {hediffType.Name}.");
            }
            else
            {
                registered[attr.DefName] = existing;
            }

            var damages = ParseDamages(attr);
            ReworkExtraMeleeRegistry.Register(new ReworkExtraMeleeRegistry.ExtraMeleeData
            {
                DefName = attr.DefName,
                HandlerType = hediffType,
                Damages = damages,
                Label = attr.Label ?? attr.DefName,
                Description = attr.Description ?? "Daños extra de melee registrados por Rework.",
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkExtraMelee] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }

    private static List<ReworkExtraDamageEntry> ParseDamages(ReworkExtraMeleeAttribute attr)
    {
        var out_ = new List<ReworkExtraDamageEntry>();
        if (string.IsNullOrWhiteSpace(attr.ExtraDamages))
        {
            Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': ExtraDamages vacío; el hediff no añadirá daños.");
            return out_;
        }

        var parts = attr.ExtraDamages.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var fields = part.Split(':');
            if (fields.Length < 2)
            {
                Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': entrada '{part}' mal formada (formato Def:Cantidad[:Pen[:Chance]]); se ignora.");
                continue;
            }

            string defName = fields[0].Trim();
            var dd = DefDatabase<DamageDef>.GetNamedSilentFail(defName);
            if (dd == null)
            {
                Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': daño '{defName}' no existe; se ignora.");
                continue;
            }

            if (!float.TryParse(fields[1].Trim(), out float amount) || amount <= 0f)
            {
                Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': cantidad '{fields[1]}' inválida en '{part}'; se ignora.");
                continue;
            }

            float pen = 0f, chance = 1f;
            if (fields.Length > 2 && !float.TryParse(fields[2].Trim(), out pen))
            {
                Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': penetración '{fields[2]}' inválida en '{part}'; se usa 0.");
                pen = 0f;
            }
            if (fields.Length > 3 && (!float.TryParse(fields[3].Trim(), out chance) || chance < 0f || chance > 1f))
            {
                Lg.Error($"[ReworkExtraMelee] '{attr.DefName}': chance '{fields[3]}' inválido en '{part}'; se usa 1.");
                chance = 1f;
            }

            out_.Add(new ReworkExtraDamageEntry
            {
                DamageDefName = defName,
                Amount = amount,
                ArmorPenetration = pen,
                Chance = chance,
                DamageDef = dd,
            });
        }
        return out_;
    }
}
