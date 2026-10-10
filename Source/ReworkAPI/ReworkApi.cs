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
    /// <summary>¿El framework Rework Reforjed está presente/activo en este runtime?
    ///
    /// §48 — HONESTIDAD: si Rework NO está instalado, esta propiedad no puede
    /// devolverte false: sin 0ReworkAPI.dll cargada, el JIT del acceso al tipo
    /// lanza FileNotFoundException ANTES de ejecutar el ctor estático. Para
    /// degradación real, envuelve el acceso en try/catch (o usa reflexión) y
    /// trata la excepción como "ausente". IsPresent==false solo ocurre si la
    /// API está cargada pero falla su inicialización.</summary>
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

    /// <summary>
    /// Compara versiones semver (major.minor[.patch[.revision]]).
    /// Devuelve negativo si 'a' &lt; 'b', cero si son iguales, positivo si 'a' &gt; 'b'.
    /// </summary>
    private static int CompareVersions(string a, string b)
    {
        return ParseVersion(a).CompareTo(ParseVersion(b));
    }

    /// <summary>
    /// Analiza una cadena de versión (p. ej. "1.2.0" o "1.2.0-beta+build")
    /// en un System.Version, ignorando sufijos pre-release y metadata.
    /// </summary>
    private static Version ParseVersion(string raw)
    {
        string clean = raw ?? "0.0.0";
        // Strip semver suffixes (pre-release, build metadata)
        int dash = clean.IndexOf('-');
        if (dash >= 0) clean = clean.Substring(0, dash);
        int plus = clean.IndexOf('+');
        if (plus >= 0) clean = clean.Substring(0, plus);
        clean = clean.Trim();
        // Ensure major.minor format
        var parts = clean.Split('.');
        if (parts.Length < 2) clean += ".0";
        return new Version(clean);
    }

    /// <summary>
    /// ¿La API cargada es igual o superior a la versión mínima requerida?
    /// Usa comparación semver. Devuelve false si la API no está presente.
    /// </summary>
    /// <example>
    /// if (ReworkApi.VersionAtLeast("1.2.0")) { /* característica disponible */ }
    /// </example>
    public static bool VersionAtLeast(string minVersion)
    {
        if (!IsPresent) return false;
        try { return CompareVersions(Version, minVersion) >= 0; }
        catch { return string.Compare(Version, minVersion, StringComparison.Ordinal) >= 0; }
    }
}

/// <summary>
/// Declara la versión mínima de la API de Rework (§43-version-contract) que un
/// ensamblado, clase o método requiere. Se valida en el escáner de atributos:
/// si la API cargada es anterior a la versión requerida, se muestra un error
/// claro indicando qué versión se necesita y cuál está cargada.
///
/// Uso (nivel de ensamblado):
///   [assembly: ReworkMinVersion("1.2.0")]
///
/// Uso (nivel de clase):
///   [ReworkMinVersion("1.3.0")]
///   public class MiFeature { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method,
                Inherited = false, AllowMultiple = false)]
public sealed class ReworkMinVersionAttribute : Attribute
{
    /// <summary>Versión mínima requerida (semver: major.minor.patch).</summary>
    public string MinVersion { get; }

    public ReworkMinVersionAttribute(string minVersion)
    {
        MinVersion = minVersion;
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
        "ReworkUnlock (visibilidad: public/unsealed/virtual reales)",
        "ReworkOverride (overrides virtuales en clases del juego)",
        "ReworkConst (plegado de static readonly a literales)",
        "ReworkRedirect (reescritura global de call-sites)",
        "ReworkInline (inline de getters triviales)",
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