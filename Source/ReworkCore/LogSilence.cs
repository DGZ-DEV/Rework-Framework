using System;
using Rework.Data;
using UnityEngine;

namespace Rework.Core;

/// <summary>
/// Silencia el ruido del reinicio interno filtrando el SINK de logs de Unity
/// (Debug.unityLogger.logHandler) únicamente durante la ventana del reinicio
/// (DataStore.suppressLogs). Se instala en la pasada 1, ANTES de marcar refonly, y
/// una vez fuera de la ventana actúa como passthrough inofensivo (nunca se desinstala,
/// porque en la pasada 2 el estático de ReworkCore es NUEVO y no podría restaurar el
/// handler original).
///
/// NO parchea IL de ningún método del juego: solo envuelve el manejador de logs de
/// Unity (equivalente al SilenceLogging de Prepatcher, pero sin tocar
/// métodos del juego). La decisión de suprimir vive en DataStore.ShouldSuppressLog
/// (0ReworkData, BCL y nunca refonly) para que el filtro funcione a través del reload.
/// </summary>
public static class LogSilence
{
    /// <summary>Instala el filtro (idempotente). Llámese en la pasada 1 antes del reload.</summary>
    public static void Install()
    {
        var logger = Debug.unityLogger;
        if (logger.logHandler is FilterHandler)
            return;

        logger.logHandler = new FilterHandler(logger.logHandler);
        // Calentamos JIT con un mensaje propio: asegura que los métodos del filtro
        // (definidos en ReworkCore) queden compilados ANTES de que la pasada 1 marque
        // ReworkCore como refonly (si no, el primer log real del frame de transición
        // intentaría compilar sobre un ensamblado refonly y fallaría).
        Debug.Log("Rework: filtro de logs del reinicio instalado");
    }

    /// <summary>true si el filtro está instalado (para diagnóstico).</summary>
    public static bool IsInstalled => Debug.unityLogger.logHandler is FilterHandler;

    private sealed class FilterHandler : ILogHandler
    {
        private readonly ILogHandler next;

        public FilterHandler(ILogHandler next)
        {
            this.next = next;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            var msg = format;
            try
            {
                if (args != null && args.Length > 0)
                    msg = string.Format(format, args);
            }
            catch (Exception)
            {
                // formato inválido: usamos el raw
            }

            if (DataStore.ShouldSuppressLog(msg))
                return;

            next?.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (DataStore.ShouldSuppressLog(exception?.ToString()))
                return;
            next?.LogException(exception, context);
        }
    }
}