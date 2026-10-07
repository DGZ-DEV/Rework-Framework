using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rework.Perf;

/// <summary>
/// Pool de objetos reutilizables (bloque 14.5/14.6/14.7): reduce allocations en runtime
/// para cargas de trabajo de mod (ticks, computaciones repetidas). Puro BCL.
/// - ReworkPool&lt;T&gt;: pool de objetos (14.5). Requiere un factory.
/// - ReworkListPool&lt;T&gt;: pool de List&lt;T&gt; (14.6).
/// SEGURIDAD: el objeto devuelto NO debe conservarse tras Return (se reutiliza).
/// </summary>
public static class ReworkPools
{
    // 14.6 — pool de List<T> (el caso más usado en mods).
    private static readonly Dictionary<Type, object> listPools = new();

    public static List<T> NewList<T>()
    {
        var pool = GetListPool<T>();
        return pool.Count > 0 ? pool.Pop() : new List<T>();
    }

    public static void ReturnList<T>(List<T> list)
    {
        if (list == null) return;
        list.Clear();
        GetListPool<T>().Push(list);
    }

    private static ReworkStackPool<T> GetListPool<T>()
    {
        if (!listPools.TryGetValue(typeof(T), out var raw))
        {
            raw = new ReworkStackPool<T>();
            listPools[typeof(T)] = raw;
        }
        return (ReworkStackPool<T>)raw;
    }

    private sealed class ReworkStackPool<T>
    {
        private readonly Stack<List<T>> stack = new();

        public int Count => stack.Count;

        public List<T> Pop()
        {
            lock (stack)
            {
                return stack.Count > 0 ? stack.Pop() : new List<T>();
            }
        }

        public void Push(List<T> item)
        {
            lock (stack)
            {
                // Cap de 64 por tipo para no acumular memoria sin límite.
                if (stack.Count < 64)
                    stack.Push(item);
            }
        }
    }
}

/// <summary>String interning para mods (14.4): internar strings repetidas (nombres de
/// defs, etiquetas) guarda memoria y acelera las comparaciones. Puro BCL.</summary>
public static class ReworkStringIntern
{
    private static readonly Dictionary<string, string> interned = new(StringComparer.Ordinal);

    /// <summary>Devuelve la instancia canónica del string (o la agrega si es nueva).</summary>
    public static string Intern(string value)
    {
        if (value == null) return null;
        if (interned.TryGetValue(value, out var existing))
            return existing;
        lock (interned)
        {
            if (interned.TryGetValue(value, out existing))
                return existing;
            interned[value] = value;
            return value;
        }
    }

    public static int InternedCount => interned.Count;
}

/// <summary>Lazy initialization (14.9): envoltorio que construye el valor la primera vez
/// que se pide (thread-safe con Lazy&lt;T&gt; del BCL).</summary>
public sealed class ReworkLazy<T> where T : class
{
    private readonly System.Lazy<T> lazy;

    public ReworkLazy(Func<T> factory) => lazy = new System.Lazy<T>(factory);

    public T Value => lazy.Value;
    public bool IsValueCreated => lazy.IsValueCreated;
}

/// <summary>Perfilado integrado (14.12): cronometra fases/bloques y acumula tiempos para
/// mostrarlos en el Rework.log. Pensado para medir el coste de tus parches/ticks.</summary>
public static class ReworkProfiler
{
    private static readonly Dictionary<string, Stopwatch> active = new();
    private static readonly Dictionary<string, (long totalMs, int count)> totals = new();
    private static readonly object gate = new();

    /// <summary>Arranca un cronómetro por nombre (reinicia si ya corría).</summary>
    public static void Begin(string name)
    {
        lock (gate)
        {
            active[name] = Stopwatch.StartNew();
        }
    }

    /// <summary>Detiene y acumula el tiempo de la fase nombrada.</summary>
    public static void End(string name)
    {
        lock (gate)
        {
            if (!active.TryGetValue(name, out var sw))
                return;
            sw.Stop();
            active.Remove(name);
            var t = totals.TryGetValue(name, out var ex) ? ex : (totalMs: 0L, count: 0);
            totals[name] = (t.totalMs + sw.ElapsedMilliseconds, t.count + 1);
        }
    }

    /// <summary>Resumen legible de todas las fases medidas (para imprimir en el log).</summary>
    public static string Summary()
    {
        lock (gate)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in totals)
                sb.Append($"{kvp.Key}={kvp.Value.totalMs}ms×{kvp.Value.count} ");
            return sb.ToString().Trim();
        }
    }

    /// <summary>Limpia acumulados (p.ej. cada N ticks).</summary>
    public static void Reset()
    {
        lock (gate)
        {
            totals.Clear();
            active.Clear();
        }
    }
}