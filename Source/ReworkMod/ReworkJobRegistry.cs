using System;
using System.Collections.Generic;
using System.Reflection;
using Rework.Core;
using Verse;
using Verse.AI;

namespace Rework;

/// <summary>
/// Registro automático de trabajos ([ReworkJob]) en runtime.
/// Se ejecuta automáticamente cuando RimWorld termina de cargar los Defs ([StaticConstructorOnStartup]).
/// Crea y añade los JobDef a DefDatabase sin requerir ningún archivo XML.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkJobRegistry
{
    private static readonly Dictionary<string, JobDef> registeredJobs = new();

    static ReworkJobRegistry()
    {
        RegisterAll();
    }

    /// <summary>Devuelve el JobDef registrado por su defName, o null si no existe.</summary>
    public static JobDef? Get(string defName)
    {
        if (string.IsNullOrEmpty(defName)) return null;
        if (registeredJobs.TryGetValue(defName, out var def)) return def;
        return DefDatabase<JobDef>.GetNamedSilentFail(defName);
    }

    /// <summary>Escanea todos los ensamblados activos y registra los JobDefs declarados con [ReworkJob].</summary>
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

                    var attr = type.GetCustomAttribute<ReworkJobAttribute>();
                    if (attr == null)
                        continue;

                    if (!typeof(JobDriver).IsAssignableFrom(type))
                    {
                        Lg.Error($"[ReworkJob] La clase '{type.FullName}' tiene [ReworkJob] pero no hereda de Verse.AI.JobDriver.");
                        continue;
                    }

                    if (RegisterJob(attr, type))
                        count++;
                }
            }
        }

        Lg.Info($"ReworkJobRegistry: {count} trabajo(s) declarativo(s) [ReworkJob] registrado(s) en DefDatabase.");
    }

    private static bool RegisterJob(ReworkJobAttribute attr, Type driverType)
    {
        try
        {
            var existing = DefDatabase<JobDef>.GetNamedSilentFail(attr.DefName);
            if (existing != null)
            {
                registeredJobs[attr.DefName] = existing;
                Lg.Verbose($"[ReworkJob] JobDef '{attr.DefName}' ya existe en DefDatabase. Enlazado.");
                return false;
            }

            var jobDef = new JobDef
            {
                defName = attr.DefName,
                driverClass = driverType,
                reportString = attr.ReportString,
                playerInterruptible = attr.PlayerInterruptible,
                casualInterruptible = attr.CasualInterruptible,
                suspendable = attr.Suspendable
            };

            DefDatabase<JobDef>.Add(jobDef);
            registeredJobs[attr.DefName] = jobDef;
            Lg.Info($"[ReworkJob] Registrado JobDef '{attr.DefName}' -> driver {driverType.Name} ('{attr.ReportString}').");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkJob] Error al registrar JobDef '{attr.DefName}': {e}");
            return false;
        }
    }
}

/// <summary>
/// Clase base opcional y amigable para JobDrivers con helpers para construir Toils comunes.
/// </summary>
public abstract class ReworkJobDriver : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return true;
    }

    /// <summary>Helper para crear un Toil que mueve al colono hacia un objetivo.</summary>
    protected Toil ToilGoto(TargetIndex ind, PathEndMode peMode = PathEndMode.Touch)
    {
        return Toils_Goto.Goto(ind, peMode);
    }

    /// <summary>Helper para crear un Toil que espera una duración fija en ticks.</summary>
    protected Toil ToilWait(int ticks)
    {
        return Toils_General.Wait(ticks);
    }

    /// <summary>Helper para crear un Toil que ejecuta una acción instantánea.</summary>
    protected Toil ToilDo(Action action)
    {
        var toil = ToilMaker.MakeToil("ReworkDo");
        toil.initAction = action;
        return toil;
    }
}
