using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Runtimes de juego para las features declarativas de Rework que necesitan tipos
/// del juego (ReworkAPI no puede referenciarlos). Cada clase aquí es consumida por
/// los puntos de dispatch inyectados de RuntimeHooks (ver GameProcessing):
///
///   ReworkCustomAlert  → instancia real de RimWorld.Alert por entrada [ReworkAlert]
///   ReworkRuntimeTab   → instancia real de Verse.InspectTabBase por entrada [ReworkTab]
///   GizmoRuntime       → crea Command_Action para entradas [ReworkGizmo]
///   StatusEffectRuntime→ administra hediffs de [ReworkStatusEffect] con expiración
///   ZoneRuntime        → dispara [ReworkZoneEffect]-API sobre zonas vanilla por tick
///
/// Nota: el lore [ReworkLore] vive en RuntimeHooks.OnHistoryEvent (inyectado en
/// HistoryEventsManager.RecordEvent); no hay clase "LoreRuntime".
/// </summary>
public static class ReworkRuntime
{
    // ---------------------------------------------------------------------------
    // GIZMOS [ReworkGizmo]
    // ---------------------------------------------------------------------------
    public static class GizmoRuntime
    {
        /// <summary>Crea los Command_Action declarados para un target (por nombre de tipo y bases).</summary>
        public static List<Gizmo>? CreateFor(object target)
        {
            if (target == null) return null;

            var type = target.GetType();
            var names = new List<string> { type.Name };
            for (var b = type.BaseType; b != null && b != typeof(object); b = b.BaseType)
            {
                names.Add(b.Name);
                if (b == typeof(Pawn) || b == typeof(Thing) || b == typeof(Building))
                    break;
            }

            var result = new List<Gizmo>();
            foreach (var name in names)
            {
                foreach (var entry in ReworkGizmoRegistry.GetGizmosForType(name))
                {
                    if (entry == null || entry.Method == null) continue;
                    var g = BuildCommand(entry, target);
                    if (g != null) result.Add(g);
                }
            }
            return result;
        }

