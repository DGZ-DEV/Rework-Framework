using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Rework.Core;
using UnityEngine;
using Verse;

namespace Rework;

/// <summary>
/// ESCÁNER MAESTRO de las features declarativas de Rework (Fase "enganchar los
/// fantasmas"). ReworkAPI solo define atributos + registros puros; este
/// [StaticConstructorOnStartup] (que corre cuando el juego ya cargó Defs) escanea
/// los ensamblados de los mods activos que referencian 0ReworkAPI y REGISTRA cada
/// atributo en su registro correspondiente:
///
///   [ReworkAIModifier]  → ReworkAIRegistry          (despacho: ThinkNode.GetPriority)
///   [ReworkAlert]       → ReworkAlertRegistry       (despacho: AlertsReadout.ctor)
///   [ReworkCompatWith]  → ReworkCompat              (despacho: al escanear, si el mod está activo)
///   [ReworkDefBuilder]  → ReworkDefBuilder          (despacho: BuildAll → DefDatabase)
///   [ReworkGizmo]       → ReworkGizmoRegistry       (despacho: Thing/Pawn.GetGizmos)
///   [ReworkInspectString]→ ReworkInspectStringRegistry (despacho: GetInspectString)
///   [ReworkLive]        → ReworkLiveEngine          (despacho: hot-reload)
///   [ReworkLore]        → ReworkLore                (despacho: HistoryEventsManager.RecordEvent)
///   [ReworkMigration]   → ReworkMigration           (despacho: inicio de partida)
///   [ReworkOn]          → ReworkBus.Register        (despacho: publicador en RuntimeHooks)
///   [ReworkQuest]       → ReworkQuestRegistry       (despacho: QuestRuntime por tick)
///   [ReworkRequires]    → ReworkDepGraph + validación (despacho: al escanear)
///   [ReworkSchedule]    → ReworkScheduler           (despacho: GameComponentUtility.GameComponentTick)
///   [ReworkStatusEffect]→ ReworkStatusEffectRegistry + HediffDef (despacho: PawnCreated + expiración)
///   [ReworkTab]         → ReworkTabRegistry + pestaña en el panel de colonos (despacho: CurTabs)
///
/// También conecta delegados internos que ReworkCore necesita (AutoWatchTick,
/// ReworkMigration.LogHook).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkAttributeScanner
{
    private static readonly HashSet<Type> busRegisteredTypes = new();

    static ReworkAttributeScanner()
    {
        ScanAll();
        WireDelegates();
    }

    /// <summary>Vuelve a escanear (utilizable tras hot-reload de un mod).</summary>
    public static void ScanAll()
    {
        int aiCount = 0, gizmoCount = 0, inspectCount = 0, alertCount = 0, tabCount = 0,
            scheduleCount = 0, migrationCount = 0, compatCount = 0, defBuilderCount = 0,
            questCount = 0, loreCount = 0, statusCount = 0, onCount = 0, liveCount = 0;

        try
        {
            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                    || ReworkConfig.ExcludedMods.Contains(mod.Name))
                    continue;

                foreach (var asm in mod.assemblies.loadedAssemblies)
                {
                    if (asm == null) continue;
                    string asmName = asm.GetName().Name ?? "";

                    // Requisitos a nivel de ensamblado: [assembly: ReworkRequires("id")]
                    ScanAssemblyRequirements(asm, asmName);

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types; }
                    catch { continue; }

                    foreach (var type in types)
                    {
                        if (type == null) continue;

                        // --- Clases: Quest / StatusEffect / Live ---
                        var questAttr = type.GetCustomAttribute<ReworkQuestAttribute>();
                        if (questAttr != null)
                        {
                            if (typeof(ReworkQuestBase).IsAssignableFrom(type) && !type.IsAbstract)
                            {
                                ReworkQuestRegistry.Register(questAttr.DefName, type);
                                questCount++;
                            }
                            else
                            {
                                Lg.Error($"[ReworkQuest] '{type.FullName}' debe heredar de ReworkQuestBase (concreta).");
                            }
                        }

                        var statusAttr = type.GetCustomAttribute<ReworkStatusEffectAttribute>();
                        if (statusAttr != null)
                        {
                            if (RegisterStatusEffect(statusAttr, type))
                                statusCount++;
                        }

                        var liveAttr = type.GetCustomAttribute<ReworkLiveAttribute>();
                        if (liveAttr != null)
                        {
                            ReworkLiveEngine.RegisterFromAttribute(type);
                            liveCount++;
                        }

                        // --- [ReworkOn]: suscripción al bus (una vez por tipo) ---
                        if (type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                .Any(m => m.GetCustomAttributes<ReworkOnAttribute>().Any())
                            && busRegisteredTypes.Add(type))
                        {
                            ReworkBus.Register(type);
                            onCount++;
                        }

                        // --- Métodos ---
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        {
                            if (method.ContainsGenericParameters) continue;

                            var ai = method.GetCustomAttribute<ReworkAIModifierAttribute>();
                            if (ai != null)
                            {
                                ReworkAIRegistry.RegisterModifier(ai.TargetJobDef, ai.Priority, AdaptAiModifier(method));
                                aiCount++;
                            }

                            var gizmo = method.GetCustomAttribute<ReworkGizmoAttribute>();
                            if (gizmo != null)
                            {
                                ReworkGizmoRegistry.Register(gizmo.TargetType, gizmo.Label, gizmo.Description, method, gizmo.IconPath);
                                gizmoCount++;
                            }

                            var inspect = method.GetCustomAttribute<ReworkInspectStringAttribute>();
                            if (inspect != null)
                            {
                                ReworkInspectStringRegistry.Register(inspect.TargetType, AdaptInspect(method));
                                inspectCount++;
                            }

                            var alert = method.GetCustomAttribute<ReworkAlertAttribute>();
                            if (alert != null)
                            {
                                ReworkAlertRegistry.Register(alert.Name, alert.Label, alert.Explanation, alert.Priority, method);
                                alertCount++;
                            }

                            var tab = method.GetCustomAttribute<ReworkTabAttribute>();
                            if (tab != null)
                            {
                                ReworkTabRegistry.Register(tab.Id, tab.Label, AdaptTab(method));
                                tabCount++;
                            }

                            var schedule = method.GetCustomAttribute<ReworkScheduleAttribute>();
                            if (schedule != null)
                            {
                                int interval = schedule.EveryTicks > 0
                                    ? schedule.EveryTicks
                                    : schedule.EveryDay ? GenDate.TicksPerDay
                                    : schedule.EverySeason ? GenDate.TicksPerSeason : 0;
                                if (interval > 0)
                                {
                                    ReworkScheduler.Register(method, interval);
                                    scheduleCount++;
                                }
                            }

                            var migration = method.GetCustomAttribute<ReworkMigrationAttribute>();
                            if (migration != null)
                            {
                                ReworkMigration.RegisterStep(migration.ModId, migration.FromVersion, migration.ToVersion, AdaptMigration(method));
                                migrationCount++;
                            }

                            var compat = method.GetCustomAttribute<ReworkCompatWithAttribute>();
                            if (compat != null)
                            {
                                ReworkCompat.RegisterCompatHook(compat.PackageId, method);
                                compatCount++;
                            }

                            var defBuilder = method.GetCustomAttribute<ReworkDefBuilderAttribute>();
                            if (defBuilder != null)
                            {
                                ReworkDefBuilder.RegisterBuilder(method);
                                defBuilderCount++;
                            }

                            var lore = method.GetCustomAttribute<ReworkLoreAttribute>();
                            if (lore != null)
                            {
                                ReworkLore.RegisterGenerator(lore.Key, AdaptLore(method));
                                loreCount++;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkAttributeScanner] El escaneo lanzó una excepción: {e}");
        }

        // --- Despachos inmediatos tras el registro ---
        if (compatCount > 0)
        {
            ReworkCompat.RunHooksForActiveMods(
                packageId => LoadedModManager.RunningModsListForReading
                    .Any(m => string.Equals(m.PackageIdPlayerFacing, packageId, StringComparison.OrdinalIgnoreCase)));
        }
        if (defBuilderCount > 0)
        {
            ReworkDefBuilder.BuildAll(AddDefToDatabase);
        }

        // --- Pestañas: instancias reales en el panel de inspección de colonos ---
        AttachTabsToPawnPane();

        Lg.Info($"[ReworkAttributeScanner] Escaneo completo: AI={aiCount} Gizmo={gizmoCount} Inspect={inspectCount} " +
                $"Alert={alertCount} Tab={tabCount} Schedule={scheduleCount} Migration={migrationCount} " +
                $"Compat={compatCount} DefBuilder={defBuilderCount} Quest={questCount} Lore={loreCount} " +
                $"StatusEffect={statusCount} Bus([ReworkOn])={onCount} Live=[{liveCount}]");
    }

    private static void WireDelegates()
    {
        // ReworkCore necesita el auto-detector de [ReworkWatch] (tick por tick).
        Rework.Core.RuntimeHooks.AutoWatchTick = ReworkWatchRegistry.AutoTick;
        // ReworkAPI no puede loguear; conectamos el hook al log central.
        ReworkMigration.LogHook = (modId, from, to) =>
            Lg.Info($"[ReworkMigration] '{modId}': migración {from} → {to} completada.");
    }

    // ---------------------------------------------------------------------------
    // Requisitos [ReworkRequires] (assembly-level)
    // ---------------------------------------------------------------------------
    private static void ScanAssemblyRequirements(Assembly asm, string asmName)
    {
        try
        {
            var requires = asm.GetCustomAttributes<ReworkRequiresAttribute>();
            foreach (var req in requires)
            {
                foreach (var requiredId in req.RequiredModIds)
                {
                    ReworkDepGraph.AddDependency(asmName, requiredId);
                    bool present = LoadedModManager.RunningModsListForReading
                        .Any(m => string.Equals(m.PackageIdPlayerFacing, requiredId, StringComparison.OrdinalIgnoreCase));
                    if (!present)
                    {
                        Lg.Error($"[ReworkRequires] El ensamblado '{asmName}' requiere el mod '{requiredId}' y NO está activo. " +
                                 "Sus features Rework pueden fallar.");
                    }
                    else
                    {
                        Lg.Verbose($"[ReworkRequires] '{asmName}' → '{requiredId}' presente ✓.");
                    }
                }
            }
        }
        catch
        {
        }
    }

    // ---------------------------------------------------------------------------
    // Efectos de estado: registro + creación del HediffDef real
    // ---------------------------------------------------------------------------
    private static bool RegisterStatusEffect(ReworkStatusEffectAttribute attr, Type handlerType)
    {
        try
        {
            if (!typeof(Hediff).IsAssignableFrom(handlerType))
            {
                Lg.Error($"[ReworkStatusEffect] '{handlerType.FullName}' debe heredar de Verse.Hediff.");
                return false;
            }

            ReworkStatusEffectRegistry.Register(attr.DefName, attr.Label ?? attr.DefName,
                attr.Description ?? "Efecto de estado registrado por Rework.", attr.DurationTicks, handlerType, attr.AutoApply);

            var existing = DefDatabase<HediffDef>.GetNamedSilentFail(attr.DefName);
            if (existing == null)
            {
                var def = new HediffDef
                {
                    defName = attr.DefName,
                    label = attr.Label ?? attr.DefName,
                    description = attr.Description ?? "Efecto de estado registrado por Rework.",
                    hediffClass = handlerType,
                    initialSeverity = 1f,
                };
                DefDatabase<HediffDef>.Add(def);
            }
            Lg.Info($"[ReworkStatusEffect] Registrado HediffDef '{attr.DefName}' -> {handlerType.Name}" +
                    (attr.AutoApply ? " (auto-aplicación a colonos nuevos)" : "") + ".");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkStatusEffect] Error registrando '{attr.DefName}': {e.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------------------
    // Pestañas: instancias reales en ThingDefOf.Human.inspectorTabsResolved
    // (MainTabWindow_Inspect.get_CurTabs lee exactamente esa lista).
    // ---------------------------------------------------------------------------
    private static void AttachTabsToPawnPane()
    {
        try
        {
            var tabs = ReworkTabRegistry.AllTabs;
            if (tabs == null || tabs.Count == 0) return;

            var humanDef = ThingDefOf.Human;
            if (humanDef == null)
            {
                Lg.Error("[ReworkTab] ThingDefOf.Human no disponible; las pestañas no se adjuntaron.");
                return;
            }

            if (humanDef.inspectorTabsResolved == null)
                humanDef.inspectorTabsResolved = new List<InspectTabBase>();

            int added = 0;
            foreach (var entry in tabs)
            {
                var inst = new Rework.Core.ReworkRuntime.ReworkRuntimeTab(entry);
                if (!humanDef.inspectorTabsResolved.Contains(inst))
                {
                    humanDef.inspectorTabsResolved.Add(inst);
                    added++;
                }
            }
            Lg.Info($"[ReworkTab] {added} pestaña(s) [ReworkTab] añadida(s) al panel de inspección de colonos.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkTab] No se pudieron adjuntar las pestañas: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // DefBuilder: añadir el Def creado a DefDatabase<T> (reflexión genérica)
    // ---------------------------------------------------------------------------
    private static void AddDefToDatabase(object defObj)
    {
        try
        {
            if (defObj is not Def def) return;
            if (string.IsNullOrEmpty(def.defName)) return;

            var defType = def.GetType();
            var existing = GenDefDatabase.GetDefSilentFail(defType, def.defName, false);
            if (existing != null)
            {
                Lg.Verbose($"[ReworkDefBuilder] '{def.defName}' ya existe; se omite ({defType.Name}).");
                return;
            }

            def.ResolveReferences();
            var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
            var add = dbType.GetMethod("Add", BindingFlags.Public | BindingFlags.Static, null, new[] { defType }, null);
            add?.Invoke(null, new object[] { def });
            var setIdx = dbType.GetMethod("SetIndices", BindingFlags.Public | BindingFlags.Static);
            setIdx?.Invoke(null, null);
            Lg.Info($"[ReworkDefBuilder] Def '{def.defName}' ({defType.Name}) añadido a DefDatabase desde código puro.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDefBuilder] No se pudo añadir '{defObj}': {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------
    // Adaptadores de firma (los atributos aceptan firmas flexibles)
    // ---------------------------------------------------------------------------
    private static ReworkAIRegistry.AIModifierDelegate AdaptAiModifier(MethodInfo m)
    {
        return (pawn, jobDef, prio) =>
        {
            try
            {
                var pars = m.GetParameters();
                object? p0 = pars.Length >= 1 && pars[0].ParameterType.IsInstanceOfType(pawn) ? pawn : null;
                if (pars.Length == 3)
                    return Convert.ToSingle(m.Invoke(null, new[] { p0, jobDef, prio }));
                if (pars.Length == 2)
                    return Convert.ToSingle(m.Invoke(null, new[] { p0, prio }));
                if (pars.Length == 1)
                    return Convert.ToSingle(m.Invoke(null, new[] { p0 }));
                return Convert.ToSingle(m.Invoke(null, null));
            }
            catch
            {
                return prio;
            }
        };
    }

    private static Func<object, string?> AdaptInspect(MethodInfo m)
    {
        return target =>
        {
            try
            {
                var pars = m.GetParameters();
                if (pars.Length == 0) return m.Invoke(null, null) as string;
                if (pars.Length == 1)
                {
                    object? arg = pars[0].ParameterType.IsInstanceOfType(target) ? target : null;
                    return m.Invoke(null, new[] { arg }) as string;
                }
            }
            catch { }
            return null;
        };
    }

    private static Action<object, object> AdaptTab(MethodInfo m)
    {
        return (rectObj, target) =>
        {
            try
            {
                var pars = m.GetParameters();
                if (pars.Length == 0) { m.Invoke(null, null); return; }
                if (pars.Length == 1)
                {
                    m.Invoke(null, new[] { Coerce(pars[0], rectObj) });
                    return;
                }
                m.Invoke(null, new[] { Coerce(pars[0], rectObj), Coerce(pars[1], target) });
            }
            catch { }
        };
    }

    private static Action<object, Dictionary<string, object>> AdaptMigration(MethodInfo m)
    {
        return (context, data) =>
        {
            try
            {
                var pars = m.GetParameters();
                if (pars.Length == 0) { m.Invoke(null, null); return; }
                if (pars.Length == 1)
                {
                    m.Invoke(null, new[] { Coerce(pars[0], context) });
                    return;
                }
                m.Invoke(null, new object[] { Coerce(pars[0], context), data });
            }
            catch { }
        };
    }

    private static Func<Dictionary<string, object>, string> AdaptLore(MethodInfo m)
    {
        return parameters =>
        {
            try
            {
                var pars = m.GetParameters();
                if (pars.Length == 0) return m.Invoke(null, null) as string ?? string.Empty;
                if (pars.Length == 1)
                    return m.Invoke(null, new[] { Coerce(pars[0], parameters) }) as string ?? string.Empty;
            }
            catch { }
            return string.Empty;
        };
    }

    private static object? Coerce(ParameterInfo p, object raw)
    {
        if (raw == null) return null;
        if (p.ParameterType.IsInstanceOfType(raw)) return raw;
        if (p.ParameterType == typeof(Rect) && raw is Rect r) return r;
        if (p.ParameterType == typeof(Dictionary<string, object>) && raw is Dictionary<string, object> d) return d;
        try { return Convert.ChangeType(raw, p.ParameterType); }
        catch { return null; }
    }
}

/// <summary>
/// Hooks de ciclo de vida del propio framework (inyectados como los de cualquier mod):
/// aplicación automática de [ReworkStatusEffect(AutoApply=true)] a colonos nuevos.
/// </summary>
public static class ReworkFrameworkLifecycleHooks
{
    [ReworkHook(ReworkHookPoint.PawnCreated)]
    public static void OnPawnCreated(Verse.Pawn pawn)
    {
        Rework.Core.ReworkRuntime.StatusEffectRuntime.TryApplyNewbornAuto(pawn);
    }
}