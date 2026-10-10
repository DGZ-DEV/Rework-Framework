using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Activadores internos que "enganchan los fantasmas" restantes: conexiones entre
/// APIs declarativas de 0ReworkAPI y events/hooks del juego. Cada bloque activa
/// un sistema que, hasta ahora, solo existía en la API pero nunca se disparaba.
///
/// • ReworkColonySkill   → [ReworkSchedule(EveryDay)] otorga XP + [ReworkInit] suscribe OnLevelUp
/// • ReworkPawnTimeline  → [ReworkHook(PawnDied/BabyBorn)] registra eventos históricos
/// • ReworkRelation      → [ReworkSchedule(EveryDay)] escanea relaciones entre colonos
/// • ReworkStateSnapshot → [ReworkSchedule(EveryDay)] captura estado de colonos
/// • ReworkDynamicMutate → mutación demo reversible, disparada por
///   ReworkRuntimeActivatorsPostDefs ([StaticConstructorOnStartup], post-Defs —
///   §48: [ReworkInit] corre en InitializeMods, ANTES de que existan los Defs)
/// </summary>
public static class ReworkRuntimeActivators
{
    // ---------------------------------------------------------------------------
    // ReworkColonySkill: otorga XP a habilidades de colonia y suscribe OnLevelUp.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Un día de juego: otorga XP a las habilidades de colonia basado en
    /// el número de colonos vivos y el combate reciente.
    /// </summary>
    [ReworkSchedule(EveryDay = true)]
    public static void GrantDailyColonyXp()
    {
        try
        {
            var map = Find.CurrentMap;
            if (map?.mapPawns == null) return;

            int colonos = map.mapPawns.FreeColonistsCount;
            if (colonos > 0)
            {
                // Supervivencia: XP base por cada colono vivo al finalizar el día.
                ReworkColonySkill.AddXp("Supervivencia", colonos * 10f);

                // Combate: bonus si hay enemigos hostiles en el mapa.
                int hostiles = map.mapPawns.AllPawns.Count(p =>
                    p != null && p.HostileTo(Faction.OfPlayer) && p is not null);
                if (hostiles > 0)
                    ReworkColonySkill.AddXp("Combate", hostiles * 5f);
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkColonySkill] GrantDailyColonyXp falló: {e.Message}");
        }
    }

