using System;
using System.Text;
using Rework;

namespace Rework.Core;

/// <summary>
/// Utilidad para volcar excepciones con contexto al log de archivo de Rework
/// (Rework.log) de forma legible, además del Player.log. Pensada para depurar
/// contenido real del mod en partida, sin consola ni UI.
/// Equivalente funcional (ligero) del ErrorPrinter de Prepatcher.
/// </summary>
public static class ErrorPrinter
{
    /// <summary>Vuelca contexto + excepción completa (incl. stack) al log de Rework.</summary>
    public static void Print(string context, Exception e)
    {
        var sb = new StringBuilder();
        sb.Append("ErrorPrinter: ").AppendLine(context);
        sb.AppendLine(e.ToString());
        // 17.3/17.7 — sugerencia de solución basada en los traps conocidos (ReworkDiag).
        var sug = ReworkDiag.Suggest(e);
        if (!string.IsNullOrEmpty(sug))
            sb.AppendLine("Sugerencia: " + sug);
        Lg.Error(sb.ToString().TrimEnd());
    }

    /// <summary>Vuelca con el origen runtime explícito (tipo + miembro que falló).</summary>
    public static void Print(string context, Exception e, Type callerType, string member)
        => Print($"{context} (en {callerType.FullName}.{member})", e);

    /// <summary>
    /// Ejecuta una acción capturando cualquier excepción y volcándola con contexto.
    /// Devuelve la excepción (o null) para que el llamador decida si debe propagar.
    /// </summary>
    public static Exception? Try(string context, Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception e)
        {
            Print(context, e);
            return e;
        }
    }
}