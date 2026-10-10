using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Permite capturar y restaurar snapshots compactos de campos [ReworkField] inyectados.
/// Ideal para depuración, comparaciones de estado o sistemas de reversión temporal.
/// </summary>
public static class ReworkStateSnapshot
{
    public class SnapshotData
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> FieldValues { get; set; } = new();
    }

    private static readonly List<SnapshotData> snapshots = new();

    public static SnapshotData Capture(IEnumerable<(string targetId, string fieldName, object? val)> fields)
    {
        var snap = new SnapshotData();
        if (fields != null)
        {
            foreach (var (targetId, fieldName, val) in fields)
            {
                string key = $"{targetId}.{fieldName}";
                snap.FieldValues[key] = val?.ToString() ?? "null";
            }
        }
        snapshots.Add(snap);
        return snap;
    }

    public static IReadOnlyList<SnapshotData> AllSnapshots => snapshots;

    public static void Clear() => snapshots.Clear();

    /// <summary>Serializa los snapshots capturados (usado por ReworkPersistence en el .rwbin).</summary>
    public static byte[] SerializeToBinary()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            var s = snapshots[i];
            bw.Write(s.Timestamp.ToBinary());
            bw.Write(s.FieldValues.Count);
            foreach (var kvp in s.FieldValues)
            {
                bw.Write(kvp.Key);
                bw.Write(kvp.Value);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Restaura los snapshots desde bytes (usado por ReworkPersistence).</summary>
    public static void DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            int count = br.ReadInt32();
            snapshots.Clear();
            for (int i = 0; i < count; i++)
            {
                var s = new SnapshotData { Timestamp = DateTime.FromBinary(br.ReadInt64()) };
                int fieldCount = br.ReadInt32();
                for (int j = 0; j < fieldCount; j++)
                {
                    string key = br.ReadString();
                    string val = br.ReadString();
                    s.FieldValues[key] = val;
                }
                snapshots.Add(s);
            }
        }
        catch { }
    }
}
