using Verse;

namespace Rework.Core;

/// <summary>
/// Controla el "reinicio" del juego de la pasada 1 SIN Harmony, usando solo
/// mecanismos vanilla de RimWorld 1.6.
///
/// CONTEXTO (verificado por descompilación en 1.6.4850):
///   El boot son eventos de LongEventHandler desde Root.Start(). La carga de
///   loads se ejecuta en un HILO DE FONDO (LongEventHandler.eventThread,
///   "LoadAllPlayData" se encola con doAsynchronously:true). El constructor de
///   Mod corre en ese hilo de fondo.
///
///   Si tras el swap dejáramos continuar la cola, el hilo principal ejecutaría el
///   MISMO código original del Assembly-CSharp ahora marcado refonly → "It is
///   illegal to invoke a method on a type loaded using the ReflectionOnly api"
///   (pantalla negra). Por eso hay que
///     1) cancelar el resto del arranque ORIGINAL, y
///     2) encolar (en la cola ORIGINAL) la recreación del componente Root con el
///        tipo del ensamblado NUEVO (RootRecreation.SwapRoot): el nuevo
///        Root_Entry.Start re-ejecuta TODO el boot sobre tipos nuevos
///        (PlayDataLoader.Loaded sigue false → Root.Start re-encola).
///
///   GenScene.GoToMainMenu usa exactamente ClearQueuedEvents + QueueLongEvent.
///
/// IMPORTANTE: este método DEBE llamarse ANTES de Loader.Reload() (antes del swap).
/// Al no existir aún el Assembly-CSharp nuevo, las referencias a Verse.LongEventHandler /
/// Verse.Root que lleva este código se resuelven al ensamblado ORIGINAL, que es el
/// que está drenando el hilo principal. Hecho después del swap se resolverían al
/// LongEventHandler nuevo (cola vacía, inútil).
/// </summary>
internal static class BootControl
{
    /// <summary>
    /// Cancela el resto del arranque original y encola la recreación del Root.
    /// Cuando el hilo de fondo muera (abort de ReworkMod), el hilo principal
    /// completará el evento actual y ejecutará la recreación (hilo principal).
    /// </summary>
    internal static void CancelBootAndScheduleRootSwap()
    {
        // 1) Quitar los eventos largos pendientes: si continuaran (p.ej. el "Misc Init"
        //    que crea UIRoot_Entry y levanta el menú principal), ejecutarían código
        //    original ya refonly → invocaciones ilegales.
        LongEventHandler.ClearQueuedEvents();

        // 2) Limpiar también los callbacks post-evento: al completarse el evento
        //    actual, LongEventHandler los ejecuta; no queremos correr los del boot
        //    original. (Campo privado, expuesto por Krafs.Publicizer.)
        LongEventHandler.toExecuteWhenFinished.Clear();

        // 3) Encolar la recreación del Root con el tipo NUEVO (RootRecreation.SwapRoot).
        //    typeof(Root) se captura COMO VALOR aquí (pasada 1 → Verse.Root ORIGINAL):
        //    el hilo principal lo usará para encontrar el componente viejo; el tipo
        //    nuevo lo resuelve SwapRoot en tiempo de ejecución (ver RootRecreation).
        //    Encadenado a la cola ORIGINAL (estamos antes del swap).
        var oldRootType = typeof(Root);
        LongEventHandler.QueueLongEvent(() => RootRecreation.SwapRoot(oldRootType), null, doAsynchronously: false, null);
    }
}