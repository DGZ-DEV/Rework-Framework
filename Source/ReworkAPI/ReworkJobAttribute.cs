using System;

namespace Rework;

/// <summary>
/// Registra automáticamente un JobDef de RimWorld en runtime para la clase JobDriver decorada.
/// Elimina la necesidad de escribir XMLs de JobDef en el desarrollo de mods.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkJobAttribute : Attribute
{
    /// <summary>Nombre único del JobDef (defName).</summary>
    public string DefName { get; }

    /// <summary>Texto que aparece en la interfaz cuando el colono realiza este trabajo.</summary>
    public string ReportString { get; set; } = "Realizando tarea.";

    /// <summary>Indica si el jugador puede interrumpir el trabajo manualmente.</summary>
    public bool PlayerInterruptible { get; set; } = true;

    /// <summary>Indica si el trabajo puede interrumpirse por razones casuales.</summary>
    public bool CasualInterruptible { get; set; } = true;

    /// <summary>Indica si el trabajo puede suspenderse temporalmente.</summary>
    public bool Suspendable { get; set; } = true;

    public ReworkJobAttribute(string defName)
    {
        DefName = defName;
    }
}
