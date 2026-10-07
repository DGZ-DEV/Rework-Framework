using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Rework.Threading;

/// <summary>Pool de hilos trabajadores + colas thread-safe (bloque 13: jobs productor-
/// consumidor, pool configurable, semillas por worker, fallback single-thread).</summary>
public sealed class ReworkJobPool
{
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly AutoResetEvent signal = new(false);
    private volatile bool running = true;
    private readonly ThreadLocal<Random> workerRandom;
    private readonly List<Thread> threads = new();

    /// <summary>Número de hilos trabajadores (0 → fallback single-thread en línea).</summary>
    public int WorkerCount { get; }

    public ReworkJobPool(int workers)
    {
        WorkerCount = workers;
        workerRandom = new ThreadLocal<Random>(() => new Random(Environment.TickCount));
        if (workers <= 0)
            return; // modo single-thread (13.14): Run ejecuta en línea

        for (var i = 0; i < workers; i++)
        {
            var idx = i;
            var t = new Thread(() => WorkerLoop(idx))
            {
                IsBackground = true,
                Name = "ReworkWorker-" + idx,
            };
            t.Start();
            threads.Add(t);
        }
    }

    private void WorkerLoop(int workerIndex)
    {
        // Semilla INDEPENDIENTE por worker (13.7): determinista por índice del worker
        // (mismo pool + mismos jobs → mismos números, 13.6) y sin races de Random.
        workerRandom.Value = new Random(1337 + workerIndex * 1000003);

        while (running)
        {
            signal.WaitOne(250);
            while (queue.TryDequeue(out var job))
            {
                try { job(); }
                catch { /* el error se captura por tarea (ReworkTask.Fail) */ }
            }
        }
    }

    /// <summary>Encola un trabajo y devuelve un handle para esperar su resultado.
    /// Con WorkerCount == 0 ejecuta EN LÍNEA (fallback single-thread, 13.14).</summary>
    public ReworkTask<T> Run<T>(Func<T> fn)
    {
        if (fn == null) throw new ArgumentNullException(nameof(fn));
        var task = new ReworkTask<T>();

        if (WorkerCount <= 0)
        {
            // Fallback single-thread (13.14): ejecutar aquí, sin hilo.
            try { task.Complete(fn()); }
            catch (Exception e) { task.Fail(e); }
            return task;
        }

        queue.Enqueue(() =>
        {
            try { task.Complete(fn()); }
            catch (Exception e) { task.Fail(e); }
        });
        signal.Set();
        return task;
    }

    /// <summary>Random del hilo actual (semilla por worker; seguro para usar dentro de
    /// un job en paralelo, 13.7).</summary>
    public Random CurrentRandom => workerRandom.Value ?? (workerRandom.Value = new Random());

    /// <summary>Detiene los hilos trabajadores. Llamar solo en cierre; en runtime del
    /// juego se deja el pool vivo (hilos background).</summary>
    public void Shutdown()
    {
        running = false;
        signal.Set();
    }
}

/// <summary>Handle del resultado de un trabajo en paralelo (colas de resultados thread-safe).</summary>
public sealed class ReworkTask<T>
{
    private readonly ManualResetEventSlim done = new(false);
    private T result;
    private Exception? error;

    public bool IsCompleted => done.IsSet;
    public bool HasError => error != null;

    /// <summary>Espera (con timeout) a que el trabajo complete. Devuelve el resultado o
    /// relanza la excepción del job.</summary>
    public bool Wait(int timeoutMs) => done.Wait(timeoutMs);

    public T Result
    {
        get
        {
            done.Wait();
            if (error != null)
                throw new InvalidOperationException("ReworkTask falló", error);
            return result;
        }
    }

    internal void Complete(T r)
    {
        result = r;
        done.Set();
    }

    internal void Fail(Exception e)
    {
        error = e;
        done.Set();
    }
}

/// <summary>Sincronización con el hilo principal (13.5): los workers encolan acciones y
/// el hilo principal las ejecuta con Drain() (desde un Update/hook del juego). Las APIs
/// de Unity solo deben tocarse desde el hilo principal.</summary>
public static class ReworkMainThread
{
    private static readonly ConcurrentQueue<Action> actions = new();

    /// <summary>Encola una acción para ejecutarse en el hilo principal (thread-safe).</summary>
    public static void RunOnMain(Action action)
    {
        if (action == null) return;
        actions.Enqueue(action);
    }

    /// <summary>Ejecuta todas las acciones encoladas EN EL HILO QUE LLAMA (debe ser el
    /// principal; llámalo desde un Update o hook del juego).</summary>
    public static void Drain()
    {
        while (actions.TryDequeue(out var a))
        {
            try { a(); }
            catch { /* el hilo principal nunca debe morir por una acción encolada */ }
        }
    }
}

/// <summary>Acceso por defecto al pool de jobs (workers = núcleos - 1, mínimo 2, o 0 si multithreading desactivado).</summary>
public static class ReworkJobs
{
    private static ReworkJobPool? activePool;

    public static ReworkJobPool Pool
    {
        get
        {
            if (activePool == null)
            {
                var workers = ReworkConfig.MultithreadingEnabled ? Math.Max(2, Environment.ProcessorCount - 1) : 0;
                activePool = new ReworkJobPool(workers);
            }
            return activePool;
        }
    }

    public static void ResetPool()
    {
        activePool?.Shutdown();
        var workers = ReworkConfig.MultithreadingEnabled ? Math.Max(2, Environment.ProcessorCount - 1) : 0;
        activePool = new ReworkJobPool(workers);
    }
}