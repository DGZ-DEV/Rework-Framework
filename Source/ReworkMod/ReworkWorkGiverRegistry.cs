using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registro automático de WorkGivers ([ReworkWorkGiver]) en runtime.
/// Se ejecuta automáticamente cuando RimWorld termina de cargar los Defs ([StaticConstructorOnStartup]).
/// Inyecta los WorkGiverDef en DefDatabase y los enlaza en el WorkTypeDef correspondiente.
///
/// Si el WorkTypeDef declarado en el atributo NO existe, se CREA desde los metadatos
/// del propio atributo (WorkTypeLabel/Verb/Gerund/Tags/...). Antes se caía en silencio
/// a WorkTypeDefOf.Hauling, lo que escondía el trabajo dentro de "acarreo" (fantasma).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkWorkGiverRegistry
{
    private static readonly Dictionary<string, WorkGiverDef> registeredGivers = new();

    static ReworkWorkGiverRegistry()
    {
        RegisterAll();
    }

    /// <summary>Devuelve el WorkGiverDef registrado por su defName, o null si no existe.</summary>
    public static WorkGiverDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registeredGivers.TryGetValue(defName, out var def)) return def;
        return DefDatabase<WorkGiverDef>.GetNamedSilentFail(defName);
    }

    /// <summary>Escanea todos los ensamblados activos y registra los WorkGiverDefs declarados con [ReworkWorkGiver].</summary>
    public static void RegisterAll()
    {
        int count = 0;
        var affectedWorkTypes = new HashSet<WorkTypeDef>();

        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass || type.IsAbstract)
                        continue;

                    var attr = type.GetCustomAttribute<ReworkWorkGiverAttribute>();
                    if (attr == null)
                        continue;

                    if (!typeof(RimWorld.WorkGiver).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkWorkGiver] La clase '{type.FullName}' tiene [ReworkWorkGiver] pero no hereda de RimWorld.WorkGiver.");
                        continue;
                    }

                    if (RegisterWorkGiver(attr, type, affectedWorkTypes))
                        count++;
                }
            }
        }

        // Reordenar workGiversByPriority para los WorkTypeDefs afectados
        foreach (var wt in affectedWorkTypes)
        {
            if (wt.workGiversByPriority != null)
            {
                wt.workGiversByPriority = wt.workGiversByPriority
                    .OrderByDescending(wg => wg.priorityInType)
                    .ToList();
            }
        }

        Lg.Info($"ReworkWorkGiverRegistry: {count} WorkGiver(s) declarativo(s) [ReworkWorkGiver] registrado(s) en DefDatabase.");
    }

    private static bool RegisterWorkGiver(ReworkWorkGiverAttribute attr, Type giverType, HashSet<WorkTypeDef> affectedWorkTypes)
    {
        try
        {
            var existing = DefDatabase<WorkGiverDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registeredGivers[attr.DefName] = existing;
                Lg.Verbose($"[ReworkWorkGiver] WorkGiverDef '{attr.DefName}' ya existe en DefDatabase. Enlazado.");
                return false;
            }

            var workTypeDef = ResolveOrCreateWorkType(attr);

            var giverDef = new WorkGiverDef
            {
                defName = attr.DefName,
                label = attr.Verb ?? attr.DefName,
                giverClass = giverType,
                workType = workTypeDef,
                priorityInType = attr.PriorityInType,
                verb = attr.Verb,
                gerund = attr.Gerund,
                directOrderable = attr.DirectOrderable,
                scanThings = attr.ScanThings,
                scanCells = attr.ScanCells,
                emergency = attr.Emergency
            };

            DefDatabase<WorkGiverDef>.Add(giverDef);
            registeredGivers[attr.DefName] = giverDef;

            // Enlazar en la lista de prioridades de su WorkTypeDef
            if (workTypeDef.workGiversByPriority == null)
            {
                workTypeDef.workGiversByPriority = new List<WorkGiverDef>();
            }
            if (!workTypeDef.workGiversByPriority.Contains(giverDef))
            {
                workTypeDef.workGiversByPriority.Add(giverDef);
                affectedWorkTypes.Add(workTypeDef);
            }

            Lg.Info($"[ReworkWorkGiver] Registrado WorkGiverDef '{attr.DefName}' -> giver {giverType.Name} (workType={workTypeDef.defName}, prio={attr.PriorityInType}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkWorkGiver] Error al registrar WorkGiverDef '{attr.DefName}': {e}");
            return false;
        }
    }

    /// <summary>
    /// Devuelve el WorkTypeDef indicado por el atributo. Si no existe y el atributo lo
    /// permite, lo CREA y lo añade a DefDatabase, de modo que [ReworkWorkGiver] puede
    /// declarar su propio tipo de trabajo sin XML (y sin acabar escondido en Hauling).
    /// </summary>
    private static WorkTypeDef ResolveOrCreateWorkType(ReworkWorkGiverAttribute attr)
    {
        var existing = DefDatabase<WorkTypeDef>.GetNamedSilentFail(attr.WorkType);
        if (existing != null) return existing;

        if (!attr.CreateWorkTypeIfMissing || string.IsNullOrEmpty(attr.WorkType))
        {
            Lg.Verbose($"[ReworkWorkGiver] WorkType '{attr.WorkType}' no existe y no se crea; se usa Hauling.");
            return WorkTypeDefOf.Hauling;
        }

        try
        {
            string label = string.IsNullOrEmpty(attr.WorkTypeLabel) ? attr.WorkType : attr.WorkTypeLabel;

            var def = new WorkTypeDef
            {
                defName = attr.WorkType,
                label = label,
                labelShort = attr.WorkTypeLabelShort ?? label,
                pawnLabel = attr.WorkTypePawnLabel ?? label,
                gerundLabel = attr.WorkTypeGerund ?? label,
                verb = attr.WorkTypeVerb ?? label,
                visible = attr.WorkTypeVisible,
                workTags = ParseWorkTags(attr.WorkTypeTags),
                naturalPriority = attr.WorkTypeNaturalPriority,
                alwaysStartActive = attr.WorkTypeAlwaysStartActive,
                relevantSkills = new List<SkillDef>(),
                workGiversByPriority = new List<WorkGiverDef>()
            };

            DefDatabase<WorkTypeDef>.Add(def);

            // Reindexar para que las búsquedas por defName lo encuentren
            // (mismo patrón que el DefBuilder de ReworkAttributeScanner).
            typeof(DefDatabase<WorkTypeDef>)
                .GetMethod("SetIndices", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null);

            Lg.Info($"[ReworkWorkGiver] WorkTypeDef '{def.defName}' creado desde el atributo " +
                    $"(label='{label}', verb='{def.verb}', tags={def.workTags}, prio={def.naturalPriority}).");

            // Sin esto el tipo de trabajo existe pero NO tiene casilla en la pestaña de Trabajo.
            EnsureWorkTabColumn(def);
            return def;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkWorkGiver] No se pudo crear el WorkTypeDef '{attr.WorkType}': {e.Message}. Se usa Hauling.");
            return WorkTypeDefOf.Hauling;
        }
    }

    /// <summary>
    /// Crea la columna de la pestaña de Trabajo para un WorkTypeDef recién creado.
    ///
    /// La pestaña de Trabajo NO se construye a partir de los WorkTypeDef: vanilla genera, al
    /// cargar los Defs (RimWorld.PawnColumnDefGenerator.ImpliedPawnColumnDefs — es decir,
    /// ANTES de cualquier [StaticConstructorOnStartup]), un PawnColumnDef
    /// "WorkPriority_&lt;defName&gt;" por cada WorkTypeDef con visible=true, y lo inserta en
    /// PawnTableDefOf.Work.columns justo antes de la columna de copiar/pegar prioridades.
    /// Un WorkTypeDef creado DESPUÉS de esa fase se queda sin casilla en la pestaña
    /// (ERRORES.md §41): existe y los colonos lo usan, pero el jugador no puede verlo ni
    /// regularlo. Aquí replicamos esa generación para nuestro tipo de trabajo.
    /// </summary>
    private static void EnsureWorkTabColumn(WorkTypeDef def)
    {
        try
        {
            var workTable = PawnTableDefOf.Work;
            if (workTable == null)
            {
                Lg.Error("[ReworkWorkGiver] PawnTableDefOf.Work no disponible; el tipo de trabajo no tendrá columna.");
                return;
            }

            string columnDefName = "WorkPriority_" + def.defName;

            var column = DefDatabase<PawnColumnDef>.GetNamedSilentFail(columnDefName);
            bool isNew = column == null;
            if (isNew)
            {
                column = new PawnColumnDef
                {
                    defName = columnDefName,
                    workType = def,
                    workerClass = typeof(PawnColumnWorker_WorkPriority),
                    sortable = true
                };
            }

            if (workTable.columns == null)
                workTable.columns = new List<PawnColumnDef>();

            // Escalonado de etiquetas de vanilla: alterna por columna de trabajo.
            column.moveWorkTypeLabelDown = workTable.columns.Count(c => c != null && c.workType != null) % 2 == 1;

            if (isNew)
            {
                DefDatabase<PawnColumnDef>.Add(column);
                typeof(DefDatabase<PawnColumnDef>)
                    .GetMethod("SetIndices", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, null);
            }

            if (!workTable.columns.Contains(column))
            {
                // Vanilla inserta las columnas de trabajo justo antes de "copiar/pegar prioridades".
                int idx = workTable.columns.FindIndex(c =>
                    c != null && c.Worker is PawnColumnWorker_CopyPasteWorkPriorities);
                if (idx < 0) idx = workTable.columns.Count;
                workTable.columns.Insert(idx, column);
            }

            Lg.Info($"[ReworkWorkGiver] Columna '{columnDefName}' añadida a la pestaña de Trabajo " +
                    $"(PawnTableDefOf.Work.columns, índice {workTable.columns.IndexOf(column)}).");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkWorkGiver] No se pudo crear la columna de Trabajo para '{def.defName}': {e}");
        }
    }

    /// <summary>Convierte "Violent,ManualDumb" en WorkTags (None si está vacío o no se reconoce).</summary>
    private static WorkTags ParseWorkTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return WorkTags.None;
        try
        {
            return (WorkTags)Enum.Parse(typeof(WorkTags), raw.Replace(" ", ""), ignoreCase: true);
        }
        catch
        {
            Lg.Error($"[ReworkWorkGiver] WorkTags '{raw}' no reconocido; se usa None.");
            return WorkTags.None;
        }
    }
}