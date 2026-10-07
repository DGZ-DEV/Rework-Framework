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
}
