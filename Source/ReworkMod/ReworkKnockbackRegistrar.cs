using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de knockbacks ([ReworkKnockback]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// por cada clase con el atributo crea el HediffDef (si no existe), resuelve
/// sonido/pensamiento/daños y garantiza el ThingDef del objeto volador
/// (ReworkFlyingObject) que el runtime usa para trasladar a la víctima.
///
/// El ThingDef del volador se crea aquí mismo (no necesita atributo aparte):
/// es infraestructura del framework, igual que los defs acompañantes de §54.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkKnockbackRegistrar
{
    private static readonly Dictionary<string, HediffDef> registered = new();

    /// <summary>ThingDef del objeto volador (lo crea el registrador una vez).</summary>
    public static ThingDef? FlyingDef => ReworkKnockbackRuntime.FlyingDef;

    static ReworkKnockbackRegistrar()
    {
        EnsureFlyingDef();
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
                    var attr = type.GetCustomAttribute<ReworkKnockbackAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Hediff).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkKnockback] '{type.FullName}' tiene [ReworkKnockback] pero no hereda de Verse.Hediff.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkKnockbackRegistrar: {count} knockback(s) declarativo(s) [ReworkKnockback] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkKnockbackAttribute attr, Type hediffType)
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
                    description = attr.Description ?? "Empuje de knockback registrado por Rework.",
                    hediffClass = hediffType,
                    initialSeverity = 1f,
                };
                DefDatabase<HediffDef>.Add(existing);
                registered[attr.DefName] = existing;
                ReworkContentSelfCheck.RegisterDef(attr.DefName, "HediffDef", true);
                Lg.Info($"[ReworkKnockback] Registrado HediffDef '{attr.DefName}' -> {hediffType.Name}.");
            }
            else
            {
                registered[attr.DefName] = existing;
            }

            object? sound = ResolveSound(attr);
            object? thought = ResolveThought(attr);
            object? impact = ResolveDamage(attr.KnockImpactDamageType, "Blunt", attr.DefName, "impacto");
            object? explosive = attr.ExplosiveRadius > 0f
                ? ResolveDamage(attr.ExplosiveDamageType, "Flame", attr.DefName, "explosión")
                : null;

            if (attr.KnockbackChance < 0f || attr.KnockbackChance > 1f)
                Lg.Error($"[ReworkKnockback] '{attr.DefName}': KnockbackChance debe estar entre 0 y 1.");
            if (attr.KnockDistanceMin < 0f || attr.KnockDistanceMax < attr.KnockDistanceMin)
                Lg.Error($"[ReworkKnockback] '{attr.DefName}': rango de distancia inválido (min >= 0, max >= min).");

            ReworkKnockbackRegistry.Register(new ReworkKnockbackRegistry.KnockbackData
            {
                DefName = attr.DefName,
                HandlerType = hediffType,
                KnockbackChance = attr.KnockbackChance,
                KnockDistanceMin = attr.KnockDistanceMin,
                KnockDistanceMax = attr.KnockDistanceMax,
                KnockbackSpeed = attr.KnockbackSpeed,
                KnockbackSoundDef = sound,
                KnockbackThoughtDef = thought,
                KnockImpactDamageMin = attr.KnockImpactDamageMin,
                KnockImpactDamageMax = attr.KnockImpactDamageMax,
                KnockImpactDamageDef = impact,
                StunChance = attr.StunChance,
                StunTicks = attr.StunTicks,
                ExplosiveRadius = attr.ExplosiveRadius,
                ExplosiveDamageDef = explosive,
                Label = attr.Label ?? attr.DefName,
                Description = attr.Description ?? "Empuje de knockback registrado por Rework.",
            });
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkKnockback] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }

    private static object? ResolveDamage(string? name, string fallback, string owner, string role)
    {
        string want = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var dd = DefDatabase<DamageDef>.GetNamedSilentFail(want);
        if (dd == null)
            Lg.Error($"[ReworkKnockback] '{owner}': daño de {role} '{want}' no existe; se usará el Knochen por defecto en el golpe.");
        return dd;
    }

    private static object? ResolveSound(ReworkKnockbackAttribute attr)
    {
        if (string.IsNullOrWhiteSpace(attr.KnockbackSound)) return null;
        var sd = DefDatabase<SoundDef>.GetNamedSilentFail(attr.KnockbackSound.Trim());
        if (sd == null)
            Lg.Error($"[ReworkKnockback] '{attr.DefName}': sonido '{attr.KnockbackSound}' no existe; sin sonido.");
        return sd;
    }

    private static object? ResolveThought(ReworkKnockbackAttribute attr)
    {
        if (string.IsNullOrWhiteSpace(attr.KnockbackThought)) return null;
        var td = DefDatabase<ThoughtDef>.GetNamedSilentFail(attr.KnockbackThought.Trim());
        if (td == null)
            Lg.Error($"[ReworkKnockback] '{attr.DefName}': pensamiento '{attr.KnockbackThought}' no existe; sin pensamiento.");
        return td;
    }

    private static void EnsureFlyingDef()
    {
        try
        {
            const string defName = "Rework_FlyingObject";
            var existing = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (existing != null)
            {
                ReworkKnockbackRuntime.FlyingDef = existing;
                return;
            }

            var def = new ThingDef
            {
                defName = defName,
                label = "objeto volador",
                thingClass = typeof(ReworkFlyingObject),
                category = ThingCategory.Item,
                altitudeLayer = AltitudeLayer.Projectile,
                useHitPoints = false,
                selectable = false,
                neverMultiSelect = true,
                drawerType = DrawerType.RealtimeOnly,
            };
            DefDatabase<ThingDef>.Add(def);
            ReworkKnockbackRuntime.FlyingDef = def;
            Lg.Info($"[ReworkKnockback] ThingDef '{defName}' del objeto volador creado.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkKnockback] No se pudo crear el ThingDef del objeto volador: {e.Message}");
        }
    }
}
