using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Modo desarrollador (bloque 17.8/17.9): un archivo `ReworkDev.txt` en la carpeta de un
/// mod limita el framework a aplicar solo los parches/inits/hooks de ese mod o de un
/// parche concreto. Sirve para AISLAR fallos (aplicas uno por uno) y para revertir un
/// parche sin quitarlo del mod.
///
/// Formato (una línea por clave):
///   OnlyPatch=ReworkDemo.ReworkDemoPatch.ParcheMultiplicador
///   OnlyMod=ReworkDemo
/// </summary>
public static class ReworkDevMode
{
    /// <summary>Si no es null, el framework aplica SOLO los parches cuyo nombre completo
    /// coincida (17.8/17.9: aislar/revertir un parche concreto).</summary>
    public static string? OnlyPatch { get; set; }

    /// <summary>Si no es null, el framework aplica SOLO los parches/inits/hooks cuyo
    /// namespace (mod) coincida.</summary>
    public static string? OnlyMod { get; set; }

    /// <summary>¿Se aplica este parche/inits/hooks del mod `modName` con nombre `fullName`?</summary>
    public static bool Filter(string modName, string fullName)
    {
        if (OnlyPatch != null)
            return fullName == OnlyPatch;
        if (OnlyMod != null)
            return modName == OnlyMod;
        return true;
    }

    public static void Reset()
    {
        OnlyPatch = null;
        OnlyMod = null;
    }
}