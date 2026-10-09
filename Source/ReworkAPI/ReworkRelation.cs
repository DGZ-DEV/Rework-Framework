using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Sistema declarativo de relaciones personalizadas entre colonos (Mentor, Deuda, Rival Histórico, etc.).
/// Persistido y gestionado sin necesidad de modificar Defs de relaciones vanilla.
/// </summary>
public static class ReworkRelation
{
    private class RelationEntry
    {
        public string TargetPawnId = "";
        public string RelationType = "";
    }

    private static readonly Dictionary<string, List<RelationEntry>> relations = new();

    public static void SetRelation(string pawnAId, string pawnBId, string relationType)
    {
        if (string.IsNullOrEmpty(pawnAId) || string.IsNullOrEmpty(pawnBId) || string.IsNullOrEmpty(relationType)) return;

        if (!relations.TryGetValue(pawnAId, out var list))
        {
            list = new List<RelationEntry>();
            relations[pawnAId] = list;
        }

        list.RemoveAll(r => r.TargetPawnId == pawnBId && r.RelationType == relationType);
        list.Add(new RelationEntry { TargetPawnId = pawnBId, RelationType = relationType });
    }

    public static bool HasRelation(string pawnAId, string pawnBId, string relationType)
    {
        if (relations.TryGetValue(pawnAId, out var list))
        {
            return list.Exists(r => r.TargetPawnId == pawnBId && r.RelationType == relationType);
        }
        return false;
    }

    public static List<string> GetRelated(string pawnAId, string relationType)
    {
        var res = new List<string>();
        if (relations.TryGetValue(pawnAId, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].RelationType, relationType, StringComparison.OrdinalIgnoreCase))
                    res.Add(list[i].TargetPawnId);
            }
        }
        return res;
    }

    /// <summary>Serializa todas las relaciones (usado por ReworkPersistence en el .rwbin).</summary>
    public static byte[] SerializeToBinary()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(relations.Count);
        foreach (var kvp in relations)
        {
            bw.Write(kvp.Key);
            bw.Write(kvp.Value.Count);
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                bw.Write(kvp.Value[i].TargetPawnId);
                bw.Write(kvp.Value[i].RelationType);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Restaura las relaciones desde bytes (usado por ReworkPersistence).</summary>
    public static void DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            int count = br.ReadInt32();
            relations.Clear();
            for (int i = 0; i < count; i++)
            {
                string pawnA = br.ReadString();
                int relCount = br.ReadInt32();
                var list = new List<RelationEntry>();
                for (int j = 0; j < relCount; j++)
                {
                    string target = br.ReadString();
                    string type = br.ReadString();
                    list.Add(new RelationEntry { TargetPawnId = target, RelationType = type });
                }
                relations[pawnA] = list;
            }
        }
        catch { }
    }
}
