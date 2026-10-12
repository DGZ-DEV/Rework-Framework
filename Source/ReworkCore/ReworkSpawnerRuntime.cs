using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Servicio de spawns con retardo ([ReworkDelayedSpawner] + `ReworkSpawner.Queue`).
///
/// Dos colas:
///  - Declarativos: los del registro, con cuenta atrás global; al cumplirse
///    spawnean en una celda válida del mapa actual (cerca del centro, en zona
///    transitable) y reprograman si repiten.
///  - Manuales (`Queue`): con mapa y celda exactos (para incidentes/gizmos);
///    misma cuenta atrás, sin repetición automática (el mod re-encola si quiere).
///
/// Todo corre en el tick global (OnGameComponentTick) con early-out si no hay
/// nada pendiente. Cada spawn se protege por separado: un def roto no rompe
/// el tick ni cancela a los demás (el roto se descarta con error claro).
/// </summary>
public static class ReworkSpawnerRuntime
{
    private class Pending
    {
        public ReworkSpawnSpec Spec = null!;
        public Map? Map;
        public IntVec3 Cell = IntVec3.Invalid;
        public int TicksLeft;
        public int RepeatsDone;
        public bool Declarative;
    }

    private static readonly List<Pending> pending = new();
    private static readonly HashSet<string> declarativeQueued = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>¿Hay spawns pendientes? (para early-out en el tick).</summary>
    public static bool HasActive => pending.Count > 0 || ReworkDelayedSpawnerRegistry.Count > 0;

    /// <summary>
    /// Encola un spawn manual en mapa+celda exactos (desde incidentes, gizmos...).
    /// </summary>
    public static void Queue(Map map, IntVec3 cell, ReworkSpawnSpec spec)
    {
        if (map == null || spec == null || string.IsNullOrEmpty(spec.Id)) return;
        pending.Add(new Pending
        {
            Spec = spec,
            Map = map,
            Cell = cell,
            TicksLeft = Math.Max(1, spec.TicksUntilSpawn),
            Declarative = false,
        });
    }

    public static void OnTick()
    {
        try
        {
            // Declarativos: encolar los que aún no están en cola.
            if (ReworkDelayedSpawnerRegistry.Count > 0)
            {
                foreach (var kvp in ReworkDelayedSpawnerRegistry.All)
                {
                    if (declarativeQueued.Contains(kvp.Key)) continue;
                    declarativeQueued.Add(kvp.Key);
                    pending.Add(new Pending
                    {
                        Spec = kvp.Value,
                        TicksLeft = Math.Max(1, kvp.Value.TicksUntilSpawn),
                        Declarative = true,
                    });
                }
            }

            if (pending.Count == 0) return;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                p.TicksLeft--;
                if (p.TicksLeft > 0) continue;

                bool ok = false;
                try { ok = Fire(p); }
                catch (Exception e)
                {
                    Lg.Error($"[ReworkSpawner] '{p.Spec.Id}' falló: {e.Message}");
                }

                if (p.Declarative && p.Spec.Repeat && (p.Spec.MaxRepeats < 0 || p.RepeatsDone < p.Spec.MaxRepeats))
                {
                    p.RepeatsDone++;
                    p.TicksLeft = Math.Max(1, p.Spec.RepeatEveryTicks);
                }
                else
                {
                    if (p.Declarative)
                        declarativeQueued.Remove(p.Spec.Id);
                    pending.RemoveAt(i);
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSpawner] Tick falló: {e.Message}");
        }
    }

    private static bool Fire(Pending p)
    {
        Map? map = p.Map ?? Find.CurrentMap;
        if (map == null) return false;

        IntVec3 cell = p.Cell.IsValid && p.Cell.InBounds(map) ? p.Cell : FindSpawnCell(map);
        if (!cell.IsValid) return false;

        var spec = p.Spec;
        if (!string.IsNullOrWhiteSpace(spec.PawnKindDefName))
            return SpawnPawns(map, cell, spec);
        if (!string.IsNullOrWhiteSpace(spec.ThingDefName))
            return SpawnThings(map, cell, spec);

        Lg.Error($"[ReworkSpawner] '{spec.Id}': sin PawnKind ni Thing; se descarta.");
        return true; // descartar: no reintentar un spec vacío
    }

