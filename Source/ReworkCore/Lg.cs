using System;
using System.IO;
using Rework;
using Rework.Data;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Logging de Rework. Durante la pasada 1 (DataStore.startedOnce == false) los
/// mensajes se bufferizan en DataStore.LogsToPass porque el abort del hilo
/// destruye el flujo normal del log; la pasada 2 los vacía.
/// (Lección del fork jikulopo: Source/Implementation/Lg.cs + DataStore.logsToPass.)
/// </summary>
internal static class Lg
{
    /// <summary>UTF-8 CON BOM: Windows (Notepad, consola) lo detecta bien y no rompe los
    /// acentos/ñ/→ del log.</summary>
    private static readonly System.Text.Encoding Utf8Bom = new System.Text.UTF8Encoding(true);

    internal static void Info(object msg) => Write("info", msg);
    internal static void Error(object msg) => Write("error", msg);
    internal static void Verbose(object msg)
    {
        if (ReworkConfig.VerboseLogs)
            Write("verbose", msg);
    }

    /// <summary>17.2/17.12 — cada error también se acumula en ReworkHealth para que la
    /// pasada 2 muestre un resumen global al usuario (sin romper el boot).</summary>
    internal static void RecordHealthFailure(string component, string message)
    {
        ReworkHealth.RecordFailure(component, message);
    }

    private static readonly object fileLock = new();

    private static void Write(string kind, object msg)
    {
        var text = msg.ToString();

        // 17.2/17.12 — acumular los fallos en ReworkHealth para que la pasada 2 muestre
        // un resumen global al usuario (recuperación parcial: el boot siguió).
        if (kind == "error")
            ReworkHealth.RecordFailure(HeuristicComponent(text), text);

        if (DataStore.startedOnce)
        {
            if (kind == "error")
                Log.Error($"Rework Reforjed: {text}");
            else
                Log.Message($"Rework Reforjed: {text}");
        }
        else
        {
            DataStore.LogsToPass.Add((kind, text));

            // Traza a disco de la pasada 1: si la pasada 2 falla (y no se pueden vaciar
            // los logs bufferizados), esto permite ver qué parches se aplicaron.
            try
            {
                File.AppendAllText(DebugPath, $"[{kind}] {text}{Environment.NewLine}", Utf8Bom);
            }
            catch
            {
                // Nunca dejar que el log rompa el flujo del reload.
            }
        }

        // Espejo a archivo propio (Rework.log): el Player.log es ruidoso y la UI no
        // muestra nada de lo que pasa en partida; este archivo es la fuente limpia
        // para depurar contenido real del mod. Se escribe SIEMPRE (pasada 1 y 2).
        AppendToFile(kind, text);
    }

    /// <summary>17.2 — componente heurístico del texto del error (para el resumen de salud).</summary>
    private static string HeuristicComponent(string text)
    {
        if (string.IsNullOrEmpty(text)) return "Rework";
        if (text.Contains("Parche libre") || text.Contains("Transpiler")) return "FreePatcher";
        if (text.Contains("Hook")) return "LifecycleHooks";
        if (text.Contains("Campo") || text.Contains("accessor")) return "FieldAdder";
        if (text.Contains("Serialización") || text.Contains("serializar")) return "FieldScribe";
        if (text.Contains("ReworkInterface")) return "InterfaceInjector";
        if (text.Contains("ReworkAnnotate")) return "AnnotateInjector";
        if (text.Contains("ReworkMethod") || text.Contains("ReworkProperty")) return "MethodInjector";
        if (text.Contains("ReworkRedirect")) return "CallSiteRedirector";
        if (text.Contains("ReworkOverride")) return "OverrideInjector";
        if (text.Contains("ReworkUnlock")) return "UnlockProcessor";
        if (text.Contains("ReworkInline")) return "InlineProcessor";
        if (text.Contains("ReworkConst")) return "ConstRewriter";
        if (text.Contains("Init")) return "InitRunner";
        if (text.Contains("Entorno") || text.Contains("layout")) return "RuntimeEnvironment";
        return "Rework";
    }

    /// <summary>Ruta del log de archivo dedicado de Rework (LocalLow, junto a Player.log).</summary>
    internal static string LogFilePath => Path.Combine(Application.persistentDataPath, "Rework.log");

    /// <summary>Ruta de la traza de la pasada 1 (LocalLow, misma zona que Player.log).</summary>
    internal static string DebugPath => Path.Combine(Application.persistentDataPath, "Rework pasada1.log");

    /// <summary>Escribe al log de archivo con cabecera de sesión única y marca de tiempo.</summary>
    internal static void AppendToFile(string kind, string text)
    {
        try
        {
            lock (fileLock)
            {
                if (!DataStore.ReworkLogSessionOpened)
                {
                    File.AppendAllText(LogFilePath, $"{Environment.NewLine}===== Rework Reforjed — sesión " +
                                                   $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} =====" + Environment.NewLine, Utf8Bom);
                    DataStore.ReworkLogSessionOpened = true;
                }
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}][{kind}] {text}{Environment.NewLine}", Utf8Bom);
            }
        }
        catch
        {
            // El log de archivo nunca debe romper el flujo ni el boot.
        }
    }

    /// <summary>La pasada 2 muestra los logs bufferizados de la pasada 1.</summary>
    internal static void FlushBuffered()
    {
        foreach (var (kind, message) in DataStore.LogsToPass)
        {
            if (kind == "error")
                Log.Error($"Rework Reforjed (antes del reload): {message}");
            else
                Log.Message($"Rework Reforjed (antes del reload): {message}");
        }
        DataStore.LogsToPass.Clear();
    }
}
