using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Diagnóstico y sugerencias (bloque 17.3/17.7): un framework no debe solo LOGUEAR un
/// error, sino ayudar a CORREGIRLO. ReworkDiag.Suggest(exception) devuelve una sugerencia
/// heurística basada en los traps ya documentados (ERRORES.md): si el mensaje del error
/// coincide con un caso conocido, se sugiere la solución.
///
/// Los traps conocidos se pueden registrar con RegisterTrap(message, suggestion).
/// </summary>
public static class ReworkDiag
{
    private static readonly List<(string needle, string suggestion)> traps = new()
    {
        ("VTable setup", "No añadas INTERFAZES de un mod a una clase del juego (bloque 4): rompe la vtable en runtime. Alternativa: subclase NUEVA (patrón ReworkWorldCameraDriver)."),
        ("Could not load type", "Verifica el nombre completo del tipo destino (espacio de nombres + clase)."),
        ("Ldarg", "Usa Ldarg con ParameterDefinition o Ldarg_0..3 (TranspilerHelpers). Ldarg con (byte) lanza 'opcode'."),
        ("Starg", "Usa Starg con ParameterDefinition (TranspilerHelpers), no con un int."),
        ("no se pudo inyectar", "El tipo del contexto (parámetro) no coincide con el del hook; revisa el índice del contexto (ctxArg)."),
        ("no se halló", "El nombre del miembro (método/campo) no existe en el tipo destino; usa el nombre real del juego."),
        ("type no soportado", "El tipo del campo no es serializable v1 (primitivas/string/enums/List de esos). Para Dictionary/HashSet usa Serialize=true (ya soportado); para arrays usa List o Scribe manual."),
        ("Could not load type 'Verse.Pawn[]'", "Un tipo de array/lista no se resolvió; revisa el genérico del campo (List`1 vs array)."),
        ("InvalidProgramException", "IL inválido: revisa los saltos y los límites EH de tu transpiler (el framework descarta EH stale con aviso)."),
        ("TypeLoadException", "La clase falló al cargarse: probablemente una interfaz de mod o un cambio de base (bloque 4)."),
    };

    /// <summary>Devuelve la primera sugerencia cuyo 'needle' aparezca en el mensaje del error
    /// (case-insensitive). Si no hay coincidencia, null.</summary>
    public static string? Suggest(Exception? e)
    {
        if (e == null) return null;
        var msg = (e.Message ?? string.Empty);
        foreach (var t in traps)
            if (msg.IndexOf(t.needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return t.suggestion;
        return null;
    }

    /// <summary>Devuelve la lista de traps conocidos (para un diagnóstico completo).</summary>
    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Traps conocidos (sugerencias):");
        foreach (var t in traps)
            sb.AppendLine($"  - '{t.needle}' → {t.suggestion}");
        return sb.ToString();
    }

    /// <summary>Permite registrar un trap propio (un mod puede ampliar el diagnóstico).</summary>
    public static void RegisterTrap(string needle, string suggestion)
    {
        if (!string.IsNullOrEmpty(needle) && !string.IsNullOrEmpty(suggestion))
            traps.Add((needle, suggestion));
    }
}