    private static IntVec3 FindSpawnCell(Map map)
    {
        try
        {
            var center = map.Center;
            if (center.Walkable(map)) return center;
            foreach (var c in GenAdj.CellsAdjacent8Way(new TargetInfo(center, map)))
            {
                if (c.Walkable(map)) return c;
            }
            return CellFinder.RandomClosewalkCellNear(center, map, 10);
        }
        catch { return IntVec3.Invalid; }
    }

    private static bool SpawnPawns(Map map, IntVec3 cell, ReworkSpawnSpec spec)
    {
        var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(spec.PawnKindDefName);
        if (kind == null)
        {
            Lg.Error($"[ReworkSpawner] '{spec.Id}': PawnKind '{spec.PawnKindDefName}' no existe; se descarta.");
            return true;
        }

        Faction? faction = null;
        if (!string.IsNullOrWhiteSpace(spec.FactionDefName))
        {
            var factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(spec.FactionDefName);
            if (factionDef == null)
            {
                Lg.Error($"[ReworkSpawner] '{spec.Id}': facción '{spec.FactionDefName}' no existe; sin facción.");
            }
            else
            {
                try { faction = Find.FactionManager.FirstFactionOfDef(factionDef); }
                catch { }
            }
        }

        int n = Math.Max(1, spec.PawnCount);
        for (int i = 0; i < n; i++)
        {
            Pawn pawn;
            try { pawn = PawnGenerator.GeneratePawn(kind, faction); }
            catch (Exception e)
            {
                Lg.Error($"[ReworkSpawner] '{spec.Id}': no se pudo generar el pawn: {e.Message}");
                return true;
            }

            IntVec3 at = cell;
            try
            {
                var adj = GenAdj.CellsAdjacent8Way(new TargetInfo(cell, map));
                foreach (var c in adj)
                {
                    if (c.Walkable(map)) { at = c; break; }
                }
            }
            catch { }

            try
            {
                if (!GenPlace.TryPlaceThing(pawn, at, map, ThingPlaceMode.Near))
                {
                    Lg.Error($"[ReworkSpawner] '{spec.Id}': no se pudo colocar el pawn; se descarta.");
                    continue;
                }
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkSpawner] '{spec.Id}': colocación falló: {e.Message}");
                continue;
            }

            ApplyMentalState(pawn, spec);
            ApplyHediffs(pawn, spec);
            Lg.Info($"[ReworkSpawner] '{spec.Id}': {pawn.LabelShort} generado en {at}.");
        }
        return true;
    }

    private static bool SpawnThings(Map map, IntVec3 cell, ReworkSpawnSpec spec)
    {
        var def = DefDatabase<ThingDef>.GetNamedSilentFail(spec.ThingDefName);
        if (def == null)
        {
            Lg.Error($"[ReworkSpawner] '{spec.Id}': Thing '{spec.ThingDefName}' no existe; se descarta.");
            return true;
        }

        try
        {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(Math.Max(1, spec.ThingCount), def.stackLimit);
            if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
            {
                Lg.Error($"[ReworkSpawner] '{spec.Id}': no se pudo colocar '{def.defName}'; se descarta.");
                return true;
            }
            Lg.Info($"[ReworkSpawner] '{spec.Id}': {thing.LabelShort} colocado en {cell}.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSpawner] '{spec.Id}': colocación falló: {e.Message}");
            return true;
        }
    }

    private static void ApplyMentalState(Pawn pawn, ReworkSpawnSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.MentalStateDefName)) return;
        try
        {
            var md = DefDatabase<MentalStateDef>.GetNamedSilentFail(spec.MentalStateDefName);
            if (md == null)
            {
                Lg.Error($"[ReworkSpawner] '{spec.Id}': mental state '{spec.MentalStateDefName}' no existe.");
                return;
            }
            pawn.mindState?.mentalStateHandler?.TryStartMentalState(md);
        }
        catch { }
    }

    private static void ApplyHediffs(Pawn pawn, ReworkSpawnSpec spec)
    {
        if (spec.HediffDefNames == null || spec.HediffDefNames.Count == 0) return;
        foreach (var name in spec.HediffDefNames)
        {
            try
            {
                var hd = DefDatabase<HediffDef>.GetNamedSilentFail(name);
                if (hd == null)
                {
                    Lg.Error($"[ReworkSpawner] '{spec.Id}': hediff '{name}' no existe; se salta.");
                    continue;
                }
                var hediff = HediffMaker.MakeHediff(hd, pawn);
                pawn.health?.AddHediff(hediff);
            }
            catch { }
        }
    }
}
