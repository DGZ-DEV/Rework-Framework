using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registro automático de incidentes ([ReworkIncident]) en runtime.
/// Se ejecuta automáticamente cuando RimWorld termina de cargar los Defs ([StaticConstructorOnStartup]).
/// Inyecta los IncidentDef en DefDatabase sin requerir archivos XML.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkIncidentRegistry
{
    private static readonly Dictionary<string, IncidentDef> registeredIncidents = new();

    static ReworkIncidentRegistry()
    {
        RegisterAll();
    }

    /// <summary>Devuelve el IncidentDef registrado por su defName, o null si no existe.</summary>
    public static IncidentDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registeredIncidents.TryGetValue(defName, out var def)) return def;
        return DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
    }

    /// <summary>Escanea todos los ensamblados activos y registra los IncidentDefs declarados con [ReworkIncident].</summary>
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

                    var attr = type.GetCustomAttribute<ReworkIncidentAttribute>();
                    if (attr == null)
                        continue;

                    if (!typeof(IncidentWorker).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkIncident] La clase '{type.FullName}' tiene [ReworkIncident] pero no hereda de RimWorld.IncidentWorker.");
                        continue;
                    }

                    if (RegisterIncident(attr, type))
                        count++;
                }
            }
        }

        Lg.Info($"ReworkIncidentRegistry: {count} incidente(s) declarativo(s) [ReworkIncident] registrado(s) en DefDatabase.");
    }

    private static bool RegisterIncident(ReworkIncidentAttribute attr, Type workerType)
    {
        try
        {
            var existing = DefDatabase<IncidentDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registeredIncidents[attr.DefName] = existing;
                Lg.Verbose($"[ReworkIncident] IncidentDef '{attr.DefName}' ya existe en DefDatabase. Enlazado.");
                return false;
            }

            var categoryDef = DefDatabase<IncidentCategoryDef>.GetNamedSilentFail(attr.Category) 
                              ?? IncidentCategoryDefOf.Misc;

            var targetTagDef = DefDatabase<IncidentTargetTagDef>.GetNamedSilentFail(attr.TargetTag) 
                               ?? IncidentTargetTagDefOf.Map_PlayerHome;

            var incidentDef = new IncidentDef
            {
                defName = attr.DefName,
                workerClass = workerType,
                category = categoryDef,
                targetTags = new List<IncidentTargetTagDef> { targetTagDef },
                baseChance = attr.BaseChance,
                letterLabel = attr.LetterLabel,
                letterText = attr.LetterText,
                letterDef = LetterDefOf.PositiveEvent
            };

            DefDatabase<IncidentDef>.Add(incidentDef);
            registeredIncidents[attr.DefName] = incidentDef;
            Lg.Info($"[ReworkIncident] Registrado IncidentDef '{attr.DefName}' -> worker {workerType.Name} (cat={categoryDef.defName}, chance={attr.BaseChance}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkIncident] Error al registrar IncidentDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Clase base opcional y amigable para IncidentWorkers declarativos.
/// </summary>
public abstract class ReworkIncidentWorker : IncidentWorker
{
    public override bool CanFireNowSub(IncidentParms parms)
    {
        return parms.target != null;
    }

    /// <summary>Helper para enviar una carta estándar informando del evento.</summary>
    protected void SendStandardLetter(TaggedString label, TaggedString text, LookTargets lookTargets)
    {
        Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, lookTargets);
    }
}
