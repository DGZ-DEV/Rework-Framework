using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rework;
using Rework.Data;
using Rework.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Hooks en tiempo de ejecución que REWORK inyecta en el Assembly-CSharp NUEVO
/// (vía Cecil, desde GameProcessing) para que el código del juego, en la pasada 2,
/// ignore los ensamblados ORIGINALES que quedaron marcados como reflection-only.
///
/// GenTypes.AllActiveAssemblies es el único punto por el que el juego enumera tipos
/// (AllTypes, GetTypeInAnyAssembly, AllSubclasses, ...). Si ahí apareciera un original
/// refonly, cualquier Activator.CreateInstance sobre sus tipos lanzaría
/// "It is illegal to invoke a method on a type loaded using the ReflectionOnly api".
/// El filtro garantiza que solo se enumeren copias válidas (las nuevas).
/// </summary>
public static class RuntimeHooks
{
    /// <summary>
    /// Envuelve la enumeración de ensamblados activos de GenTypes saltando los
    /// originales refonly. Se inyecta en el getter de AllActiveAssemblies:
    /// el getter crea el iterador (newobj ...; ret) y esta llamada se inserta antes
    /// del ret para filtrar su resultado.
    /// </summary>
    public static IEnumerable<Assembly> WrapActiveAssemblies(IEnumerable<Assembly> source)
    {
        foreach (var asm in source)
        {
            if (DataStore.RefOnlyOriginals.Contains(asm))
                continue;
            yield return asm;
        }
    }

    /// <summary>
    /// Silencia SOLO el ruido conocido del reinicio interno (equivalente al
    /// SilenceLogging de Prepatcher): la ThreadAbortException de la pasada 1 y los NREs
    /// del frame de transición, ÚNICAMENTE mientras DataStore.suppressLogs es true (la
    /// ventana entre la pasada 1 y el final de la pasada 2). Fuera de esa ventana nunca
    /// suprime, de modo que no se esconden errores reales. Se inyecta como prefijo en
    /// Verse.Log.Error(string).
    /// </summary>
    public static bool ShouldSuppressLog(string msg) => DataStore.ShouldSuppressLog(msg);

    // §48 (auditoría anti-fantasma): los antiguos despachadores
    // SweepOriginalComponents / EnsureWorldCameraDriver / LogWorldCameraDiagnostic
    // fueron ELIMINADOS. Sus inyectadores (PatchWorldInterfaceResetRuntimeSweep,
    // PatchWorldCameraManagerCreateWorldCameraSweep y
    // PatchWorldCameraManagerGetDriverSelfHeal) quedaron huérfanos al ser superados
    // por la solución definitiva de la cámara (subclase ReworkWorldCameraDriver,
    // paso 7 del pipeline): los despachadores estaban documentados como "se inyecta
    // en puntos estratégicos" cuando NADA los inyectaba — patrón §44 inverso
    // (inyector huérfano ⇒ despachador inactivo). Historial: ERRORES.md §48.


    /// <summary>
    /// Intercepta la culminación de SafeSaver.Save:
    /// 1. Genera SIEMPRE el archivo binario ultrarrápido (.rwbin) con el centinela #REFORJED.
    /// 2. Empaqueta los almacenes de servicio de Rework en el compañero .rwdat
    ///    (ReworkPersistence.SaveAll).
    /// (El respaldo XML de tiempo lo gestiona GameComponent_ReworkXmlBackup.)
    /// </summary>
    public static void OnSafeSaveCompleted(string savePath)
    {
        try
        {
            if (string.IsNullOrEmpty(savePath) || !System.IO.File.Exists(savePath)) return;

            string binPath = System.IO.Path.ChangeExtension(savePath, ReworkBinaryScribe.BinaryExtension);
            bool ok = ReworkBinaryScribe.WriteBinaryFromXml(savePath, binPath);

            if (ok)
            {
                Lg.Info($"[ReworkBinaryScribe] Save binario generado (.rwbin) con centinela #REFORJED: '{System.IO.Path.GetFileName(binPath)}'.");
            }

            // Integración de persistencia: los almacenes de servicios (ReworkColonySkill,
            // ReworkPawnTimeline, ReworkWorldStore, ...) se empaquetan en el compañero
            // .rwdat junto al save (ReworkPersistence.SaveAll).
            ReworkPersistence.SaveAll(savePath);
        }
        catch (System.Exception e)
        {
            Lg.Error($"[ReworkBinaryScribe] Error empaquetando save binario: {e}");
        }
    }

