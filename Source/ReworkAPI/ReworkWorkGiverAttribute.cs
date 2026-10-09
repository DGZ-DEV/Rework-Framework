using System;

namespace Rework;

/// <summary>
/// Registra automáticamente un WorkGiverDef de RimWorld en runtime para la clase WorkGiver decorada.
/// Conecta la asignación automática de trabajos de la colonia con los JobDefs sin requerir XMLs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkWorkGiverAttribute : Attribute
{
    /// <summary>Nombre único del WorkGiverDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Tipo de trabajo al que pertenece (ej: "Hauling", "Cleaning", "Crafting", "Doctor").</summary>
    public string WorkType { get; set; } = "Hauling";

    /// <summary>Prioridad dentro del WorkType (mayor número = mayor prioridad dentro de la categoría).</summary>
    public int PriorityInType { get; set; } = 50;

    /// <summary>Verbo que describe la acción (ej: "reparar", "limpiar").</summary>
    public string? Verb { get; set; }

    /// <summary>Gerundio que describe la acción (ej: "reparando", "limpiando").</summary>
    public string? Gerund { get; set; }

    /// <summary>Indica si el jugador puede ordenar el trabajo directamente con clic derecho.</summary>
    public bool DirectOrderable { get; set; } = true;

    /// <summary>Indica si el worker escanea cosas en el mapa.</summary>
    public bool ScanThings { get; set; } = true;

    /// <summary>Indica si el worker escanea celdas en el mapa.</summary>
    public bool ScanCells { get; set; } = false;

    /// <summary>Indica si es un trabajo de emergencia (máxima prioridad al ocurrir).</summary>
    public bool Emergency { get; set; } = false;

    // -------------------------------------------------------------------------
    // Metadatos para CREAR el WorkTypeDef si 'WorkType' todavía no existe.
    //
    // Antes, si el WorkType no existía, el registro caía en silencio a Hauling:
    // el trabajo declarado acababa escondido dentro de "acarreo" (fantasma).
    // Con estos campos, [ReworkWorkGiver] declara su PROPIO tipo de trabajo y el
    // framework lo crea y lo añade a DefDatabase — todo sin XML.
    // -------------------------------------------------------------------------

    /// <summary>Si true (por defecto), se crea y añade el WorkTypeDef cuando 'WorkType' no existe.</summary>
    public bool CreateWorkTypeIfMissing { get; set; } = true;

    /// <summary>Etiqueta visible del WorkTypeDef a crear (ej: "matanza"). Si es null se usa el defName.</summary>
    public string? WorkTypeLabel { get; set; }

    /// <summary>Etiqueta corta del WorkTypeDef a crear. Si es null se usa WorkTypeLabel.</summary>
    public string? WorkTypeLabelShort { get; set; }

    /// <summary>Etiqueta del colono para el WorkTypeDef a crear. Si es null se usa WorkTypeLabel.</summary>
    public string? WorkTypePawnLabel { get; set; }

    /// <summary>Gerundio del WorkTypeDef a crear (ej: "matando"). Si es null se usa WorkTypeLabel.</summary>
    public string? WorkTypeGerund { get; set; }

    /// <summary>Verbo del WorkTypeDef a crear (ej: "matar"). Si es null se usa WorkTypeLabel.</summary>
    public string? WorkTypeVerb { get; set; }

    /// <summary>WorkTags del WorkTypeDef a crear, separados por coma (ej: "Violent" o "Violent,ManualDumb").</summary>
    public string? WorkTypeTags { get; set; }

    /// <summary>Si true (por defecto), el WorkTypeDef a crear aparece como columna en la
    /// pestaña de Trabajo (así el jugador puede subirle/bajarle la prioridad o desactivarlo).</summary>
    public bool WorkTypeVisible { get; set; } = true;

    /// <summary>Prioridad natural del WorkTypeDef a crear (posición en la pestaña de Trabajo).</summary>
    public int WorkTypeNaturalPriority { get; set; } = 0;

    /// <summary>Si true, el WorkTypeDef a crear arranca activo a su prioridad natural (1).
    /// Si false (por defecto), los colonos lo reciben habilitado a la prioridad más baja (3).</summary>
    public bool WorkTypeAlwaysStartActive { get; set; } = false;

    public ReworkWorkGiverAttribute(string defName)
    {
        DefName = defName;
    }
}
