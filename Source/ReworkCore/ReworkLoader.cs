using System;
using System.Linq;
using System.Reflection;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Sustituto de Assembly.LoadFrom en Verse.ModAssemblyHandler.ReloadAll (el boot los
/// parchea por Cecil así). Garantiza que la pasada 2 (re-arranque del boot) cargue las
/// copias NUEVAS de los ensamblados que REWORK recargó (Rework.dll / ReworkCore.dll),
/// y no los originales marcados refonly.
///
/// - Si la ruta está registrada en DataStore.AssembliesByPath (copias nuevas recargadas
///   por Loader.LoadAssembly), devuelve esa copia nueva.
/// - Si no, deduplica por nombre simple (Unity ya no admite dos ensamblados con el
///   mismo nombre): devuelve el ya cargado.
/// - De lo contrario, el clásico Assembly.LoadFrom.
/// (Equivalente a DataStore.duplicateAssemblies + reescritura de ReloadAll del fork
/// jikulopo, AssemblyLoadingFreePatch.)
/// </summary>
public static class ReworkLoader
{
    public static Assembly LoadFile(string fullPath)
    {
        // 1) Copia nueva registrada por la pasada 1 (ruta → Assembly.Load(bytes)).
        if (DataStore.AssembliesByPath.TryGetValue(fullPath, out var registered))
            return registered;

        try
        {
            var shortName = AssemblyName.GetAssemblyName(fullPath).Name;

            // 2) Dedup por nombre simple, SALTANDO los originales refonly (si el mapa
            //    falló por cualquier motivo, nunca devolver el refonly: sería inútil).
            var candidates = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !DataStore.RefOnlyOriginals.Contains(a) && a.GetName().Name == shortName)
                .ToList();

            // 3) Preferir el que cargó desde ESTA misma ruta; si no, el primero válido.
            var byPath = candidates.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Location) &&
                string.Equals(a.Location, fullPath, StringComparison.OrdinalIgnoreCase));
            if (byPath != null)
                return byPath;
            if (candidates.Count > 0)
                return candidates[0];
        }
        catch
        {
            // No se pudo leer el nombre: seguimos a LoadFrom y dejamos que Unity resuelva.
        }

        // 4) Nunca antes cargado: carga clásica desde disco.
        return Assembly.LoadFrom(fullPath);
    }
}