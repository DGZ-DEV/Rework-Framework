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
}
