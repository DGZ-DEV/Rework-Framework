using System;

namespace Rework;

/// <summary>
/// Registra automáticamente un RecipeDef de RimWorld en runtime para la clase RecipeWorker decorada.
/// Permite definir recetas de mesas de trabajo y cirugías sin escribir archivos XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkRecipeAttribute : Attribute
{
    /// <summary>Nombre único del RecipeDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible en la interfaz para el jugador.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción detallada de la receta.</summary>
    public string? Description { get; set; }

    /// <summary>Texto en el menú de asignación de trabajo (ej: "fabricando armadura").</summary>
    public string JobString { get; set; } = "Haciendo.";

    /// <summary>Cantidad base de trabajo requerida en ticks (ej: 1000f).</summary>
    public float WorkAmount { get; set; } = 500f;

    /// <summary>Nombres de los edificios/mesas donde se puede realizar la receta separados por coma (ej: "CraftingSpot,TableMachining").</summary>
    public string? RecipeUsers { get; set; }

    public ReworkRecipeAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un HediffDef de RimWorld en runtime para la clase Hediff decorada.
/// Permite definir condiciones de salud, implantes, estados mentales y buffs sin escribir archivos XML.
///
/// ⚠️ §48 — el registro crea el Def, pero NINGUNA ruta vanilla ni del framework lo
/// APLICA por sí solo: el hediff solo existe en un pawn cuando algo lo aplica.
/// Vías de aplicación: una receta con addsHediff, código tuyo
/// (pawn.health.AddHediff), o un [ReworkStatusEffect] con el MISMO defName
/// (eso sí lo aplica/ex-pira el framework). Sin una de ellas, el Def queda
/// registrado pero inerte.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkHediffAttribute : Attribute
{
    /// <summary>Nombre único del HediffDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible en la pestaña de salud del colono.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción médica o detallada del efecto.</summary>
    public string? Description { get; set; }

    /// <summary>Severidad inicial del hediff (por defecto 1.0f).</summary>
    public float InitialSeverity { get; set; } = 1.0f;

    /// <summary>Severidad a la cual el pawn muere por este hediff (si es <= 0 no es letal).</summary>
    public float LethalSeverity { get; set; } = -1.0f;

    /// <summary>Indica si se considera una condición negativa o enfermedad.</summary>
    public bool IsBad { get; set; } = false;

    public ReworkHediffAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un TraitDef de RimWorld en runtime para la clase decorada.
/// Permite crear rasgos de personalidad de colonos sin escribir archivos XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkTraitAttribute : Attribute
{
    /// <summary>Nombre único del TraitDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre del rasgo que ven los jugadores.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción del rasgo de personalidad.</summary>
    public string? Description { get; set; }

    /// <summary>Grado o nivel del rasgo (por defecto 0).</summary>
    public int Degree { get; set; } = 0;

    /// <summary>Probabilidad relativa de aparición en nuevos colonos (por defecto 1.0f).</summary>
    public float Commonality { get; set; } = 1.0f;

    public ReworkTraitAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un NeedDef de RimWorld en runtime para la clase Need decorada.
/// Permite crear barras de necesidad para colonos sin escribir archivos XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkNeedAttribute : Attribute
{
    /// <summary>Nombre único del NeedDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Nombre visible de la necesidad en la interfaz.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción detallada de la necesidad.</summary>
    public string? Description { get; set; }

    /// <summary>Nivel base inicial (0.0 a 1.0, por defecto 0.5f).</summary>
    public float BaseLevel { get; set; } = 0.5f;

    /// <summary>Tasa de caída por día (por defecto 0.5f).</summary>
    public float FallPerDay { get; set; } = 0.5f;

    /// <summary>Indica si solo aplica a colonos del jugador.</summary>
    public bool ColonistsOnly { get; set; } = true;

    public ReworkNeedAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registra automáticamente un Designator en una categoría de órdenes en la interfaz sin XML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkDesignatorAttribute : Attribute
{
    /// <summary>Categoría donde aparecerá el designador (ej: "Orders", "Zone", "Structure", "Production").</summary>
    public string Category { get; set; } = "Orders";

    public ReworkDesignatorAttribute(string category = "Orders")
    {
        Category = category;
    }
}

/// <summary>
/// Registra automáticamente un GenStep para generación procedimental de mapas sin XML.
/// Inyecta el GenStepDef en MapGeneratorDef para que se ejecute al generar nuevos mapas.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkGenStepAttribute : Attribute
{
    /// <summary>Nombre único del GenStepDef.</summary>
    public string DefName { get; }

    /// <summary>Orden de ejecución dentro de la generación de mapas (mayor = más tarde).</summary>
    public float Order { get; set; } = 500f;

    /// <summary>Generador de mapa objetivo (por defecto "MainMapGenerator").</summary>
    public string MapGenerator { get; set; } = "MainMapGenerator";

    public ReworkGenStepAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Aplica una mutación declarativa, reversible y documentada sobre campos de Defs existentes de vanilla.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public sealed class ReworkMutateAttribute : Attribute
{
    /// <summary>Tipo de Def objetivo (ej: typeof(ThingDef)). Si es null, se infiere del DefName.</summary>
    public Type? DefType { get; set; }

    /// <summary>defName del Def de RimWorld a mutar.</summary>
    public string DefName { get; }

    /// <summary>Ruta al campo (ej: "baseHealthScale" o "statBases.MiningSpeed").</summary>
    public string FieldPath { get; }

    /// <summary>Nuevo valor asignado.</summary>
    public object? Value { get; }

    public ReworkMutateAttribute(string defName, string fieldPath, object? value)
    {
        DefName = defName;
        FieldPath = fieldPath;
        Value = value;
    }
}


