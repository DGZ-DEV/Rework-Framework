using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método estático como callback de compatibilidad condicional con un mod externo.
/// Solo se ejecutará si el PackageId especificado está presente en la lista de mods activos.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkCompatWithAttribute : Attribute
{
    public string PackageId { get; }

    public ReworkCompatWithAttribute(string packageId)
    {
        PackageId = packageId;
    }
}

/// <summary>
/// Gestor de compatibilidad condicional desacoplada de Rework.
/// </summary>
public static class ReworkCompat
{
    private static readonly List<(string packageId, MethodInfo method)> compatHooks = new();

    public static void RegisterCompatHook(string packageId, MethodInfo method)
    {
        compatHooks.Add((packageId, method));
    }

    public static void RunHooksForActiveMods(Func<string, bool> isModActiveChecker)
    {
        for (int i = 0; i < compatHooks.Count; i++)
        {
            var (packageId, method) = compatHooks[i];
            if (isModActiveChecker(packageId))
            {
                try
                {
                    method.Invoke(null, null);
                }
                catch { }
            }
        }
    }
}