    /// <summary>
    /// Intenta cargar el XmlDocument directamente desde el binario (.rwbin) con centinela #REFORJED.
    /// Si existe y es íntegro, lo descomprime y retorna su DocumentElement.
    /// Si no existe o está corrupto, retorna null para que RimWorld cargue el XML (.rws) vanilla como fallback.
    ///
    /// §48 (anti-fantasma): en AMBAS rutas se restaura el compañero .rwdat
    /// (ReworkPersistence.LoadAll) — antes NADIE lo llamaba: los 5 almacenes de
    /// servicio (ColonySkill, PawnTimeline, WorldStore, StateSnapshot, Relation)
    /// se GUARDABAN en cada partida y se PERDÍAN en cada carga (y las migraciones
    /// re-ejecutaban porque su versión vive en el WorldStore no restaurado).
    /// </summary>
    public static System.Xml.XmlElement? TryLoadBinaryDocument(string filePath)
    {
        // Restaurar SIEMPRE los almacenes de servicio del save que se está abriendo
        // (si no hay .rwdat, LoadAll no hace nada). Corre ANTES de que
        // "GameInitialized" dispare las migraciones, que así ven la versión
        // restaurada y no re-ejecutan.
        ReworkPersistence.LoadAll(filePath);

        try
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string binPath = System.IO.Path.ChangeExtension(filePath, ReworkBinaryScribe.BinaryExtension);
            if (!System.IO.File.Exists(binPath)) return null;

            if (!ReworkBinaryScribe.IsValidBinarySave(binPath))
            {
                Lg.Error($"[ReworkBinaryScribe] Binario corrupto o incompleto sin centinela #REFORJED: '{System.IO.Path.GetFileName(binPath)}'. Fallback seguro al XML (.rws).");
                return null;
            }

            var doc = ReworkBinaryScribe.LoadXmlFromBinary(binPath);
            if (doc != null && doc.DocumentElement != null)
            {
                Lg.Info($"[ReworkBinaryScribe] Partida cargada exitosamente desde BINARIO (.rwbin): '{System.IO.Path.GetFileName(binPath)}'.");
                return doc.DocumentElement;
            }
        }
        catch (System.Exception e)
        {
            Lg.Error($"[ReworkBinaryScribe] Excepción al cargar binario, activando fallback a XML: {e.Message}");
        }
        return null;
    }

    // ============================================================================
    // PUNTOS DE DISPATCH INYECTADOS POR GameProcessing (Fase "enganchar los
    // fantasmas"): cada método público estático de aquí es el destino de un `call`
    // que el pipeline inserta en un método del Assembly-CSharp NUEVO. Versionan la
    // integración de las features declarativas (ReworkScheduler, ReworkBus,
    // ReworkWatch, zonas, gizmos, inspección, alertas, IA, lore, overlays, ...).
    // ============================================================================

    private static int lastTick = -1;

    /// <summary>Delegado que ReworkMod conecta al auto-detector de [ReworkWatch] (tick por tick).</summary>
    public static System.Action? AutoWatchTick;

    /// <summary>
    /// Despachador de tick global. Inyectado al INICIO de
    /// Verse.GameComponentUtility.GameComponentTick() (se llama UNA vez por tick
    /// de juego). Conecta a vida real:
    ///   • ReworkScheduler     → [ReworkSchedule]
    ///   • ReworkBus           → evento ReworkGameTickEvent por tick
    ///   • ReworkParallel      → FlushMainThreadQueue (callbacks en hilo principal)
    ///   • ReworkMainThread    → Drain (acciones encoladas por mods via RunOnMain)
    ///   • ReworkWatch         → auto-detección de cambios en campos observados
    ///   • StatusEffectRuntime → expiración de [ReworkStatusEffect]
    ///   • ZoneRuntime         → efectos de zona periódicos
    /// El trabajo global se ejecuta una sola vez por tick (guard lastTick).
    ///
    /// §53-audit-hooks: early-out si ningún subsistema registró trabajo para el tick.
    /// Evita entrar en try/catch, alocar ReworkGameTickEvent y llamar a subsistemas
    /// vacíos (cada uno ~60 veces/segundo). El try/catch se eliminó del dispatcher:
    /// cada subsistema ya maneja sus propios errores internamente.
    /// </summary>
    public static void OnGameComponentTick()
    {
        // §53-audit-hooks — early-out: si ningún subsistema tiene trabajo, retornar
        // inmediatamente sin entrar en try/catch ni alocar el evento de tick.
        if (ReworkScheduler.Count == 0
            && !ReworkBus.HasSubscribers<ReworkGameTickEvent>()
            && !ReworkRuntime.QuestRuntime.HasQuests
            && !ReworkRuntime.StatusEffectRuntime.HasActive
            && !ReworkRuntime.ZoneRuntime.HasZones)
        {
            // AutoWatch y parallel son baratos cuando están vacíos (el primero
            // tiene su propio early-out en watchers.Count == 0; el segundo hace
            // TryDequeue que falla inmediatamente si la cola está vacía).
            AutoWatchTick?.Invoke();
            ReworkParallel.FlushMainThreadQueue();
            ReworkMainThread.Drain();
            return;
        }

        var tick = Find.TickManager?.TicksGame ?? 0;

        if (tick != lastTick)
        {
            lastTick = tick;
            ReworkScheduler.OnGameTick(tick);
            // Solo alocar el evento si hay suscriptores (evita GC cada tick)
            if (ReworkBus.HasSubscribers<ReworkGameTickEvent>())
                ReworkBus.Publish(new ReworkGameTickEvent(tick));
            ReworkParallel.FlushMainThreadQueue();
            ReworkMainThread.Drain();
            AutoWatchTick?.Invoke();
            ReworkRuntime.QuestRuntime.OnTick();
        }

        ReworkRuntime.StatusEffectRuntime.OnTick();
        ReworkRuntime.ZoneRuntime.OnTick();
    }

    /// <summary>
    /// Añade los gizmos [ReworkGizmo] del target al enumerable de gizmos vanilla.
    /// Inyectado antes del ret de Verse.Thing.GetGizmos() y Verse.Pawn.GetGizmos()
    /// (ambos son iteradores: devuelven el estado d__NN; aquí se envuelve con Concat).
    ///
    /// §53-audit-hooks: early-out si no hay gizmos registrados (evita crear listas
    /// y caminar la jerarquía de tipos en cada selección).
    /// </summary>
    public static IEnumerable<Verse.Gizmo> AppendReworkGizmos(IEnumerable<Verse.Gizmo> gizmos, object target)
    {
        if (gizmos == null) return System.Linq.Enumerable.Empty<Verse.Gizmo>();
        if (ReworkGizmoRegistry.Count == 0) return gizmos;
        var extra = ReworkRuntime.GizmoRuntime.CreateFor(target);
        if (extra == null || extra.Count == 0) return gizmos;
        return System.Linq.Enumerable.Concat(gizmos, extra);
    }

    /// <summary>
    /// Añade las líneas [ReworkInspectString] a la cadena de inspección vanilla.
    /// Inyectado antes de cada ret de Verse.Thing.GetInspectString() y
    /// Verse.Pawn.GetInspectString().
    ///
    /// §53-audit-hooks: early-out si no hay manejadores registrados; el try/catch
    /// se eliminó del dispatcher porque GetAppendedString ya maneja errores por
    /// manejador individual.
    /// </summary>
    public static string AppendReworkInspectString(string current, object target)
    {
        if (ReworkInspectStringRegistry.HandlerCount == 0) return current;
        string extra = ReworkInspectStringRegistry.GetAppendedString(target);
        if (string.IsNullOrEmpty(extra)) return current;
        if (string.IsNullOrEmpty(current)) return extra;
        return current + "\n" + extra;
    }

    /// <summary>
    /// Registra las alertas [ReworkAlert] declarativas en el AlertsReadout real.
    /// Inyectado antes del ret final de RimWorld.AlertsReadout.ctor() — justo después
    /// de que vanilla construya AllAlerts desde allAlertTypesCached; añadimos las
    /// nuestras.
    ///
    /// §48: idempotencia POR INSTANCIA de readout (antes era un flag global
    /// `reworkAlertsAttached` que jamás se reseteaba: al salir al menú y entrar
    /// en una SEGUNDA partida, el nuevo AlertsReadout se quedaba sin alertas).
    /// Cada readout nuevo se sondea contra AllAlerts del propio readout.
    /// </summary>
    public static void AttachReworkAlerts(RimWorld.AlertsReadout readout)
    {
        if (readout == null) return;
        try
        {
            var all = ReworkAlertRegistry.AllAlerts;
            if (all == null || all.Count == 0) return;

            // Vanilla sondea por reflexión TODAS las subclases de Alert y deja una
            // instancia inerte de ReworkCustomAlert en AllAlerts (ERRORES.md §39).
            // La quitamos antes de añadir las instancias reales con su entrada.
            readout.AllAlerts.RemoveAll(a => a is ReworkRuntime.ReworkCustomAlert rca && rca.IsInert);

            // §48: solo una vez POR READOUT (la sonda del ctor corre una vez por
            // instancia; esto también cubre un hipotético re-attach).
            if (readout.AllAlerts.Any(a => a is ReworkRuntime.ReworkCustomAlert rca && !rca.IsInert))
                return;

            foreach (var entry in all)
            {
                readout.AllAlerts.Add(new ReworkRuntime.ReworkCustomAlert(entry));
            }
            Lg.Info($"[ReworkAlert] {all.Count} alerta(s) declarativa(s) conectada(s) al AlertsReadout.");
        }
        catch (System.Exception e)
        {
            Lg.Error($"[ReworkAlert] No se pudieron conectar las alertas: {e}");
        }
    }

    /// <summary>
    /// Aplica los modificadores de IA [ReworkAIModifier] a la prioridad calculada de
    /// un ThinkNode. Inyectado antes del ret de Verse.AI.ThinkNode.GetPriority(Pawn):
    /// si el ret devuelve la prioridad normal (prev = ldfld priority), se ajusta; la
    /// rama de error (0) no se toca.
    ///
    /// §53-audit-hooks: early-out si no hay modificadores registrados; el try/catch
    /// se eliminó del dispatcher porque EvaluateJobPriority ya maneja errores por
    /// modificador individual.
    /// </summary>
    public static float AdjustThinkPriority(float current, Verse.Pawn pawn, string jobDefName)
    {
        if (ReworkAIRegistry.ModifierCount == 0) return current;
        return ReworkAIRegistry.EvaluateJobPriority(pawn, jobDefName, current);
    }

    /// <summary>Publica un evento de ciclo de vida en el ReworkBus ([ReworkOn]).</summary>
    public static void PublishLifecycleEvent(string name)
    {
        PublishLifecycleEventContext(name, null);
    }

    /// <summary>Publica un evento de ciclo de vida con contexto en el ReworkBus.</summary>
    public static void PublishLifecycleEventContext(string name, object? context)
    {
        try
        {
            ReworkBus.Publish(new ReworkLifecycleEvent(name, context));
            // §48: ClearAll SOLO en partida nueva. Antes también corría en
            // "GameInitialized", que se dispara al FINAL de CARGAR una partida:
            // con LoadAll restaurando los almacenes al inicio de la carga
            // (TryLoadBinaryDocument), el ClearAll tardío LOS VACIABA otra vez
            // (persistencia fantasma #1). Las migraciones corren en ambos
            // eventos: en la carga ven la versión RESTAURADA del WorldStore y
            // no re-ejecutan; en partida nueva corren sobre almacenes limpios.
            if (name == "NewGameStarted")
            {
                ReworkPersistence.ClearAll();
            }
            if (name == "NewGameStarted" || name == "GameInitialized")
            {
                ReworkMigration.RunAllMigrations(Current.Game, new System.Collections.Generic.Dictionary<string, object>());
            }
        }
        catch (System.Exception e)
        {
            Lg.Error($"RuntimeHooks.PublishLifecycleEvent('{name}') falló: {e}");
        }
    }

    /// <summary>
    /// Reenvía cada HistoryEvent del juego a los generadores de lore [ReworkLore]
    /// cuya Key coincide con el defName del evento; si generan texto, se muestra
    /// un Mensaje (flavor narrativo real).
    ///
    /// §53-audit-hooks: early-out si no hay generadores registrados; el try/catch
    /// se eliminó del dispatcher porque ReworkLore.Generate ya maneja errores
    /// internamente.
    /// </summary>
    public static void OnHistoryEvent(RimWorld.HistoryEvent ev)
    {
        if (ReworkLore.GeneratorCount == 0 || ev.def == null) return;
        string key = ev.def.defName;
        if (!ReworkLore.HasGenerator(key)) return;
        string text = ReworkLore.Generate(key, new System.Collections.Generic.Dictionary<string, object>
        {
            ["eventDef"] = key
        });
        if (!string.IsNullOrEmpty(text))
        {
            Messages.Message(text, MessageTypeDefOf.NeutralEvent, historical: true);
        }
    }

    /// <summary>
    /// Dibuja los overlays [ReworkOverlay] registrados en el pase de GUI del mapa.
    /// Inyectado al inicio de Verse.MapComponentUtility.MapComponentOnGUI(Map);
    /// solo dibuja en el evento Repaint (un pase por frame).
    ///
    /// §53-audit-hooks: early-out si no hay drawers; el try/catch se eliminó porque
    /// RenderAll ya maneja errores por drawer individual.
    /// </summary>
    public static void RenderReworkOverlays(Verse.Map map)
    {
        if (ReworkOverlay.Count == 0) return;
        if (Event.current != null && Event.current.type != EventType.Repaint) return;
        if (map == null) return;
        ReworkOverlay.RenderAll(map);
    }
}
