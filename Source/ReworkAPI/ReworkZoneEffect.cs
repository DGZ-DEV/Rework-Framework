using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Define un área o zona geográfica lógica que aplica efectos continuos o periódicos a los pawns
/// dentro de su perímetro, sin requerir ThingDef, Building ni ThingComp en XML.
/// </summary>
public class ZoneEffectDefinition
{
    public string Id { get; }
    public int IntervalTicks { get; set; } = 60;
    public Action<object>? OnPawnInside { get; set; }

    public ZoneEffectDefinition(string id, int intervalTicks = 60)
    {
        Id = id;
        IntervalTicks = intervalTicks;
    }
}

/// <summary>
/// Motor y despachador de Efectos de Zona de Rework.
/// </summary>
public static class ReworkZoneManager
{
    private static readonly Dictionary<string, ZoneEffectDefinition> zones = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterZone(string id, int intervalTicks, Action<object> onPawnInside)
    {
        zones[id] = new ZoneEffectDefinition(id, intervalTicks)
        {
            OnPawnInside = onPawnInside
        };
    }

    public static ZoneEffectDefinition? GetZone(string id)
    {
        zones.TryGetValue(id, out var zone);
        return zone;
    }

    /// <summary>Todas las zonas registradas (las consume ZoneRuntime para el tick real).</summary>
    public static IReadOnlyDictionary<string, ZoneEffectDefinition> RegisteredZones => zones;

    public static int RegisteredCount => zones.Count;

    public static void TriggerEffect(string zoneId, object pawn)
    {
        if (zones.TryGetValue(zoneId, out var zone))
        {
            try
            {
                zone.OnPawnInside?.Invoke(pawn);
            }
            catch
            {
                // Resiliencia pasiva ante excepciones de mods
            }
        }
    }
}
