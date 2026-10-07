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

            var workTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(attr.WorkType) 
                              ?? WorkTypeDefOf.Hauling;

            var giverDef = new WorkGiverDef
            {
                defName = attr.DefName,
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
}
