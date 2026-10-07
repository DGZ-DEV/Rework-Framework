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

    public ReworkWorkGiverAttribute(string defName)
    {
        DefName = defName;
    }
}
