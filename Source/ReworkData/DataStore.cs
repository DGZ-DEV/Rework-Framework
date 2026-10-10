using System.Collections.Generic;
using System.Reflection;

namespace Rework.Data;

/// <summary>
/// Ensamblado SIN dependencias (solo BCL): nunca se recarga, por lo que sirve
/// para pasar estado a través de la "barrera del reload".
/// Equivalente a Prepatcher Source/DataAssembly/DataStore.cs (Zetrith):
/// "This assembly doesn't have any dependencies (apart from system ones)
///  so won't get reloaded and can be used to pass data across the reloading barrier".
/// </summary>
public static class DataStore
{
    /// <summary>
    /// true cuando la pasada 1 (rewrite + reload) terminó. La pasada 2 lo usa
    /// para saber que NO debe volver a reescribir nada.
    /// </summary>
    public static bool startedOnce;

    /// <summary>
    /// true durante la ventana del reinicio interno (justo antes del abort de la pasada 1
    /// hasta que la pasada 2 termina de verificar). Verse.Log.Error → ShouldSuppressLog
    /// silencia SOLO el ruido conocido de esa ventana (ThreadAbortException + NREs del
    /// frame de transición). Fuera de esta ventana nunca se suprime, así no se esconden
    /// errores reales. Equivalente al SilenceLogging de Prepatcher.
    /// </summary>
    public static bool suppressLogs;

    /// <summary>
    /// Decide si un mensaje de log es "ruido del reinicio" (ThreadAbortException de la
    /// pasada 1 o NREs del frame de transición), y además estamos en la ventana
    /// (suppressLogs). Vive aquí (BCL, sin dependencias de Unity/juego) para que tanto
    /// la pasada 1 (original) como la pasada 2 (nuevo) y el filtro de logs (que apunta a
    /// 0ReworkData, que nunca se hace refonly) puedan consultarlo de forma segura.
    /// </summary>
    public static bool ShouldSuppressLog(string? msg)
    {
        if (!suppressLogs || string.IsNullOrEmpty(msg))
            return false;
        return msg.IndexOf("ThreadAbortException", System.StringComparison.Ordinal) >= 0
            || msg.IndexOf("Root level exception in Update()", System.StringComparison.Ordinal) >= 0
            || msg.IndexOf("Exception was thrown while trying to handle exception", System.StringComparison.Ordinal) >= 0
            // Ruido de la música de entrada durante la transición del reinicio:
            || msg.IndexOf("uninitialized DefOf of type SongDefOf", System.StringComparison.Ordinal) >= 0
            || msg.IndexOf("did StartPlaying but there is already a music source", System.StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Buffer de logs de la pasada 1. El abort del hilo (Thread.CurrentThread.Abort)
    /// destruye la pila de llamadas en mitad del arranque; sin buffer, los logs de
    /// la pasada 1 se pierden. Lección tomada del fork jikulopo (Lg.cs + logsToPass).
    /// La pasada 2 los vacía con prefijo "(antes del reload)".
    /// </summary>
    public static readonly List<(string kind, string message)> LogsToPass = new();

    /// <summary>
    /// Ruta de disco → ensamblado NUEVO recargado por REWORK (Assembly.Load(bytes)).
    /// Lo usa ReworkLoader.LoadFile dentro del ModAssemblyHandler.ReloadAll patcheado
    /// para que la pasada 2 (re-arranque del boot) vuelva a cargar las copias NUEVAS
    /// de Rework.dll / ReworkCore.dll, y NO los originales marcados refonly (los cuales
    /// el buscador interno de Mono saltaría por el nombre duplicado).
    /// (Equivalente a DataStore.duplicateAssemblies del fork jikulopo.)
    /// Comparación de rutas insensible a mayúsculas: la pasada 1 registra con
    /// Assembly.Location y la pasada 2 consulta con item.FullName; Windows no distingue.
    /// </summary>
    public static readonly Dictionary<string, Assembly> AssembliesByPath =
        new Dictionary<string, Assembly>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensamblados ORIGINALES que la pasada 1 marcó como reflection-only.
    /// Se registra aquí (no en un estático de ReworkCore) porque la pasada 2 corre
    /// con el ReworkCore NUEVO, cuyos estáticos están frescos y vacíos; DataStore
    /// (0ReworkData, sin dependencias) nunca se recarga y sobrevive la barrera.
    /// Lo consumen: ReworkLoader.LoadFile (saltar refonly al deduplicar por nombre) y
    /// RuntimeHooks.WrapActiveAssemblies (filtro de GenTypes).
    /// </summary>
    public static readonly HashSet<Assembly> RefOnlyOriginals = new();

    /// <summary>
    /// true cuando ya se escribió la cabecera de sesión en el log de archivo
    /// (Rework.log). Vive aquí para que la sesión se abra UNA sola vez por arranque,
    /// aunque la pasada 2 corra con un ReworkCore nuevo (cuyos estáticos están frescos).
    /// </summary>
    public static bool ReworkLogSessionOpened;

    /// <summary>true cuando los [ReworkInit] ya se ejecutaron (pasada 2, una vez).</summary>
    public static bool InitRan;

    /// <summary>Registro de telemetría de campos reales inyectados por Rework.</summary>
    public static readonly List<string> InjectedFields = new();

    /// <summary>Registro de telemetría de parches estructurales/hooks aplicados.</summary>
    public static readonly List<string> AppliedPatches = new();
}
