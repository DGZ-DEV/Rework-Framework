using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Un spawn programado: qué aparece, dónde y cuándo.
/// Lo crea el framework desde [ReworkDelayedSpawner] o a mano desde código.
/// </summary>
public class ReworkSpawnSpec
{
    public string Id = "";
    public string? PawnKindDefName;
    public string? FactionDefName;
    public int PawnCount = 1;
    public string? ThingDefName;
    public int ThingCount = 1;
    public string? MentalStateDefName;
    public List<string> HediffDefNames = new();
    public int TicksUntilSpawn = 1800;
    public bool Repeat;
    public int RepeatEveryTicks = 60000;
    public int MaxRepeats = -1;
}

/// <summary>
/// Programa spawns con retardo en el mapa actual: tras N ticks aparece lo
/// pedido (pawns con facción/mental state/hediffs, o cosas), con repetición
/// opcional. Sin XML ni edificios invisibles: el framework lleva la cuenta.
///
/// Equivale al CompDelayedSpawner de JecsTools, sin el comp: el spawner no
/// vive en un Thing (que hay que colocar y vigilar), sino en el servicio.
///
/// Uso declarativo (spawn único en el mapa del jugador al arrancar):
/// <code>
/// [ReworkDelayedSpawner("MiMod_StarVampire", PawnKind = "MiMod_StarVampire",
///     Faction = "Mechanoid", TicksUntilSpawn = 1800)]
/// public static class SpawnerVampiro { }
/// </code>
/// Uso manual (posición exacta, desde un incidente o gizmo):
/// <code>
/// ReworkSpawner.Queue(map, cell, spec);
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkDelayedSpawnerAttribute : Attribute
{
    /// <summary>Id único del spawner (para no duplicar).</summary>
    public string Id { get; }

    /// <summary>defName del PawnKindDef a generar (o ThingDefName para cosas).</summary>
    public string? PawnKind { get; set; }

    /// <summary>defName del FactionDef (vacío = sin facción / primera disponible).</summary>
    public string? Faction { get; set; }

    /// <summary>Número de pawns por spawn.</summary>
    public int PawnCount { get; set; } = 1;

    /// <summary>defName del ThingDef a colocar (alternativa a PawnKind).</summary>
    public string? Thing { get; set; }

    /// <summary>Cantidad de cosas por spawn.</summary>
    public int ThingCount { get; set; } = 1;

    /// <summary>defName del MentalStateDef al generar (vacío = ninguno).</summary>
    public string? MentalState { get; set; }

    /// <summary>defNames de HediffDefs a aplicar, separados por coma.</summary>
    public string? Hediffs { get; set; }

    /// <summary>Ticks hasta el spawn (1800 = 30 segundos).</summary>
    public int TicksUntilSpawn { get; set; } = 1800;

    /// <summary>Si se repite tras spawnear.</summary>
    public bool Repeat { get; set; } = false;

    /// <summary>Ticks entre repeticiones.</summary>
    public int RepeatEveryTicks { get; set; } = 60000;

    /// <summary>Máximo de repeticiones (-1 = infinito).</summary>
    public int MaxRepeats { get; set; } = -1;

    public ReworkDelayedSpawnerAttribute(string id)
    {
        Id = id;
    }
}

/// <summary>
/// Registro de spawners declarativos ([ReworkDelayedSpawner]).
/// El tick global los activa en el mapa actual cuando cumplen.
/// </summary>
public static class ReworkDelayedSpawnerRegistry
{
    private static readonly Dictionary<string, ReworkSpawnSpec> spawners =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra un spawner (lo llama el registrador del framework o el mod).
    /// </summary>
    public static void Register(ReworkSpawnSpec spec)
    {
        if (spec == null || string.IsNullOrEmpty(spec.Id)) return;
        spawners[spec.Id] = spec;
    }

    /// <summary>Todos los spawners declarativos.</summary>
    public static IReadOnlyDictionary<string, ReworkSpawnSpec> All => spawners;

    /// <summary>Número de spawners (para early-out en el tick).</summary>
    public static int Count => spawners.Count;
}
