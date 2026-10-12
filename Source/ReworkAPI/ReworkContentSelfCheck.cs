using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Sistema genérico de auto-verificación de contenido (§55-self-check).
///
/// Extiende el "anti-fantasma" que hoy sólo comprueba 'registrado = 1' (el Def
/// está en DefDatabase) al siguiente escalón: 'registrado y aparece/funciona'
/// (el Def está en DefDatabase Y tiene sus companion defs / propiedades críticas
/// configuradas). Esto habría detectado el §41 (WorkTypeDef visible sin columna
/// en la pestaña de Trabajo) antes de que el usuario lo notara en partida.
///
/// El registry es genérico y Def-agnostic (usa object en la API pública) para que
/// viva en 0ReworkAPI sin dependencias del juego. Las comprobaciones concretas
/// (que necesitan tipos de RimWorld) se registran desde ReworkCore/ReworkMod via
/// <see cref="Register"/> o <see cref="RegisterDef"/>.
/// </summary>
public static class ReworkContentSelfCheck
{
    /// <summary>Resultado de una comprobación individual.</summary>
    public class CheckResult
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Registered { get; set; }
        public bool Usable { get; set; }
        public string? Detail { get; set; }
    }

    /// <summary>Una función que produce resultados de comprobación.</summary>
    public delegate IEnumerable<CheckResult> CheckDelegate();

    /// <summary>Registro de un Def individual para verificación.</summary>
    public class DefEntry
    {
        public string DefName { get; set; } = "";
        public string DefType { get; set; } = "";
        public bool IsRegistered { get; set; }
        public bool IsUsable { get; set; } = true;
        public string? Detail { get; set; }
    }

    private static readonly List<CheckDelegate> checks = new();
    private static readonly List<DefEntry> trackedDefs = new();

    /// <summary>
    /// Registra una función de comprobación que produce resultados.
    /// Se llama desde código de framework (ReworkCore/ReworkMod) para añadir
    /// comprobaciones de usabilidad específicas.
    /// </summary>
    public static void Register(CheckDelegate check)
    {
        if (check == null) return;
        checks.Add(check);
    }

    /// <summary>
    /// Registra un Def individual para verificación. Se llama desde los registries
    /// de contenido justo después de añadir el Def a DefDatabase.
    /// </summary>
    public static void RegisterDef(string defName, string defType, bool isRegistered)
    {
        trackedDefs.Add(new DefEntry
        {
            DefName = defName,
            DefType = defType,
            IsRegistered = isRegistered,
            IsUsable = isRegistered
        });
    }

    /// <summary>Número total de Defs individuales registrados para verificación.</summary>
    public static int TrackedCount => trackedDefs.Count;

    /// <summary>
    /// Ejecuta todas las comprobaciones registradas y devuelve los resultados.
    /// Se debe llamar DESPUÉS de que todos los [StaticConstructorOnStartup] hayan
    /// terminado (usar LongEventHandler.ExecuteWhenFinished para ello).
    /// </summary>
    public static CheckResult[] RunAll()
    {
        var results = new List<CheckResult>();

        // 1) Defs individualmente registrados
        foreach (var def in trackedDefs)
        {
            results.Add(new CheckResult
            {
                Name = def.DefName,
                Category = def.DefType,
                Registered = def.IsRegistered,
                Usable = def.IsUsable,
                Detail = def.Detail
            });
        }

        // 2) Comprobaciones funciones registradas
        for (int i = 0; i < checks.Count; i++)
        {
            try
            {
                foreach (var r in checks[i]())
                    results.Add(r);
            }
            catch (Exception e)
            {
                results.Add(new CheckResult
                {
                    Name = $"Check[{i}]",
                    Registered = false,
                    Usable = false,
                    Detail = $"Comprobación lanzó: {e.Message}"
                });
            }
        }

        return results.ToArray();
    }

    /// <summary>¿Todos los Defs registrados pasaron la verificación?</summary>
    public static bool AllPassed()
    {
        if (trackedDefs.Count == 0 && checks.Count == 0) return true;
        var results = RunAll();
        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i].Registered || !results[i].Usable) return false;
        }
        return true;
    }
}
