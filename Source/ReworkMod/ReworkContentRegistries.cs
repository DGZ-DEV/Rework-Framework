using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador automático de RecipeDefs declarados con [ReworkRecipe] en runtime.
/// Se ejecuta automáticamente cuando RimWorld termina de cargar los Defs ([StaticConstructorOnStartup]).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkRecipeRegistry
{
    private static readonly Dictionary<string, RecipeDef> registered = new();

    static ReworkRecipeRegistry()
    {
        RegisterAll();
    }

    public static RecipeDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkRecipeAttribute>();
                    if (attr == null) continue;

                    if (!typeof(RecipeWorker).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkRecipe] '{type.FullName}' tiene [ReworkRecipe] pero no hereda de Verse.RecipeWorker.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkRecipeRegistry: {count} receta(s) declarativa(s) [ReworkRecipe] registrada(s) en DefDatabase.");
    }

    private static bool Register(ReworkRecipeAttribute attr, Type workerType)
    {
        try
        {
            var existing = DefDatabase<RecipeDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var recipeDef = new RecipeDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Receta registrada por Rework.",
                jobString = attr.JobString,
                workAmount = attr.WorkAmount,
                workerClass = workerType
            };

            // Enlazar mesas si se especificaron
            if (!string.IsNullOrEmpty(attr.RecipeUsers))
            {
                recipeDef.recipeUsers = new List<ThingDef>();
                var users = attr.RecipeUsers.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var userName in users)
                {
                    var userTrimmed = userName.Trim();
                    var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(userTrimmed);
                    if (thingDef != null)
                    {
                        recipeDef.recipeUsers.Add(thingDef);
                        if (thingDef.recipes == null) thingDef.recipes = new List<RecipeDef>();
                        if (!thingDef.recipes.Contains(recipeDef))
                            thingDef.recipes.Add(recipeDef);
                    }
                }
            }

            DefDatabase<RecipeDef>.Add(recipeDef);
            registered[attr.DefName] = recipeDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "RecipeDef", true);
            Lg.Info($"[ReworkRecipe] Registrada RecipeDef '{attr.DefName}' -> {workerType.Name}.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkRecipe] Error registrando RecipeDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de HediffDefs declarados con [ReworkHediff] en runtime.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkHediffRegistry
{
    private static readonly Dictionary<string, HediffDef> registered = new();

    static ReworkHediffRegistry()
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
                    var attr = type.GetCustomAttribute<ReworkHediffAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Hediff).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkHediff] '{type.FullName}' tiene [ReworkHediff] pero no hereda de Verse.Hediff.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkHediffRegistry: {count} Hediff(s) declarativo(s) [ReworkHediff] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkHediffAttribute attr, Type hediffType)
    {
        try
        {
            var existing = DefDatabase<HediffDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var hediffDef = new HediffDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Condición de salud registrada por Rework.",
                hediffClass = hediffType,
                initialSeverity = attr.InitialSeverity,
                lethalSeverity = attr.LethalSeverity,
                isBad = attr.IsBad
            };

            DefDatabase<HediffDef>.Add(hediffDef);
            registered[attr.DefName] = hediffDef;
            Lg.Info($"[ReworkHediff] Registrado HediffDef '{attr.DefName}' -> {hediffType.Name}.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkHediff] Error registrando HediffDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de TraitDefs declarados con [ReworkTrait] en runtime.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkTraitRegistry
{
    private static readonly Dictionary<string, TraitDef> registered = new();

    static ReworkTraitRegistry()
    {
        RegisterAll();
    }

    public static TraitDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<TraitDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkTraitAttribute>();
                    if (attr == null) continue;

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkTraitRegistry: {count} Trait(s) declarativo(s) [ReworkTrait] registrado(s) en DefDatabase.");
    }

    private static bool Register(ReworkTraitAttribute attr, Type declaringType)
    {
        try
        {
            var existing = DefDatabase<TraitDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var degreeData = new TraitDegreeData
            {
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Rasgo de personalidad registrado por Rework.",
                degree = attr.Degree,
                commonality = attr.Commonality
            };

            var traitDef = new TraitDef
            {
                defName = attr.DefName,
                degreeDatas = new List<TraitDegreeData> { degreeData }
            };

            DefDatabase<TraitDef>.Add(traitDef);
            registered[attr.DefName] = traitDef;
            Lg.Info($"[ReworkTrait] Registrado TraitDef '{attr.DefName}' ('{degreeData.label}', degree={attr.Degree}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkTrait] Error registrando TraitDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de NeedDefs declarados con [ReworkNeed] en runtime.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkNeedRegistry
{
    private static readonly Dictionary<string, NeedDef> registered = new();

    static ReworkNeedRegistry()
    {
        RegisterAll();
    }

    public static NeedDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<NeedDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkNeedAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Need).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkNeed] '{type.FullName}' tiene [ReworkNeed] pero no hereda de RimWorld.Need.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkNeedRegistry: {count} Need(s) declarativa(s) [ReworkNeed] registrada(s) en DefDatabase.");
    }

    private static bool Register(ReworkNeedAttribute attr, Type needType)
    {
        try
        {
            var existing = DefDatabase<NeedDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var needDef = new NeedDef
            {
                defName = attr.DefName,
                label = attr.Label ?? attr.DefName,
                description = attr.Description ?? "Necesidad registrada por Rework.",
                needClass = needType,
                baseLevel = attr.BaseLevel,
                fallPerDay = attr.FallPerDay,
                colonistsOnly = attr.ColonistsOnly
            };

            DefDatabase<NeedDef>.Add(needDef);
            registered[attr.DefName] = needDef;
            ReworkContentSelfCheck.RegisterDef(attr.DefName, "NeedDef", true);
            Lg.Info($"[ReworkNeed] Registrado NeedDef '{attr.DefName}' -> {needType.Name}.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkNeed] Error registrando NeedDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de Designators declarados con [ReworkDesignator] en runtime.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkDesignatorRegistry
{
    private static readonly Dictionary<Type, DesignationCategoryDef> registered = new();

    static ReworkDesignatorRegistry()
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
                    if (type == null || !type.IsClass || type.IsAbstract) continue;
                    var attr = type.GetCustomAttribute<ReworkDesignatorAttribute>();
                    if (attr == null) continue;

                    if (!typeof(Designator).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkDesignator] '{type.FullName}' tiene [ReworkDesignator] pero no hereda de Verse.Designator.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkDesignatorRegistry: {count} Designator(s) declarativo(s) [ReworkDesignator] enlazado(s).");
    }

    private static bool Register(ReworkDesignatorAttribute attr, Type designatorType)
    {
        try
        {
            var category = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(attr.Category)
                           ?? DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Orders")
                           ?? DesignationCategoryDefOf.Zone;

            if (category.specialDesignatorClasses == null)
            {
                category.specialDesignatorClasses = new List<Type>();
            }

            if (!category.specialDesignatorClasses.Contains(designatorType))
            {
                category.specialDesignatorClasses.Add(designatorType);
                registered[designatorType] = category;
                Lg.Info($"[ReworkDesignator] Registrado {designatorType.Name} en categoría '{category.defName}'.");
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkDesignator] Error enlazando designator '{designatorType.Name}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador automático de GenSteps declarados con [ReworkGenStep] en runtime.
/// Inyecta GenStepDef en DefDatabase y en la lista de genSteps del MapGeneratorDef.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkGenStepRegistry
{
    private static readonly Dictionary<string, GenStepDef> registered = new();

    static ReworkGenStepRegistry()
    {
        RegisterAll();
    }

    public static GenStepDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registered.TryGetValue(defName, out var def)) return def;
        return DefDatabase<GenStepDef>.GetNamedSilentFail(defName);
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
                    var attr = type.GetCustomAttribute<ReworkGenStepAttribute>();
                    if (attr == null) continue;

                    if (!typeof(GenStep).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkGenStep] '{type.FullName}' tiene [ReworkGenStep] pero no hereda de Verse.GenStep.");
                        continue;
                    }

                    if (Register(attr, type)) count++;
                }
            }
        }

        Lg.Info($"ReworkGenStepRegistry: {count} GenStep(s) declarativo(s) [ReworkGenStep] registrado(s).");
    }

    private static bool Register(ReworkGenStepAttribute attr, Type genStepType)
    {
        try
        {
            var existing = DefDatabase<GenStepDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registered[attr.DefName] = existing;
                return false;
            }

            var genStepInstance = (GenStep)Activator.CreateInstance(genStepType);
            var genStepDef = new GenStepDef
            {
                defName = attr.DefName,
                order = attr.Order,
                genStep = genStepInstance
            };

            DefDatabase<GenStepDef>.Add(genStepDef);
            registered[attr.DefName] = genStepDef;

            // Enlazar al MapGeneratorDef correspondiente
            var mapGen = DefDatabase<MapGeneratorDef>.GetNamedSilentFail(attr.MapGenerator)
                         ?? DefDatabase<MapGeneratorDef>.GetNamedSilentFail("MainMapGenerator");

            if (mapGen != null)
            {
                if (mapGen.genSteps == null) mapGen.genSteps = new List<GenStepDef>();
                if (!mapGen.genSteps.Contains(genStepDef))
                {
                    mapGen.genSteps.Add(genStepDef);
                    mapGen.genSteps.Sort((a, b) => a.order.CompareTo(b.order));
                }
            }

            Lg.Info($"[ReworkGenStep] Registrado GenStepDef '{attr.DefName}' -> {genStepType.Name} (order={attr.Order}, mapGen={mapGen?.defName}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkGenStep] Error registrando GenStepDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Motor de mutaciones declarativas y reversibles de Defs vanilla ([ReworkMutate]).
/// Aplica modificaciones en runtime rastreadas y registradas en el diagnóstico.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkMutateRegistry
{
    private class MutationRecord
    {
        public Def TargetDef = null!;
        public FieldInfo Field = null!;
        public object? OriginalValue;
        public object? MutatedValue;
    }

    private static readonly List<MutationRecord> history = new();

    static ReworkMutateRegistry()
    {
        ApplyAll();
    }

    public static int AppliedCount => history.Count;

    public static void ApplyAll()
    {
        int count = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                // Mutaciones a nivel de ensamblado
                var asmAttrs = asm.GetCustomAttributes<ReworkMutateAttribute>();
                foreach (var attr in asmAttrs)
                {
                    if (ApplyMutation(attr)) count++;
                }

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    var typeAttrs = type.GetCustomAttributes<ReworkMutateAttribute>();
                    foreach (var attr in typeAttrs)
                    {
                        if (ApplyMutation(attr)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkMutateRegistry: {count} mutación(es) declarativa(s) [ReworkMutate] aplicada(s) sobre Defs.");
    }

    private static bool ApplyMutation(ReworkMutateAttribute attr)
    {
        try
        {
            // Resolver el Def objetivo
            Def? target = null;
            if (attr.DefType != null)
            {
                target = GenDefDatabase.GetDefSilentFail(attr.DefType, attr.DefName, false);
            }
            else
            {
                // Búsqueda en los tipos más comunes
                target = DefDatabase<ThingDef>.GetNamedSilentFail(attr.DefName)
                         ?? (Def)DefDatabase<IncidentDef>.GetNamedSilentFail(attr.DefName)
                         ?? (Def)DefDatabase<RecipeDef>.GetNamedSilentFail(attr.DefName);
            }

            if (target == null)
            {
                Lg.Error($"[ReworkMutate] No se encontró el Def '{attr.DefName}'.");
                return false;
            }

            // Resolver campo
            var field = target.GetType().GetField(attr.FieldPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Lg.Error($"[ReworkMutate] Campo '{attr.FieldPath}' no encontrado en {target.GetType().Name}.");
                return false;
            }

            object? original = field.GetValue(target);
            object? converted = attr.Value != null ? Convert.ChangeType(attr.Value, field.FieldType) : null;
            field.SetValue(target, converted);

            history.Add(new MutationRecord
            {
                TargetDef = target,
                Field = field,
                OriginalValue = original,
                MutatedValue = converted
            });

            Lg.Info($"[ReworkMutate] Mutado {target.defName}.{field.Name}: '{original}' -> '{converted}'.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkMutate] Error aplicando mutación en '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Registrador y despachador de reactividad para [ReworkWatch].
/// Escanea métodos con [ReworkWatch] y los engancha para ser notificados cuando un campo inyectado cambia.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkWatchRegistry
{
    private class Watcher
    {
        public string FieldName = "";
        public Type? TargetType;

        // §53-audit-hooks: cachear delegado compilado en vez de MethodInfo.Invoke.
        // El invoker unifica las 3 firmas posibles (0, 1, 3 parámetros) en
        // Action<object, object?, object?> para evitar GetParameters() + Invoke
        // por reflexión en cada notificación.
        public Action<object, object?, object?>? Invoker;

        // Respaldo solo si la creación del delegado falló (caso raro).
        public MethodInfo? FallbackMethod;
        public int ParamCount;
    }

    private static readonly List<Watcher> watchers = new();

    static ReworkWatchRegistry()
    {
        ReworkWatch.Notifier = NotifyChanged;
        RegisterAll();
    }

    public static int WatchedCount => watchers.Count;

    public static void RegisterAll()
    {
        watchers.Clear();
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
                        var attrs = m.GetCustomAttributes<ReworkWatchAttribute>();
                        foreach (var attr in attrs)
                        {
                            var pars = m.GetParameters();
                            var w = new Watcher
                            {
                                FieldName = attr.FieldName,
                                TargetType = attr.TargetType ?? (pars.Length > 0 ? pars[0].ParameterType : null),
                                ParamCount = pars.Length,
                            };
                            // §53-audit-hooks: cachear delegado compilado en vez de MethodInfo.Invoke
                            w.Invoker = TryCreateInvoker(m, pars.Length);
                            if (w.Invoker == null) w.FallbackMethod = m;
                            watchers.Add(w);
                            count++;
                            Lg.Info($"[ReworkWatch] Observador registrado: {m.DeclaringType?.Name}.{m.Name} -> campo '{attr.FieldName}'");
                        }
                    }
                }
            }
        }

        if (count > 0)
            Lg.Info($"[ReworkWatch] {count} observador(es) registrado(s) — pipeline activo.");
        else
            Lg.Info("[ReworkWatch] Sin observadores registrados aún (ningún [ReworkWatch] activo en mods cargados).");

        // Auto-verificación del pipeline: disparamos un evento de prueba DIRECTO
        // para confirmar que el mecanismo de invocación funciona de extremo a extremo.
        PipelineSelfTest();
    }

    private static void PipelineSelfTest()
    {
        bool selfTestFired = false;
        // Watcher temporal solo para la prueba — no persiste
        var selfTestMethod = typeof(ReworkWatchRegistry).GetMethod(nameof(SelfTestCallback), BindingFlags.Static | BindingFlags.NonPublic)!;
        var selfTestWatcher = new Watcher
        {
            FieldName = "__ReworkWatchSelfTest__",
            TargetType = null,
            ParamCount = selfTestMethod.GetParameters().Length,
        };
        selfTestWatcher.Invoker = TryCreateInvoker(selfTestMethod, selfTestWatcher.ParamCount);
        if (selfTestWatcher.Invoker == null) selfTestWatcher.FallbackMethod = selfTestMethod;
        _selfTestFired = false;
        watchers.Add(selfTestWatcher);
        NotifyChanged(new object(), "__ReworkWatchSelfTest__", null, null);
        selfTestFired = _selfTestFired;
        watchers.Remove(selfTestWatcher);

        Lg.Info($"[ReworkWatch] Auto-verificación del pipeline: selfTestFired={selfTestFired} — [ReworkWatch] verificado {(selfTestFired ? "✓" : "✗")}");
    }

    private static bool _selfTestFired = false;
    private static void SelfTestCallback() { _selfTestFired = true; }

    /// <summary>
    /// Notifica a todos los observadores registrados que un campo inyectado ha cambiado.
    /// Invocado manualmente o vía eventos en caliente.
    /// </summary>
    public static void NotifyChanged(object target, string fieldName, object? oldValue, object? newValue)
    {
        if (target == null || string.IsNullOrEmpty(fieldName)) return;

        Type targetType = target.GetType();
        for (int i = 0; i < watchers.Count; i++)
        {
            var w = watchers[i];
            if (string.Equals(w.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                if (w.TargetType == null || w.TargetType.IsAssignableFrom(targetType))
                {
                    try
                    {
                        // §53-audit-hooks: usar delegado cacheado en vez de MethodInfo.Invoke
                        if (w.Invoker != null)
                        {
                            w.Invoker(target, oldValue, newValue);
                        }
                        else if (w.FallbackMethod != null)
                        {
                            if (w.ParamCount == 1)
                                w.FallbackMethod.Invoke(null, new[] { target });
                            else if (w.ParamCount == 3)
                                w.FallbackMethod.Invoke(null, new[] { target, oldValue, newValue });
                            else
                                w.FallbackMethod.Invoke(null, null);
                        }
                    }
                    catch (Exception e)
                    {
                        Lg.Error($"[ReworkWatch] Error ejecutando observador: {e}");
                    }
                }
            }
        }
    }

    // §53-audit-hooks: cachear delegados compilados para [ReworkWatch] en vez de
    // MethodInfo.Invoke + GetParameters() en cada notificación.
    // Estas helpers crean un Action<object, object?, object?> unificado que adapta
    // las 3 firmas posibles (0/1/3 params) del método observado.

    private static Action<object, object?, object?>? TryCreateInvoker(MethodInfo method, int paramCount)
    {
        try
        {
            if (paramCount == 0)
            {
                var del = Delegate.CreateDelegate(typeof(Action), method) as Action;
                return del != null ? (Action<object, object?, object?>)((t, o, n) => del()) : null;
            }
            if (paramCount == 1)
            {
                var paramType = method.GetParameters()[0].ParameterType;
                var del = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(paramType), method);
                if (del == null) return null;
                return Wrap1(paramType, del);
            }
            if (paramCount == 3)
            {
                var pars = method.GetParameters();
                var p1 = pars[0].ParameterType;
                var p2 = pars[1].ParameterType;
                var p3 = pars[2].ParameterType;
                var del = Delegate.CreateDelegate(typeof(Action<,,>).MakeGenericType(p1, p2, p3), method);
                if (del == null) return null;
                return Wrap3(p1, p2, p3, del);
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static Action<object, object?, object?> Wrap1(Type paramType, Delegate del)
    {
        var helper = typeof(ReworkWatchRegistry)
            .GetMethod(nameof(Wrap1), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(paramType);
        return (Action<object, object?, object?>)helper.Invoke(null, new object[] { del })!;
    }

    private static Action<object, object?, object?> Wrap3(Type p1, Type p2, Type p3, Delegate del)
    {
        var helper = typeof(ReworkWatchRegistry)
            .GetMethod(nameof(Wrap3), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(p1, p2, p3);
        return (Action<object, object?, object?>)helper.Invoke(null, new object[] { del })!;
    }

    private static Action<object, object?, object?> Wrap1<T>(Delegate del)
    {
        var action = (Action<T>)del;
        return (t, o, n) => action((T)t);
    }

    private static Action<object, object?, object?> Wrap3<T1, T2, T3>(Delegate del)
    {
        var action = (Action<T1, T2, T3>)del;
        return (t, o, n) => action((T1)t, (T2)o, (T3)n);
    }

    // ---------------------------------------------------------------------------
    // ningún llamador de NotifyChanged; el "watch" solo funcionaba si el mod lo
    // invocaba a mano. Ahora un tick del framework (RuntimeHooks.AutoWatchTick,
    // inyectado en GameComponentUtility.GameComponentTick) compara por reflexión el
    // valor actual de cada campo observado con el último valor cacheado; cuando
    // cambia, notifica a los observadores y publica ReworkFieldChangedEvent en el bus.
    // El cacheado es DÉBIL (ConditionalWeakTable): los pawns destruidos se limpian solos.
    // ---------------------------------------------------------------------------
    private class RefEq : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>> watchCache = new();
    private static int lastAutoTick = -1;

    /// <summary>
    /// Detección automática de cambios (diff por tick). Se llama desde
    /// RuntimeHooks.OnGameComponentTick una vez por tick de juego.
    /// </summary>
    public static void AutoTick()
    {
        if (watchers.Count == 0) return;
        int now = Find.TickManager?.TicksGame ?? 0;
        if (now == lastAutoTick) return;
        lastAutoTick = now;

        try
        {
            var byType = watchers
                .Where(w => w.TargetType != null && !string.IsNullOrEmpty(w.FieldName))
                .GroupBy(w => w.TargetType!);

            foreach (var group in byType)
            {
                foreach (var inst in ResolveInstances(group.Key))
                {
                    if (inst == null) continue;
                    if (!watchCache.TryGetValue(inst, out var fields))
                    {
                        fields = new Dictionary<string, object?>(StringComparer.Ordinal);
                        watchCache.Add(inst, fields);
                    }

                    foreach (var w in group)
                    {
                        object? current = ReadField(inst, w.FieldName);
                        if (fields.TryGetValue(w.FieldName, out var previous))
                        {
                            if (!Equals(current, previous))
                            {
                                NotifyChanged(inst, w.FieldName, previous, current);
                                ReworkBus.Publish(new ReworkFieldChangedEvent<object, object>(inst, w.FieldName, previous, current));
                            }
                        }
                        fields[w.FieldName] = current;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkWatch] AutoTick falló: {e.Message}");
        }
    }

    private static IEnumerable<object> ResolveInstances(Type targetType)
    {
        try
        {
            if (targetType == typeof(Pawn))
            {
                var list = new List<object>();
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map?.mapPawns?.AllPawnsSpawned != null)
                            list.AddRange(map.mapPawns.AllPawnsSpawned);
                    }
                }
                return list;
            }
            if (typeof(Map).IsAssignableFrom(targetType))
            {
                return Find.Maps != null ? Find.Maps.OfType<object>().ToList() : System.Linq.Enumerable.Empty<object>();
            }
        }
        catch { }
        return System.Linq.Enumerable.Empty<object>();
    }

    private static object? ReadField(object inst, string fieldName)
    {
        try
        {
            return inst.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)?.GetValue(inst);
        }
        catch
        {
            return null;
        }
    }
}



