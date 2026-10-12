using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de spawners ([ReworkDelayedSpawner]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// valida cada atributo (PawnKind o Thing obligatorio, defs existentes) y lo
/// guarda en el registro. El tick global (`ReworkSpawnerRuntime`, enganchado
/// en `RuntimeHooks.OnGameComponentTick`) hace el resto.
///
/// Los defs se validan aquí con error claro; el runtime re-valida barato por
/// si un mod tardío los toca (defensa en profundidad, sin NRE).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkDelayedSpawnerRegistrar
{
    static ReworkDelayedSpawnerRegistrar()
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
                    foreach (var attr in type.GetCustomAttributes<ReworkDelayedSpawnerAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkDelayedSpawnerRegistrar: {count} spawner(s) declarativo(s) [ReworkDelayedSpawner] registrado(s).");
    }

    private static bool Register(ReworkDelayedSpawnerAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.Id))
            {
                Lg.Error($"[ReworkSpawner] '{carrierType.FullName}': Id vacío; se ignora.");
                return false;
            }

            bool hasPawn = !string.IsNullOrWhiteSpace(attr.PawnKind);
            bool hasThing = !string.IsNullOrWhiteSpace(attr.Thing);
            if (!hasPawn && !hasThing)
            {
                Lg.Error($"[ReworkSpawner] '{attr.Id}': sin PawnKind ni Thing; se ignora.");
                return false;
            }

            if (hasPawn && DefDatabase<PawnKindDef>.GetNamedSilentFail(attr.PawnKind.Trim()) == null)
            {
                Lg.Error($"[ReworkSpawner] '{attr.Id}': PawnKind '{attr.PawnKind}' no existe; se ignora.");
                return false;
            }
            if (hasThing && DefDatabase<ThingDef>.GetNamedSilentFail(attr.Thing.Trim()) == null)
            {
                Lg.Error($"[ReworkSpawner] '{attr.Id}': Thing '{attr.Thing}' no existe; se ignora.");
                return false;
            }
            if (!string.IsNullOrWhiteSpace(attr.Faction)
                && DefDatabase<FactionDef>.GetNamedSilentFail(attr.Faction.Trim()) == null)
                Lg.Error($"[ReworkSpawner] '{attr.Id}': facción '{attr.Faction}' no existe; sin facción.");
            if (!string.IsNullOrWhiteSpace(attr.MentalState)
                && DefDatabase<MentalStateDef>.GetNamedSilentFail(attr.MentalState.Trim()) == null)
                Lg.Error($"[ReworkSpawner] '{attr.Id}': mental state '{attr.MentalState}' no existe; sin mental state.");

            var hediffs = new List<string>();
            if (!string.IsNullOrWhiteSpace(attr.Hediffs))
            {
                foreach (var h in attr.Hediffs.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = h.Trim();
                    if (name.Length == 0) continue;
                    if (DefDatabase<HediffDef>.GetNamedSilentFail(name) == null)
                        Lg.Error($"[ReworkSpawner] '{attr.Id}': hediff '{name}' no existe; se salta.");
                    else
                        hediffs.Add(name);
                }
            }

            ReworkDelayedSpawnerRegistry.Register(new ReworkSpawnSpec
            {
                Id = attr.Id,
                PawnKindDefName = string.IsNullOrWhiteSpace(attr.PawnKind) ? null : attr.PawnKind.Trim(),
                FactionDefName = string.IsNullOrWhiteSpace(attr.Faction) ? null : attr.Faction.Trim(),
                PawnCount = Math.Max(1, attr.PawnCount),
                ThingDefName = string.IsNullOrWhiteSpace(attr.Thing) ? null : attr.Thing.Trim(),
                ThingCount = Math.Max(1, attr.ThingCount),
                MentalStateDefName = string.IsNullOrWhiteSpace(attr.MentalState) ? null : attr.MentalState.Trim(),
                HediffDefNames = hediffs,
                TicksUntilSpawn = Math.Max(1, attr.TicksUntilSpawn),
                Repeat = attr.Repeat,
                RepeatEveryTicks = Math.Max(1, attr.RepeatEveryTicks),
                MaxRepeats = attr.MaxRepeats,
            });
            Lg.Info($"[ReworkSpawner] '{attr.Id}' registrado ({(hasPawn ? "pawn " + attr.PawnKind : "thing " + attr.Thing)} en {attr.TicksUntilSpawn} ticks{(attr.Repeat ? ", repite" : "")}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSpawner] Error registrando '{attr.Id}': {e.Message}");
            return false;
        }
    }
}
