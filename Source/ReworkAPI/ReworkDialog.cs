using System;
using System.Collections.Generic;
using System.Text;

namespace Rework.Dialog;

/// <summary>
/// Banco de frases por situación (bloque 15.1–15.9): infraestructura de diálogo/narrativa
/// pura (BCL) para mods.
///   - Frases por situación + por idioma (15.1/15.7).
///   - Carga desde texto simple "situación|frase" (15.2; un mod puede leerlo de un
///     archivo de texto del contenido; JSON/XML los aporta el mod con su librería).
///   - Consulta O(1) con Dictionary (15.4/15.6) + interning reutilizando
///     Rework.Perf.ReworkStringIntern (15.5).
///   - Prioridades entre mods (15.8): la PRIMERA frase registrada para una situación
///     gana; Add no pisa (igual que los campos [ReworkField]).
///   - Contexto dinámico (15.9): FormatPhrase sustituye {etiquetas} (nombre, rasgo…)
///     con el contexto que el llamador pasa.
/// </summary>
public static class ReworkDialog
{
    /// <summary>Clave de situación + idioma → frase (la primera gana, 15.8).</summary>
    private static readonly Dictionary<string, string> phrases = new(StringComparer.Ordinal);

    private static string Key(string situation, string lang)
        => situation + "\u0001" + lang;

    /// <summary>Registra una frase para una situación e idioma. La PRIMERA gana
    /// (prioridades entre mods: el mod que carga antes decide).</summary>
    public static void AddPhrase(string situation, string lang, string phrase)
    {
        if (situation == null || lang == null || phrase == null) return;
        // Internar la frase (15.5): la misma cadena en memoria se comparte.
        var key = Rework.Perf.ReworkStringIntern.Intern(Key(situation, lang));
        if (!phrases.ContainsKey(key))
            phrases[key] = Rework.Perf.ReworkStringIntern.Intern(phrase);
    }

    /// <summary>Registra varias frases del formato líneas "situación|frase"
    /// (15.2 — el mod puede cargar esto desde un archivo de texto de su contenido).</summary>
    public static void AddPhrases(string lang, string multiline)
    {
        if (multiline == null) return;
        foreach (var line in multiline.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            var sep = t.IndexOf('|');
            if (sep <= 0) continue;
            AddPhrase(t.Substring(0, sep).Trim(), lang, t.Substring(sep + 1).Trim());
        }
    }

    /// <summary>Consulta O(1) (15.4/15.6): frase de la situación en el idioma actual, con
    /// fallback al idioma por defecto ("es" o la primera registrada). Devuelve null si no
    /// hay frase.</summary>
    public static string? GetPhrase(string situation, string lang, string defaultLang = "es")
    {
        if (phrases.TryGetValue(Key(situation, lang), out var p))
            return p;
        if (lang != defaultLang && phrases.TryGetValue(Key(situation, defaultLang), out var d))
            return d;
        // Fallback último: cualquier idioma de esa situación.
        var prefix = situation + "\u0001";
        foreach (var kvp in phrases)
            if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                return kvp.Value;
        return null;
    }

    /// <summary>Contexto dinámico (15.9): sustituye {etiqueta} por el valor del diccionario
    /// de contexto (p.ej. {nombre}, {rasgo}). Ej.: "Hola, {nombre}." con ctx["nombre"]="DGZ".</summary>
    public static string FormatPhrase(string? phrase, IReadOnlyDictionary<string, string>? ctx)
    {
        if (phrase == null) return string.Empty;
        if (ctx == null || ctx.Count == 0) return phrase;
        var sb = new StringBuilder(phrase);
        foreach (var kvp in ctx)
            sb.Replace("{" + kvp.Key + "}", kvp.Value);
        return sb.ToString();
    }

    /// <summary>Nº de frases registradas (para debug).</summary>
    public static int PhraseCount => phrases.Count;

    /// <summary>Cantidad de situaciones distintas (para reports).</summary>
    public static int SituationCount
    {
        get
        {
            var set = new HashSet<string>();
            foreach (var k in phrases.Keys)
                set.Add(k.Split('\u0001')[0]);
            return set.Count;
        }
    }
}