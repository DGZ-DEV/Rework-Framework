using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Rework;

/// <summary>
/// Motor de paralelismo gestionado y seguro para simulaciones y cálculos pesados en segundo plano.
/// Despacha tareas complejas a hilos de trabajo y devuelve resultados sincronizados con el hilo principal.
/// </summary>
public static class ReworkParallel
{
    private static readonly ConcurrentQueue<Action> mainThreadCallbacks = new();

    /// <summary>
    /// Ejecuta un cálculo intensivo en un hilo de fondo y despacha el callback resultante
    /// para ser consumido de forma segura en el hilo principal del juego.
    /// </summary>
    public static void RunBackground<T>(Func<T> computeFunc, Action<T> onCompleteOnMainThread)
    {
        Task.Run(() =>
        {
            try
            {
                T result = computeFunc();
                mainThreadCallbacks.Enqueue(() => onCompleteOnMainThread(result));
            }
            catch { }
        });
    }

    /// <summary>
    /// Procesa los callbacks pendientes en el hilo principal (ej: invocado en cada tick del juego).
    /// </summary>
    public static void FlushMainThreadQueue()
    {
        while (mainThreadCallbacks.TryDequeue(out var action))
        {
            try { action(); } catch { }
        }
    }
}
