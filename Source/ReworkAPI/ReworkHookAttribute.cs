using System;

namespace Rework;

/// <summary>
/// Puntos del ciclo de vida del juego a los que un mod puede engancharse SIN escribir
/// un transpiler: Rework inyecta automáticamente la llamada a tu hook al INICIO del
/// método del juego que representa el evento.
///
/// El hook debe ser un método ESTÁTICO void, público, en una clase estática:
///   - 0 parámetros  → se llama sin argumentos.
///   - 1 parámetro   → recibe el "this" del evento (p.ej. Verse.Pawn en PawnDied).
/// Si no cumple el contrato, Rework lo reporta y lo omite (no rompe el boot).
/// </summary>
public enum ReworkHookPoint
{
    /// <summary>Verse.Game.LoadGame: una partida empieza a cargarse (nueva o desde save).
    /// Contexto: Verse.Game (opcional).</summary>
    GameStart,

    /// <summary>Verse.Game.FinalizeInit: la partida terminó de inicializarse.
    /// Contexto: Verse.Game (opcional).</summary>
    GameInitialized,

    /// <summary>Verse.Map.FinalizeLoading: un mapa terminó de cargarse.
    /// Contexto: Verse.Map (opcional).</summary>
    MapGenerated,

    /// <summary>Verse.Map.FinalizeInit: un mapa terminó su init.
    /// Contexto: Verse.Map (opcional).</summary>
    MapInitialized,

    /// <summary>Verse.Pawn.Kill: un pawn muere.
    /// Contexto: Verse.Pawn (opcional).</summary>
    PawnDied,

    /// <summary>Verse.Pawn.SpawnSetup(Map, bool): un pawn aparece/entra a un mapa.
    /// Contexto: Verse.Pawn (opcional). Nota: se dispara cada vez que el pawn se
    /// re-ubica (entra al mapa, despawn en zona, etc.), no solo al nacer.</summary>
    PawnSpawned,

    /// <summary>RimWorld.IncidentWorker.TryExecute(IncidentParms): un incidente/incursión
    /// intenta dispararse (penden raids, eventos del storyteller, etc.).
    /// Contexto: RimWorld.IncidentWorker (opcional). Nota: el hook corre ANTES del
    /// procesado (no cambia si el incidente termina aplicándose).</summary>
    IncidentFired,

    /// <summary>Verse.Map.MapPreTick: tick de un mapa (60/s mientras hay mapa activo).
    /// Contexto: Verse.Map (opcional). ADVERTENCIA de rendimiento: se dispara cada tick;
    /// mantén el hook barato o haz un contador (p.ej. solo cada 60 ticks).</summary>
    MapTick,

    /// <summary>Verse.Pawn.Tick: tick de cada pawn (60/s por pawn en el mapa).
    /// Contexto: Verse.Pawn (opcional). ADVERTENCIA de rendimiento: se dispara por CADA
    /// pawn cada tick; úsalo con moderación (contador/intervalo) o degradará el juego.</summary>
    PawnTick,

    /// <summary>Verse.GameComponent.GameComponentTick: tick de cada GameComponent (60/s
    /// mientras hay partida). Contexto: Verse.GameComponent (opcional). RENDIMIENTO:
    /// usa contador/intervalo.</summary>
    GameComponentTick,

    /// <summary>Verse.MapComponent.MapComponentTick: tick de cada MapComponent (60/s por
    /// mapa). Contexto: Verse.MapComponent (opcional). RENDIMIENTO: contador/intervalo.
    /// Nota: solo corre si la subclase llama a base.MapComponentTick().</summary>
    MapComponentTick,

    /// <summary>RimWorld.Planet.WorldComponent.WorldComponentTick: tick de cada
    /// WorldComponent (60/s mientras hay mundo). Contexto: RimWorld.Planet.WorldComponent
    /// (opcional). RENDIMIENTO: contador/intervalo.</summary>
    WorldComponentTick,

    /// <summary>Verse.Game.set_CurrentMap(Map): se cambió el mapa activo de la partida.
    /// Contexto: Verse.Game (opcional); dentro del hook lee game.CurrentMap para el
    /// mapa nuevo.</summary>
    CurrentMapChanged,

    /// <summary>RimWorld.Planet.Caravan (constructor): se creó una caravana nueva.
    /// Contexto: RimWorld.Planet.Caravan (opcional). Nota: el hook corre mientras el
    /// ctor aún está en progreso (no toques campos sin inicializar; solo registra).</summary>
    CaravanSpawned,

    /// <summary>Verse.Game.InitNewGame: arranca una PARTIDA NUEVA (no cargar una existente;
    /// para la carga existe GameStart). Contexto: Verse.Game (opcional).</summary>
    NewGameStarted,

    /// <summary>Verse.Pawn.PostMake: un pawn acaba de ser CREADO por código (p.ej. la
    /// generación de colonos iniciales, raids, etc.). Contexto: Verse.Pawn (opcional).
    /// Nota: es "pawn creado", NO "bebé nacido" — el nacimiento biológico no tiene un
    /// punto único seguro; PostMake cubre la creación programática (la mayoría).</summary>
    PawnCreated,

    /// <summary>RimWorld.Storyteller.StorytellerTick: el storyteller hace su tick (decide
    /// incidentes, dificultad, etc.). Contexto: RimWorld.Storyteller (opcional).
    /// RENDIMIENTO: 60/s; usa contador/intervalo si solo quieres muestrear.</summary>
    StorytellerTick,

    /// <summary>Verse.Game.Dispose: se sale de la partida actual (vuelta al menú / fin).
    /// Contexto: Verse.Game (opcional).</summary>
    GameEnded,

    /// <summary>Verse.GameDataSaveLoader.SaveGame(): se guarda la partida (a autosave o
    /// manual). Sin contexto (método estático sin parámetros útiles).</summary>
    GameSaving,

    /// <summary>RimWorld.PregnancyUtility.ApplyBirthOutcome: nace un bebé.
    /// Contexto: Verse.Pawn — la MADRE biológica (parámetro 5 de ApplyBirthOutcome;
    /// el bebé no existe aún como Pawn en ese punto).</summary>
    BabyBorn,

    /// <summary>RimWorld.Planet.CaravanEnterMapUtility.Enter: una caravana entra a un mapa.
    /// Contexto: Verse.Map — el mapa destino (parámetro 2 de Enter).</summary>
    CaravanEnteredMap,

    /// <summary>RimWorld.Planet.CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan: un pawn
    /// sale del mapa para unirse/crear una caravana.
    /// Contexto: Verse.Pawn — el pawn que sale (parámetro 1).</summary>
    PawnLeftToCaravan,
}

/// <summary>
/// Marca un método estático void para que Rework lo enganche al punto del ciclo de
/// vida indicado. Rework inyecta la llamada al inicio del método del juego, ANTES de
/// cualquier región try/catch (mismo patrón que los inicializadores de campo).
///
///   [ReworkHook(ReworkHookPoint.GameStart)]
///   public static void OnGameStart() { ... }
///
///   [ReworkHook(ReworkHookPoint.PawnDied)]
///   public static void OnPawnDied(Pawn pawn) { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkHookAttribute : Attribute
{
    /// <summary>Punto del ciclo de vida al que enganchar.</summary>
    public ReworkHookPoint Point { get; set; }

    public ReworkHookAttribute()
    {
    }

    /// <summary>Conveniencia: [ReworkHook(ReworkHookPoint.GameStart)] ≡ [ReworkHook(Point = ...)].</summary>
    public ReworkHookAttribute(ReworkHookPoint point)
    {
        Point = point;
    }
}