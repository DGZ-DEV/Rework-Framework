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
        // §48: formato v2 (magic "RW2") — guarda el TIPO de cada valor además de
        // su texto. El formato v1 hacía ToString() al serializar y restauraba
        // strings crudos: Get<int>/Get<float> tras una carga devolvían default
        // (el 'val is T' fallaba con el string en caja). Con el tipo, el round-trip
        // conserva primitivas y enums.
        bw.Write('R'); bw.Write('W'); bw.Write('2');
        bw.Write(store.Count);
        foreach (var kvp in store)
        {
            bw.Write(kvp.Key);
            bw.Write(kvp.Value?.GetType().AssemblyQualifiedName ?? "");
            bw.Write(kvp.Value?.ToString() ?? "");
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
            store.Clear();

            bool formatoV2 = data.Length >= 3 && data[0] == 'R' && data[1] == 'W' && data[2] == '2';
            if (formatoV2)
            {
                br.ReadBytes(3); // magic
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = br.ReadString();
                    string typeName = br.ReadString();
                    string val = br.ReadString();

                    object boxed = val;
                    if (typeName.Length > 0)
                    {
                        var t = System.Type.GetType(typeName);
                        if (t != null)
                        {
                            try { boxed = System.Convert.ChangeType(val, t); }
                            catch { /* tipo no convertible: queda como string */ }
                        }
                    }
                    store[key] = boxed;
                }
            }
            else
            {
                // §48: formato legado (v1, sin tipos): los valores se restauran
                // como string (comportamiento antiguo, al menos no se pierden).
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = br.ReadString();
                    string val = br.ReadString();
                    store[key] = val;
                }
            }
        }
        catch { }
    }
}
