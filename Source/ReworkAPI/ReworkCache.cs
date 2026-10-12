using System;
using System.Collections.Concurrent;

namespace Rework;

/// <summary>
/// Capa de Caché con Time-to-Live (TTL en ticks) para optimizar cálculos costosos en bucles de juego.
/// </summary>
public static class ReworkCache
{
    private class CacheEntry
    {
        public object Value = null!;
        public int ExpireTick;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> cache = new();

    public static T GetOrCompute<T>(string key, int currentTick, int ttlTicks, Func<T> computeFunc)
    {
        if (cache.TryGetValue(key, out var entry) && currentTick < entry.ExpireTick)
        {
            return (T)entry.Value;
        }

        T freshValue = computeFunc();
        cache[key] = new CacheEntry
        {
            Value = freshValue!,
            ExpireTick = currentTick + ttlTicks
        };
        return freshValue;
    }

    public static void Invalidate(string key) => cache.TryRemove(key, out _);

    public static void Clear() => cache.Clear();
}
