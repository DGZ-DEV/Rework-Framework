using System;
using System.Collections.Concurrent;

namespace Rework;

/// <summary>
/// Key-Value Store de persistencia arbitraria por mundo/partida guardada.
/// Permite almacenar datos estructurados o primitivas en el save binario (.rwbin) sin implementar Scribe.
/// </summary>
public static class ReworkWorldStore
{
    private static readonly ConcurrentDictionary<string, object> store = new();

    public static void Set<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (value == null)
            store.TryRemove(key, out _);
        else
            store[key] = value;
    }

    public static T? Get<T>(string key, T? defaultValue = default)
    {
        if (string.IsNullOrEmpty(key)) return defaultValue;
        if (store.TryGetValue(key, out var val) && val is T typedVal)
            return typedVal;
        return defaultValue;
    }

    public static bool Contains(string key) => !string.IsNullOrEmpty(key) && store.ContainsKey(key);

    public static void Clear() => store.Clear();

    public static byte[] SerializeToBinary()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(store.Count);
        foreach (var kvp in store)
        {
            bw.Write(kvp.Key);
            string strVal = kvp.Value?.ToString() ?? "";
            bw.Write(strVal);
        }
        return ms.ToArray();
    }

    public static void DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            int count = br.ReadInt32();
            store.Clear();
            for (int i = 0; i < count; i++)
            {
                string key = br.ReadString();
                string val = br.ReadString();
                store[key] = val;
            }
        }
        catch { }
    }
}
