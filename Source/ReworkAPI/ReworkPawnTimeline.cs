using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Evento o hito en la vida de un colono (primer disparo, crisis superada, pérdida de miembro, etc.).
/// </summary>
public class TimelineEvent
{
    public int Tick { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    public TimelineEvent(int tick, string title, string description)
    {
        Tick = tick;
        Title = title;
        Description = description;
    }
}

/// <summary>
/// Historial de vida persistente por Pawn.
/// Almacena una secuencia temporal de hitos sin sobrecargar el objeto Pawn nativo.
/// Totalmente serializable a binario (.rwbin).
/// </summary>
public static class ReworkPawnTimeline
{
    private static readonly Dictionary<string, List<TimelineEvent>> timelines = new();

    /// <summary>
    /// Añade un hito histórico a la línea de tiempo de un pawn (identificado por ID o ThingID).
    /// </summary>
    public static void RecordEvent(string pawnId, int tick, string title, string description)
    {
        if (string.IsNullOrEmpty(pawnId)) return;

        if (!timelines.TryGetValue(pawnId, out var list))
        {
            list = new List<TimelineEvent>();
            timelines[pawnId] = list;
        }

        list.Add(new TimelineEvent(tick, title, description));
    }

    /// <summary>
    /// Obtiene la lista de hitos de un pawn.
    /// </summary>
    public static IReadOnlyList<TimelineEvent> GetTimeline(string pawnId)
    {
        if (timelines.TryGetValue(pawnId, out var list))
            return list;
        return Array.Empty<TimelineEvent>();
    }

    /// <summary>
    /// Serializa los datos del timeline a bytes binarios.
    /// </summary>
    public static byte[] SerializeToBinary()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(timelines.Count);
        foreach (var kvp in timelines)
        {
            bw.Write(kvp.Key);
            bw.Write(kvp.Value.Count);
            foreach (var ev in kvp.Value)
            {
                bw.Write(ev.Tick);
                bw.Write(ev.Title);
                bw.Write(ev.Description);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializa los datos del timeline desde bytes binarios.
    /// </summary>
    public static void DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            int pawnCount = br.ReadInt32();
            timelines.Clear();
            for (int i = 0; i < pawnCount; i++)
            {
                string pawnId = br.ReadString();
                int eventCount = br.ReadInt32();
                var list = new List<TimelineEvent>(eventCount);
                for (int j = 0; j < eventCount; j++)
                {
                    int tick = br.ReadInt32();
                    string title = br.ReadString();
                    string desc = br.ReadString();
                    list.Add(new TimelineEvent(tick, title, desc));
                }
                timelines[pawnId] = list;
            }
        }
        catch
        {
            // Fallback seguro
        }
    }
}