        private static Command? BuildCommand(ReworkGizmoRegistry.GizmoEntry entry, object target)
        {
            try
            {
                var cmd = new Command_Action
                {
                    defaultLabel = string.IsNullOrEmpty(entry.Label) ? "Rework" : entry.Label,
                    defaultDesc = entry.Description
                };

                if (!string.IsNullOrEmpty(entry.IconPath))
                {
                    try { cmd.icon = ContentFinder<Texture2D>.Get(entry.IconPath); }
                    catch { cmd.icon = null; }
                }

                cmd.action = () =>
                {
                    try
                    {
                        var pars = entry.Method.GetParameters();
                        if (pars.Length == 0)
                            entry.Method.Invoke(null, null);
                        else if (pars.Length == 1)
                            entry.Method.Invoke(null, new[] { target });
                    }
                    catch (Exception e)
                    {
                        Lg.Error($"[ReworkGizmo] '{entry.Method.DeclaringType?.Name}.{entry.Method.Name}' lanzó: {e.Message}");
                    }
                };
                return cmd;
            }
            catch
            {
                return null;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // ALERTAS [ReworkAlert]
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Alerta concreta que evalúa su condición MethodInfo para activarse.
    ///
    /// OJO (ERRORES.md §39): RimWorld.AlertsReadout..ctor() sondea por reflexión
    /// TODAS las subclases no abstractas de RimWorld.Alert y las instancia con
    /// Activator.CreateInstance, que exige un constructor PÚBLICO SIN PARÁMETROS.
    /// Si falta, el ctor del AlertsReadout lanza MissingMethodException, UIRoot_Play
    /// no se construye, Find.MapUI queda null y generar el mapa revienta con NRE
    /// (el juego se congela al iniciar partida). Por eso existe el ctor vacío:
    /// crea una instancia inerte que vanilla puede construir sin romper nada.
    /// </summary>
    public sealed class ReworkCustomAlert : RimWorld.Alert
    {
        private readonly ReworkAlertRegistry.AlertEntry? entry;

        /// <summary>Instancia inerte exigida por el sondeo por reflexión de vanilla.</summary>
        public ReworkCustomAlert()
        {
            this.entry = null;
        }

        public ReworkCustomAlert(ReworkAlertRegistry.AlertEntry entry)
        {
            this.entry = entry;
            this.defaultLabel = entry.Label;
            this.defaultExplanation = entry.Explanation;
        }

        /// <summary>¿Es la instancia vacía creada por vanilla (sin entrada declarativa)?</summary>
        public bool IsInert => entry == null;

        public override string GetLabel()
        {
            if (entry == null) return "";
            return string.IsNullOrEmpty(entry.Label) ? (base.defaultLabel ?? "Rework") : entry.Label;
        }

        public override TaggedString GetExplanation()
        {
            if (entry == null) return new TaggedString("");
            var expl = string.IsNullOrEmpty(entry.Explanation) ? base.defaultExplanation : entry.Explanation;
            return new TaggedString(expl ?? "");
        }

        public override AlertReport GetReport()
        {
            if (entry == null) return AlertReport.Inactive;
            try
            {
                if (entry.ConditionMethod == null) return AlertReport.Inactive;
                var pars = entry.ConditionMethod.GetParameters();
                object? result = pars.Length == 0
                    ? entry.ConditionMethod.Invoke(null, null)
                    : entry.ConditionMethod.Invoke(null, new object?[] { Current.Game });
                return result is bool b && b ? AlertReport.Active : AlertReport.Inactive;
            }
            catch
            {
                return AlertReport.Inactive;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // PESTAÑAS [ReworkTab]
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Pestaña de inspección concreta que delega su dibujo en la entrada registrada.
    ///
    /// Misma precaución que ReworkCustomAlert (ERRORES.md §39): vanilla sondea las
    /// subclases no abstractas de InspectTabBase y las instancia con
    /// Activator.CreateInstance, así que hace falta un ctor público sin parámetros.
    /// El resultado es una pestaña inerte (StillValid=false) que nunca se muestra.
    /// </summary>
    public sealed class ReworkRuntimeTab : InspectTabBase
    {
        private readonly ReworkTabRegistry.TabEntry? entry;

        /// <summary>Instancia inerte exigida por el sondeo por reflexión de vanilla.</summary>
        public ReworkRuntimeTab()
        {
            this.entry = null;
            this.labelKey = "";
        }

        public ReworkRuntimeTab(ReworkTabRegistry.TabEntry entry)
        {
            this.entry = entry;
            this.labelKey = entry.Label;
        }

        public override float PaneTopY => 0f;

        public override bool StillValid => entry != null;

        public override void CloseTab() { }

        public override void FillTab()
        {
            if (entry == null) return;
            try
            {
                object? sel = Find.Selector?.SingleSelectedThing;
                entry.DrawAction?.Invoke(TabRect, sel ?? Current.Game);
            }
            catch (Exception e)
            {
                Log.Error($"[ReworkTab] '{entry.Id}' falló al dibujar: {e.Message}");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // EFECTOS DE ESTADO [ReworkStatusEffect]
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Administra hediffs aplicados desde la API (o autoaplicados a colonos nuevos)
    /// con expiración por duración: aplica ahora y retira al cumplirse DurationTicks.
    /// </summary>
    public static class StatusEffectRuntime
    {
        private class Application
        {
            public Pawn Pawn = null!;
            public string DefName = "";
            public int EndTick;
        }

        private static readonly List<Application> apps = new();

        /// <summary>Aplica el efecto de estado (registrado con [ReworkStatusEffect]) a un pawn.</summary>
        public static bool Apply(Pawn pawn, string defName)
        {
            try
            {
                if (pawn == null || pawn.health == null) return false;
                var data = ReworkStatusEffectRegistry.Get(defName);
                if (data == null) return false;

                var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    // §48: antes este fallo era SILENCIOSO — un registro manual en
                    // ReworkStatusEffectRegistry (sin atributo, que es quien crea
                    // el HediffDef) hacía que Apply no hiciera NADA sin explicación.
                    Lg.Error($"[ReworkStatusEffect] No existe HediffDef '{defName}' (el registro " +
                             "por atributo lo crea; un registro manual necesita un HediffDef con ese defName " +
                             "definido por XML o [ReworkHediff]). Efecto NO aplicado.");
                    return false;
                }

                var existing = pawn.health.hediffSet.hediffs.FirstOrDefault(h =>
                    h.def != null && h.def.defName == defName);
                if (existing != null)
                    return false; // no duplicar

                pawn.health.AddHediff(HediffMaker.MakeHediff(def, pawn));
                if (data.DurationTicks > 0)
                {
                    apps.RemoveAll(a => a.Pawn == pawn && a.DefName == defName);
                    apps.Add(new Application
                    {
                        Pawn = pawn,
                        DefName = defName,
                        EndTick = (Find.TickManager?.TicksGame ?? 0) + data.DurationTicks
                    });
                }
                Lg.Info($"[ReworkStatusEffect] '{defName}' aplicado a {pawn.LabelShort} ({(data.DurationTicks > 0 ? data.DurationTicks + " ticks" : "permanente")}).");
                return true;
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkStatusEffect] Error aplicando '{defName}': {e.Message}");
                return false;
            }
        }

        /// <summary>Aplica todos los efectos con AutoApply=true a un pawn (llamado en PawnCreated).</summary>
        public static void TryApplyNewbornAuto(Pawn pawn)
        {
            if (pawn == null || pawn.Faction == null || !pawn.Faction.IsPlayer) return;
            foreach (var data in ReworkStatusEffectRegistry.AllAutoApply)
            {
                Apply(pawn, data.DefName);
            }
        }

        /// <summary>¿Hay efectos de estado activos pendientes de expiración?</summary>
        public static bool HasActive => apps.Count > 0;

        /// <summary>Expira los efectos cumplidos (desde el tick dispatcher).</summary>
        public static void OnTick()
        {
            if (apps.Count == 0) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = apps.Count - 1; i >= 0; i--)
            {
                var a = apps[i];
                if (a.Pawn == null || a.Pawn.Destroyed || now >= a.EndTick)
                {
                    try
                    {
                        if (a.Pawn?.health != null && a.Pawn.health.hediffSet != null)
                        {
                            var h = a.Pawn.health.hediffSet.hediffs.FirstOrDefault(hh => hh.def != null && hh.def.defName == a.DefName);
                            if (h != null)
                            {
                                a.Pawn.health.RemoveHediff(h);
                                Lg.Verbose($"[ReworkStatusEffect] '{a.DefName}' expirado en {a.Pawn.LabelShort}.");
                            }
                        }
                    }
                    catch { }
                    apps.RemoveAt(i);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------
    // EFECTOS DE ZONA (API ReworkZoneManager + zonas vanilla)
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Conecta las zonas registradas con ReworkZoneManager.RegisterZone a las zonas
    /// reales del juego: por cadena de intervalo, busca pawns dentro de la zona y
    /// dispara OnPawnInside. Coincidencia de zona por label o por nombre de clase.
    /// </summary>
    public static class ZoneRuntime
    {
        private static readonly Dictionary<string, int> lastTick = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>¿Hay zonas de efecto registradas? (para early-out en OnGameComponentTick)</summary>
        public static bool HasZones => ReworkZoneManager.RegisteredCount > 0;

        public static void OnTick()
        {
            try
            {
                if (Find.Maps == null || Find.Maps.Count == 0) return;
                int now = Find.TickManager?.TicksGame ?? 0;

                foreach (var map in Find.Maps)
                {
                    var zones = map.zoneManager?.AllZones;
                    if (zones == null) continue;

                    foreach (var zone in zones)
                    {
                        foreach (var def in ReworkZoneManager.RegisteredZones)
                        {
                            if (!MatchesZone(zone, def.Key)) continue;
                            if (lastTick.TryGetValue(def.Key, out int lt) && now - lt < def.Value.IntervalTicks)
                                continue;
                            lastTick[def.Key] = now;

                            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                            {
                                if (zone.Cells.Contains(pawn.Position))
                                {
                                    ReworkZoneManager.TriggerEffect(def.Key, pawn);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkZoneEffect] Tick falló: {e.Message}");
            }
        }

        private static bool MatchesZone(Zone zone, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (string.Equals(zone.label, id, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(zone.GetType().Name, id, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------------------
    // QUESTS [ReworkQuest]
    // ---------------------------------------------------------------------------
    /// <summary>
    /// Verifica periódicamente las misiones registradas con [ReworkQuest]: cada
    /// 60.000 ticks instancia la clase, y si CanTrigger() devuelve true invoca
    /// OnAccepted() (la misión en sí queda a cargo del mod; el framework la
    /// dispara y registra en log).
    /// </summary>
    public static class QuestRuntime
    {
        private static readonly Dictionary<string, int> lastCheck = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>¿Hay quests registrados? (para early-out en OnGameComponentTick)</summary>
        public static bool HasQuests => ReworkQuestRegistry.Count > 0;

        public static void OnTick()
        {
            try
            {
                if (Find.TickManager == null) return;
                int now = Find.TickManager.TicksGame;
                foreach (var kvp in ReworkQuestRegistry.AllQuests)
                {
                    if (!lastCheck.TryGetValue(kvp.Key, out int lc) || now - lc >= 60000)
                    {
                        lastCheck[kvp.Key] = now;
                        TryTrigger(kvp.Key, kvp.Value);
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryTrigger(string defName, Type questType)
        {
            try
            {
                if (questType == null || questType.IsAbstract) return;
                var inst = (ReworkQuestBase)Activator.CreateInstance(questType);
                if (inst != null && inst.CanTrigger())
                {
                    Lg.Info($"[ReworkQuest] Misión '{defName}' disparada (CanTrigger=true); ejecutando OnAccepted.");
                    inst.OnAccepted();
                }
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkQuest] '{defName}' falló al disparar: {e.Message}");
            }
        }
    }
}