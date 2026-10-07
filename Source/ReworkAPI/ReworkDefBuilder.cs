using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método estático que devuelve una instancia de cualquier Def (ThingDef, HediffDef, etc.)
/// para ser inyectado automáticamente en DefDatabase en tiempo de inicialización de RimWorld.
/// Elimina al 100% la necesidad de archivos XML para crear contenido del juego.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkDefBuilderAttribute : Attribute
{
}

/// <summary>
/// Motor y registro de Defs dinámicos generados desde código puro.
/// </summary>
public static class ReworkDefBuilder
{
    private static readonly List<MethodInfo> builderMethods = new();
    private static readonly List<object> createdDefs = new();

    public static void RegisterBuilder(MethodInfo method)
    {
        if (method != null && !builderMethods.Contains(method))
            builderMethods.Add(method);
    }

    public static IReadOnlyList<object> CreatedDefs => createdDefs;

    public static void BuildAll(Action<object> defDatabaseAdder)
    {
        createdDefs.Clear();
        for (int i = 0; i < builderMethods.Count; i++)
        {
            try
            {
                var def = builderMethods[i].Invoke(null, null);
                if (def != null)
                {
                    createdDefs.Add(def);
                    defDatabaseAdder?.Invoke(def);
                }
            }
            catch
            {
                // Fallback silencioso en API pública
            }
        }
    }
}
