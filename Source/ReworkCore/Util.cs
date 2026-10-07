using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Rework.Core;

/// <summary>Utilidades generales (equivalente a Prepatcher Source/Implementation/Util.cs).</summary>
internal static class Util
{
    /// <summary>
    /// Carpeta Managed según plataforma. En Windows/Linux: "Managed" (relativa a
    /// Application.dataPath). En macOS: "Resources/Data/Managed".
    /// (Prepatcher Util.ManagedFolderOS.)
    /// </summary>
    public static string ManagedFolderOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "Resources/Data/Managed";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Managed";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Managed";
        throw new NotSupportedException("Plataforma desconocida");
    }

    /// <summary>
    /// Recorrido BFS genérico desde un conjunto de inicio siguiendo la función next.
    /// (Prepatcher Util.BFS, usado por Reloader.PropagateNeedsReload.)
    /// </summary>
    public static IEnumerable<T> BFS<T>(IEnumerable<T> start, Func<T, IEnumerable<T>> next)
    {
        var result = new HashSet<T>();
        var todo = new Queue<T>();

        foreach (var o in start)
            todo.Enqueue(o);

        while (todo.Count > 0)
        {
            var t = todo.Dequeue();
            if (!result.Add(t))
                continue;

            foreach (var d in next(t))
                if (!result.Contains(d))
                    todo.Enqueue(d);
        }

        return result;
    }
}
