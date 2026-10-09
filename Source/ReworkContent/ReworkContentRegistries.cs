using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace Rework;

/// <summary>
/// Registrador automático de GeneDefs declarados con [ReworkGene] (ReworkContent).
/// Corre cuando RimWorld termina de cargar los Defs ([StaticConstructorOnStartup]).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkGeneRegistry
{
    private static readonly Dictionary<string, GeneDef> registered = new();

    static ReworkGeneRegistry()
    {
        RegisterAll();
    }

    public static GeneDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<GeneDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkGeneAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Gene).IsAssignableFrom(type))
                    {
                        Log.Error($"[ReworkGene] '{type.FullName}' tiene [ReworkGene] pero no hereda de Verse.Gene.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkGene] {count} gen(es) declarativo(s) [ReworkGene] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkGeneAttribute attr, Type geneType)
    {
        try
        {
            var existing = DefDatabase<GeneDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var category = DefDatabase<GeneCategoryDef>.GetNamedSilentFail(attr.DisplayCategory)
                           ?? RimWorld.GeneCategoryDefOf.Miscellaneous;

            var geneDef = new GeneDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Gen registrado por ReworkContent.",
                geneClass = attr.GeneClass,
                displayCategory = category,
                biostatCpx = attr.BiostatCpx,
                biostatMet = attr.BiostatMet,
                biostatArc = attr.BiostatArc,
                selectionWeight = attr.SelectionWeight,
                canGenerateInGeneSet = attr.CanGenerateInGeneSet,
                labelShortAdj = attr.LabelShortAdj ?? attr.Label ?? attr.DefName
            };

            DefDatabase<GeneDef>.Add(geneDef);
            registered[attr.DefName] = geneDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "GeneDef", true);
            Log.Message($"[ReworkGene] Registrado GeneDef '{attr.DefName}' -> {geneType.Name} (cat={category.defName}).");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[ReworkGene] Error registrando GeneDef '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de ResearchProjectDefs declarados con [ReworkResearch] (ReworkContent).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkResearchRegistry
{
    private static readonly Dictionary<string, ResearchProjectDef> registered = new();

    static ReworkResearchRegistry()
    {
        RegisterAll();
    }

    public static ResearchProjectDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkResearchAttribute>();
                    if (attr == null) continue;
                    if (Register(attr, type)) count++;
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkResearch] {count} investigación(es) declarativa(s) [ReworkResearch] registrada(s) en DefDatabase.");
    }

    private static bool Register(ReworkResearchAttribute attr, Type declaringType)
    {
        try
        {
            var existing = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var tab = DefDatabase<ResearchTabDef>.GetNamedSilentFail(attr.Tab)
                      ?? RimWorld.ResearchTabDefOf.Main;

            var project = new ResearchProjectDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Investigación registrada por ReworkContent.",
                baseCost = attr.BaseCost,
                techLevel = attr.TechLevel,
                tab = tab,
                researchViewX = attr.X,
                researchViewY = attr.Y,
                prerequisites = new List<ResearchProjectDef>()
            };

            // Prerrequisitos declarativos
            if (!string.IsNullOrEmpty(attr.Prerequisites))
            {
                foreach (var pre in attr.Prerequisites!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var preDef = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(pre.Trim());
                    if (preDef != null) project.prerequisites.Add(preDef);
                }
            }

            if (!string.IsNullOrEmpty(attr.RequiredResearchBuilding))
            {
                project.requiredResearchBuilding = DefDatabase<ThingDef>.GetNamedSilentFail(attr.RequiredResearchBuilding);
            }

            DefDatabase<ResearchProjectDef>.Add(project);
            registered[attr.DefName] = project;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "ResearchProjectDef", true);
            Log.Message($"[ReworkResearch] Registrado ResearchProjectDef '{attr.DefName}' (cost={attr.BaseCost}, tab={tab.defName}).");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[ReworkResearch] Error registrando ResearchProjectDef '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de RaidStrategyDefs declarados con [ReworkRaid] (ReworkContent).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkRaidRegistry
{
    private static readonly Dictionary<string, RaidStrategyDef> registered = new();

    static ReworkRaidRegistry()
    {
        RegisterAll();
    }

    public static RaidStrategyDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<RaidStrategyDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkRaidAttribute>();
                    if (attr == null) continue;

                    if (!typeof(RaidStrategyWorker).IsAssignableFrom(type))
                    {
                        Log.Error($"[ReworkRaid] '{type.FullName}' tiene [ReworkRaid] pero no hereda de RimWorld.RaidStrategyWorker.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkRaid] {count} estrategia(s) declarativa(s) [ReworkRaid] registrada(s) en DefDatabase.");
    }

    private static bool Register(ReworkRaidAttribute attr, Type workerType)
    {
        try
        {
            var existing = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var arriveMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(attr.ArriveMode);
            if (arriveMode == null)
                arriveMode = DefDatabase<PawnsArrivalModeDef>.AllDefsListForReading.FirstOrDefault();

            // Curva plana de factor de puntos (SimpleCurve no usa collection-initializer seguro)
            var curve = new SimpleCurve();
            curve.Add(new CurvePoint(0f, attr.PointsFactor), false);
            curve.Add(new CurvePoint(10000f, attr.PointsFactor), false);

            var raidDef = new RaidStrategyDef
            {
                defName = attr.DefName,
                workerClass = workerType,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Estrategia de incursión registrada por ReworkContent.",
                minPawns = attr.MinPawns,
                pawnsCanBringFood = true,
                pointsFactorCurve = curve,
                arrivalTextEnemy = attr.ArrivalTextEnemy ?? "Una fuerza hostil se acerca.",
                arrivalTextFriendly = attr.ArrivalTextFriendly ?? "Un grupo aliado llega.",
                letterLabelEnemy = attr.LetterLabelEnemy ?? "Incursión",
                letterLabelFriendly = attr.LetterLabelFriendly ?? "Refuerzos",
                arriveModes = arriveMode != null ? new List<PawnsArrivalModeDef> { arriveMode } : null
            };

            DefDatabase<RaidStrategyDef>.Add(raidDef);
            registered[attr.DefName] = raidDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "RaidStrategyDef", true);
            Log.Message($"[ReworkRaid] Registrado RaidStrategyDef '{attr.DefName}' -> {workerType.Name} (minPawns={attr.MinPawns}).");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[ReworkRaid] Error registrando RaidStrategyDef '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de ThoughtDefs declarados con [ReworkThought] (ReworkContent).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkThoughtRegistry
{
    private static readonly Dictionary<string, ThoughtDef> registered = new();

    static ReworkThoughtRegistry()
    {
        RegisterAll();
    }

    public static ThoughtDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkThoughtAttribute>();
                    if (attr == null) continue;

                    if (!typeof(RimWorld.Thought).IsAssignableFrom(type))
                    {
                        Log.Error($"[ReworkThought] '{type.FullName}' tiene [ReworkThought] pero no hereda de RimWorld.Thought.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkThought] {count} pensamiento(s) declarativo(s) [ReworkThought] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkThoughtAttribute attr, Type thoughtType)
    {
        try
        {
            var existing = DefDatabase<ThoughtDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var stage = new ThoughtStage
            {
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Pensamiento registrado por ReworkContent.",
                baseMoodEffect = attr.BaseMoodEffect,
                visible = true
            };

            var thoughtDef = new ThoughtDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Pensamiento registrado por ReworkContent.",
                thoughtClass = attr.ThoughtClass,
                stages = new List<ThoughtStage> { stage },
                durationDays = attr.DurationDays
            };

            DefDatabase<ThoughtDef>.Add(thoughtDef);
            registered[attr.DefName] = thoughtDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "ThoughtDef", true);
            Log.Message($"[ReworkThought] Registrado ThoughtDef '{attr.DefName}' (mood={attr.BaseMoodEffect}, class={thoughtType.Name}).");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[ReworkThought] Error registrando ThoughtDef '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de ThingDefs de prenda (Apparel) declarados con [ReworkApparel] (ReworkContent).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkApparelRegistry
{
    private static readonly Dictionary<string, ThingDef> registered = new();

    static ReworkApparelRegistry()
    {
        RegisterAll();
    }

    public static ThingDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkApparelAttribute>();
                    if (attr == null) continue;
                    if (Register(attr, type)) count++;
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkApparel] {count} prenda(s) declarativa(s) [ReworkApparel] registrada(s) en DefDatabase.");
    }

    private static bool Register(ReworkApparelAttribute attr, Type apparelType)
    {
        try
        {
            var existing = DefDatabase<ThingDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var layer = DefDatabase<ApparelLayerDef>.GetNamedSilentFail(attr.Layer)
                        ?? RimWorld.ApparelLayerDefOf.OnSkin;

            var bodyGroups = new List<BodyPartGroupDef>();
            foreach (var g in attr.BodyPartGroups.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var gDef = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(g.Trim());
                if (gDef != null) bodyGroups.Add(gDef);
            }
            if (bodyGroups.Count == 0) bodyGroups.Add(RimWorld.BodyPartGroupDefOf.Torso);

            var apparel = new ApparelProperties
            {
                bodyPartGroups = bodyGroups,
                layers = new List<ApparelLayerDef> { layer },
                wearPerDay = 0.5f,
                tags = new List<string>()
            };

            var thingDef = new ThingDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Prenda registrada por ReworkContent.",
                thingClass = attr.ApparelClass,
                category = ThingCategory.Item,
                graphicData = new GraphicData
                {
                    texPath = attr.TexPath,
                    drawSize = new UnityEngine.Vector2(1f, 1f)
                },
                apparel = apparel,
                drawGUIOverlay = true,
                selectable = true
            };

            DefDatabase<ThingDef>.Add(thingDef);
            registered[attr.DefName] = thingDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "ThingDef(Apparel)", true);
            Log.Message($"[ReworkApparel] Registrado ThingDef '{attr.DefName}' -> {apparelType.Name} (capa={layer.defName}).");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[ReworkApparel] Error registrando ThingDef '{attr.DefName}': {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Registrador de zonas de efecto ([ReworkZoneEffect]) para ReworkZoneManager (0ReworkAPI).
/// Escanea métodos estáticos con [ReworkZoneEffect] y los conecta al manager.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkZoneEffectRegistry
{
    static ReworkZoneEffectRegistry()
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
                    if (type == null) continue;
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    foreach (var m in methods)
                    {
                        var attr = m.GetCustomAttribute<ReworkZoneEffectAttribute>();
                        if (attr == null) continue;

                        var pars = m.GetParameters();
                        if (pars.Length != 1 || pars[0].ParameterType != typeof(object))
                        {
                            Log.Error($"[ReworkZoneEffect] '{m.DeclaringType?.Name}.{m.Name}' debe tener firma 'static void X(object pawn)'.");
                            continue;
                        }

                        try
                        {
                            ReworkZoneManager.RegisterZone(attr.Id, attr.IntervalTicks, pawn => m.Invoke(null, new[] { pawn }));
                            count++;
                            Log.Message($"[ReworkZoneEffect] Zona '{attr.Id}' conectada a {m.DeclaringType?.Name}.{m.Name} (intervalo {attr.IntervalTicks} ticks).");
                        }
                        catch (Exception e)
                        {
                            Log.Error($"[ReworkZoneEffect] Error conectando zona '{attr.Id}': {e.Message}");
                        }
                    }
                }
            }
        }

        if (count > 0)
            Log.Message($"[ReworkZoneEffect] {count} zona(s) de efecto declarativa(s) registrada(s) en ReworkZoneManager.");
    }
}