    /// <summary>
    /// En el arranque, suscribe OnLevelUp a un handler que muestra un mensaje al jugador.
    /// </summary>
    [ReworkInit(Priority = 10)]
    public static void SubscribeColonySkillLevelUp()
    {
        try
        {
            ReworkColonySkill.OnLevelUp += (skillName, newLevel) =>
            {
                try
                {
                    Messages.Message($"¡Tu colonia ha subido {skillName} a nivel {newLevel}!",
                        MessageTypeDefOf.PositiveEvent, false);
                    Lg.Info($"[ReworkColonySkill] '{skillName}' subió a nivel {newLevel}.");
                }
                catch { }
            };
            Lg.Info("[ReworkColonySkill] Suscripción a OnLevelUp conectada.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkColonySkill] No se pudo suscribir a OnLevelUp: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // ReworkPawnTimeline: registra eventos históricos de pawns.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Registra en la línea de tiempo del pawn cuando muere.
    /// </summary>
    [ReworkHook(ReworkHookPoint.PawnDied)]
    public static void OnPawnDiedTimeline(Pawn pawn)
    {
        try
        {
            if (pawn?.Name == null) return;
            ReworkPawnTimeline.RecordEvent(
                pawn.GetUniqueLoadID(),
                GenTicks.TicksAbs,
                "fallecimiento",
                $"Murió {pawn.LabelShort} en el mapa.");
        }
        catch { }
    }

    /// <summary>
    /// Registra en la línea de tiempo de la madre cuando nace un bebé.
    /// </summary>
    [ReworkHook(ReworkHookPoint.BabyBorn)]
    public static void OnBabyBornTimeline(Pawn mother)
    {
        try
        {
            if (mother?.Name == null) return;
            ReworkPawnTimeline.RecordEvent(
                mother.GetUniqueLoadID(),
                GenTicks.TicksAbs,
                "nacimiento",
                $"Dio a luz a un bebé en el mapa.");
        }
        catch { }
    }

    // ---------------------------------------------------------------------------
    // ReworkRelation: escanea relaciones entre colonos diariamente.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Cada día, escanea las relaciones entre pares de colonos y registra
    /// las relaciones detectadas en ReworkRelation.
    /// </summary>
    [ReworkSchedule(EveryDay = true)]
    public static void ScanPawnRelations()
    {
        try
        {
            var map = Find.CurrentMap;
            var colonos = map?.mapPawns?.FreeColonists;
            if (colonos == null || colonos.Count == 0) return;

            for (int i = 0; i < colonos.Count; i++)
            {
                var pawnA = colonos[i];
                if (pawnA == null || pawnA.Name == null) continue;

                for (int j = 0; j < colonos.Count; j++)
                {
                    if (i == j) continue;
                    var pawnB = colonos[j];
                    if (pawnB == null || pawnB.Name == null) continue;

                    if (pawnA.relations == null) continue;
                    int opinion = pawnA.relations.OpinionOf(pawnB);
                    string kind = opinion > 10 ? "amistad"
                              : opinion < -10 ? "antiguedad"
                              : "neutral";

                    ReworkRelation.SetRelation(
                        pawnA.GetUniqueLoadID(),
                        pawnB.GetUniqueLoadID(),
                        kind);
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkRelation] ScanPawnRelations falló: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // ReworkStateSnapshot: captura estado de colonos diariamente.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Captura un snapshot del estado de los colonos (nombre, salud, necesidad
    /// de alimento) cada día para el sistema de reversión temporal.
    /// </summary>
    [ReworkSchedule(EveryDay = true)]
    public static void DailySnapshot()
    {
        try
        {
            var map = Find.CurrentMap;
            var colonos = map?.mapPawns?.FreeColonists;
            if (colonos == null || colonos.Count == 0) return;

            var entries = new List<(string, string, object?)>();

            for (int i = 0; i < colonos.Count; i++)
            {
                var pawn = colonos[i];
                if (pawn == null || pawn.Name == null) continue;
                string pid = pawn.GetUniqueLoadID();

                entries.Add((pid, "name", pawn.LabelShort));
                entries.Add((pid, "health", pawn.health?.hediffSet?.hediffs?.Count > 0));

                if (pawn.needs != null)
                {
                    var food = pawn.needs.TryGetNeed(NeedDefOf.Food);
                    entries.Add((pid, "food", food?.CurLevel ?? 0f));
                }
            }

            if (entries.Count > 0)
                ReworkStateSnapshot.Capture(entries);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkStateSnapshot] DailySnapshot falló: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // ReworkDynamicMutate: aplica una mutación demo reversible en el arranque.
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Aplica una mutación demo: CraftingSpot deja de tener hit points.
    /// Demuestra que ApplyMutation funciona y que RevertAll (en hot-reload) lo revierte.
    ///
    /// §48: NO lleva [ReworkInit] — InitRunner corre durante InitializeMods, ANTES
    /// de que PlayDataLoader cargue los Defs (DefDatabase vacío → la mutación era
    /// un no-op silencioso desde siempre). Ahora la dispara
    /// ReworkRuntimeActivatorsPostDefs, un [StaticConstructorOnStartup] que corre
    /// justo cuando los Defs ya existen.
    /// </summary>
    public static void ApplyDemoMutation()
    {
        try
        {
            var cs = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            if (cs != null)
            {
                bool ok = ReworkDynamicMutate.ApplyMutation(
                    cs, "useHitPoints", false, "demo:craftingspot-no-hitpoints");
                if (ok)
                {
                    Lg.Info("[ReworkDynamicMutate] Mutación demo aplicada: CraftingSpot.useHitPoints=false.");
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDynamicMutate] ApplyDemoMutation falló: {e.Message}");
        }
    }
}

/// <summary>
/// §48 — Activadores que NECESITAN los Defs cargados. [StaticConstructorOnStartup]
/// corre tras PlayDataLoader (a diferencia de [ReworkInit], que corre en la fase de
/// InitializeMods sin Defs). El orden entre clases [StaticConstructorOnStartup] no
/// está garantizado: este activador no depende de los registros del escáner.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkRuntimeActivatorsPostDefs
{
    static ReworkRuntimeActivatorsPostDefs()
    {
        ReworkRuntimeActivators.ApplyDemoMutation();
    }
}
