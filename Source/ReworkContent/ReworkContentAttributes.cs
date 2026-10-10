using System;

namespace Rework;

/// <summary>
/// Registra automáticamente un GeneDef (xenogenética) de RimWorld en runtime para la clase Gene decorada.
/// Permite crear genes de xenotipos sin escribir archivos XML en Defs/GeneDefs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkGeneAttribute : Attribute
{
    /// <summary>Nombre único del GeneDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible del gen.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción del gen.</summary>
    public string? Description { get; set; }

    /// <summary>Subclase de Verse.Gene que implementa el comportamiento (por defecto Verse.Gene).</summary>
    public Type GeneClass { get; set; } = typeof(Verse.Gene);

    /// <summary>Categoría de visualización (defName de GeneCategoryDef, p.ej. "Miscellaneous" o "Archite").</summary>
    public string DisplayCategory { get; set; } = "Miscellaneous";

    /// <summary>Complejidad (biostat-Cpx) del gen.</summary>
    public int BiostatCpx { get; set; } = 0;

    /// <summary>Metabolismo (biostat-Met) del gen.</summary>
    public int BiostatMet { get; set; } = 0;

    /// <summary>Arquitectura (biostat-Arc) del gen.</summary>
    public int BiostatArc { get; set; } = 0;

    /// <summary>Peso de selección al generar xenotipos.</summary>
    public float SelectionWeight { get; set; } = 1.0f;

    /// <summary>Si puede aparecer en conjuntos de genes generados aleatoriamente.</summary>
    public bool CanGenerateInGeneSet { get; set; } = true;

    /// <summary>Etiqueta corta adjetiva (p.ej. "resistente").</summary>
    public string? LabelShortAdj { get; set; }

    public ReworkGeneAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un ResearchProjectDef de RimWorld en runtime.
/// Permite añadir investigaciones al árbol de tecnología sin escribir XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkResearchAttribute : Attribute
{
    /// <summary>Nombre único del ResearchProjectDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible de la investigación.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción de la investigación.</summary>
    public string? Description { get; set; }

    /// <summary>Coste base en puntos de investigación.</summary>
    public float BaseCost { get; set; } = 500f;

    /// <summary>Nivel tecnológico requerido (RimWorld.TechLevel).</summary>
    public RimWorld.TechLevel TechLevel { get; set; } = RimWorld.TechLevel.Industrial;

    /// <summary>Pestaña del árbol de investigación (defName de ResearchTabDef, p.ej. "Main").</summary>
    public string Tab { get; set; } = "Main";

    /// <summary>Coordenada X en el árbol de investigación.</summary>
    public float X { get; set; } = 0f;

    /// <summary>Coordenada Y en el árbol de investigación.</summary>
    public float Y { get; set; } = 0f;

    /// <summary>defName del edificio de investigación requerido (opcional).</summary>
    public string? RequiredResearchBuilding { get; set; }

    /// <summary>defNames de investigaciones previas requeridas (separados por coma; opcional).</summary>
    public string? Prerequisites { get; set; }

    public ReworkResearchAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un RaidStrategyDef de RimWorld en runtime.
/// Permite crear estrategias de incursión personalizadas sin escribir XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkRaidAttribute : Attribute
{
    /// <summary>Nombre único del RaidStrategyDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible de la estrategia.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción de la estrategia de incursión.</summary>
    public string? Description { get; set; }

    /// <summary>Mínimo de pañuelos (pawns) para la incursión.</summary>
    public float MinPawns { get; set; } = 1f;

    /// <summary>Modificador del factor de puntos (curva simple: 0 → 1.0).</summary>
    public float PointsFactor { get; set; } = 1.0f;

    /// <summary>Peso de selección de la estrategia (curva plana; vanilla lo evalúa
    /// contra los puntos de la incursión en RaidStrategyWorker.SelectionWeight).
    /// §48: OBLIGATORIO asignar selectionWeightPerPointsCurve — sin ella, vanilla
    /// lanza NRE dentro del filtro de estrategias y REVIENTA TODAS las raids enemigas.</summary>
    public float SelectionWeight { get; set; } = 1.0f;

    /// <summary>Modo de llegada (defName de PawnsArrivalModeDef, p.ej. "EdgeWalkIn").</summary>
    public string ArriveMode { get; set; } = "EdgeWalkIn";

    /// <summary>Texto de llegada cuando el bando es enemigo.</summary>
    public string? ArrivalTextEnemy { get; set; }

    /// <summary>Texto de llegada cuando el bando es amistoso.</summary>
    public string? ArrivalTextFriendly { get; set; }

    /// <summary>Etiqueta de la carta cuando el bando es enemigo.</summary>
    public string? LetterLabelEnemy { get; set; }

    /// <summary>Etiqueta de la carta cuando el bando es amistoso.</summary>
    public string? LetterLabelFriendly { get; set; }

    public ReworkRaidAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un ThoughtDef de RimWorld en runtime.
/// Permite crear pensamientos (buffs/debuffs de ánimo) sin escribir XML.
///
/// ⚠️ §48 — DOS condiciones que antes no se documentaban:
/// (1) APLICACIÓN: ninguna ruta vanilla aplica un ThoughtDef arbitrario; algo
///     debe concederlo (ThoughtWorker/situación, o código tuyo con
///     pawn.needs.mood.thoughts.memories.TryGainMemory / ThoughtMaker).
/// (2) CLASE DECORADA: la clase que lleva el atributo NO se conecta al Def —
///     el pensamiento usa ThoughtClass (default Thought_Memory). Si escribes
///     lógica en la clase anotada, asigna ThoughtClass = typeof(TuClase).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkThoughtAttribute : Attribute
{
    /// <summary>Nombre único del ThoughtDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible del pensamiento.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción del pensamiento.</summary>
    public string? Description { get; set; }

    /// <summary>Efecto base sobre el ánimo del colono (unidades de ánimo, -100..+100).</summary>
    public float BaseMoodEffect { get; set; } = 0f;

    /// <summary>Duración en días del pensamiento (solo si es una memoria).</summary>
    public float DurationDays { get; set; } = 1f;

    /// <summary>Subclase de RimWorld.Thought que implementa el comportamiento (por defecto RimWorld.Thought_Memory).</summary>
    public Type ThoughtClass { get; set; } = typeof(RimWorld.Thought_Memory);

    public ReworkThoughtAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un ThingDef de prenda (RimWorld.Apparel) en runtime.
/// Permite crear ropa y armadura equipable sin escribir XML en Defs/ThingDefs.
///
/// ⚠️ §48 — el registro crea el ThingDef, pero no le da OBTENCIÓN: sin
/// ThingCategoryDef, receta de confección, costList o stock de trader, la
/// prenda NO aparece en el juego normal (solo spawneable por dev-mode/código).
/// Además: se crea sin statBases (sin masa/MaxHitPoints — añádelos con
/// [ReworkMutate] o código si los necesitas) y TexPath debe apuntar a una
/// textura real. La clase decorada se ignora salvo que pases
/// ApparelClass = typeof(...) con una subclase de Apparel.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkApparelAttribute : Attribute
{
    /// <summary>Nombre único del ThingDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible de la prenda.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción de la prenda.</summary>
    public string? Description { get; set; }

    /// <summary>defName de la capa de vestimenta (p.ej. "OnSkin", "Middle", "Shell").</summary>
    public string Layer { get; set; } = "OnSkin";

    /// <summary>defNames de los grupos de partes del cuerpo cubiertas (separados por coma, p.ej. "Torso,Legs").</summary>
    public string BodyPartGroups { get; set; } = "Torso";

    /// <summary>Ruta de la textura (texPath) de la prenda.</summary>
    public string TexPath { get; set; } = "Things/Item/Resource/Cloth";

    /// <summary>Subclase de RimWorld.Apparel (por defecto RimWorld.Apparel).</summary>
    public Type ApparelClass { get; set; } = typeof(RimWorld.Apparel);

    /// <summary>Volumen del objeto (en litros).</summary>
    public float Volume { get; set; } = 0.2f;

    /// <summary>Si genera algo de calor (no usado si no hay statBases).</summary>
    public bool IsArmor { get; set; } = false;

    public ReworkApparelAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra una zona de efecto geográfica ([ReworkZoneEffect]) gestionada por
/// ReworkZoneManager (0ReworkAPI, invención del roadmap).
/// Marca un método estático que se invoca periódicamente por cada pawn dentro de la zona.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkZoneEffectAttribute : Attribute
{
    /// <summary>Id único de la zona.</summary>
    public string Id { get; }

    /// <summary>Intervalo de ticks entre invocaciones (60 = 1s).</summary>
    public int IntervalTicks { get; set; } = 60;

    public ReworkZoneEffectAttribute(string id)
    {
        Id = id;
    }
}