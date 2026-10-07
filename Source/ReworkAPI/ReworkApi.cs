using System;
using System.Reflection;

namespace Rework;

/// <summary>
/// Detección y versionado de la API (bloque 16.3/16.4/16.5/16.6).
///
/// Cualquier mod puede consultar en RUNTIME si el framework Rework Reforjed está activo
/// y con qué versión, sin romper si no lo está:
///   if (ReworkApi.IsPresent) { ... usar ReworkApi.Version ... }
///   else { ... comportamiento vanilla/alternativo (degradación graceful) ... }
///
/// Cómo funciona: Rework Reforjed lo detecta porque la API está cargada SOLO cuando
/// Rework está activo (0ReworkAPI.dll pertenece al framework; el mod la referencia sin
/// empaquetarla). La versión se lee del ensamblado 0ReworkAPI (AssemblyInformationalVersion).
/// </summary>
public static class ReworkApi
{
    /// <summary>¿El framework Rework Reforjed está presente/activo en este runtime?</summary>
    public static bool IsPresent { get; }

    /// <summary>Versión de la API (16.3), p.ej. "1.0.0" — del AssemblyInformationalVersion.</summary>
    public static string Version { get; }

    /// <summary>Modo de construir: si Rework compila una variante sin la API, esto avisa.
    /// (Sin uso práctico hoy; queda para 'dependencia opcional' avanzada.)</summary>
    public static bool IsFullBuild { get; } = true;

    static ReworkApi()
    {
        try
        {
            var asm = typeof(ReworkApi).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Version = info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            IsPresent = true;
        }
        catch
        {
            Version = "0.0.0";
            IsPresent = false;
        }
    }

    /// <summary>
    /// Helper de degradación graceful (16.6): ejecuta 'withRework' si el framework está
    /// presente, si no ejecuta 'without' (o no hace nada). Así un mod integra APIs de
    /// Rework y sigue funcionando en una lista sin él.
    /// </summary>
    public static void IfPresent(Action withRework, Action? without = null)
    {
        if (IsPresent)
            withRework?.Invoke();
        else
            without?.Invoke();
    }

    /// <summary>Igual que IfPresent pero devolviendo un valor (T por defecto sin framework).</summary>
    public static T IfPresent<T>(Func<T> withRework, T fallback = default!)
    {
        return IsPresent ? withRework() : fallback;
    }
}

/// <summary>Manifiesto de la API (16.1): estado de cada puerta del framework, para que un
/// mod (o un modder humano) sepa qué hay disponible en esta versión. Se lee por reflexión;
/// es informativo (se muestra con ReworkApiManifest.Describe).</summary>
public static class ReworkApiManifest
{
    /// <summary>Nombres de las puertas públicas de la API (atributos + librerías).</summary>
    public static readonly string[] Gates =
    {
        "ReworkField (campos + Default/Initializer/Serialize)",
        "ReworkMethod (métodos añadidos)",
        "ReworkProperty (propiedades añadidas)",
        "ReworkAnnotate (atributos en miembros)",
        "ReworkPatch (parche directo + transpiler)",
        "ReworkInit (código al arranque)",
        "ReworkHook (ciclo de vida, 22 puntos)",
        "TranspilerHelpers (IL amigable)",
        "Rework.Threading (jobs, pool, main-thread)",
        "Rework.Perf (pool de listas, interning, lazy, profiler)",
        "Rework.Dialog (frases por situación/idioma/contexto)",
    };

    /// <summary>Devuelve el manifiesto legible (16.1): versión + puertas.</summary>
    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Rework Reforjed API v{ReworkApi.Version} — presente={ReworkApi.IsPresent}");
        sb.AppendLine("Puertas:");
        foreach (var g in Gates)
            sb.AppendLine("  - " + g);
        return sb.ToString();
    }
}