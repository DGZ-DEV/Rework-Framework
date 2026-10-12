using System;

namespace Rework;

/// <summary>
/// Declara absorción plana de daño en la clase Hediff que lleva el atributo:
/// cada vez que el portador vaya a recibir daño, el framework resta
/// <see cref="DamageToSoak"/> puntos del golpe antes de que vanilla lo procese.
///
/// Equivale al HediffComp_DamageSoak de JecsTools, pero sin XML: el registro
/// crea el HediffDef y engancha el comportamiento en el despacho de daño.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkDamageSoakAttribute : Attribute
{
    /// <summary>Nombre único del HediffDef (defName) que absorbe daño.</summary>
    public string DefName { get; }

    /// <summary>Cantidad plana absorbida por golpe (después se resta del daño).</summary>
    public float DamageToSoak { get; set; } = 1f;

    /// <summary>
    /// defName del DamageDef al que aplica (p. ej. "Cut"). Null o vacío = todos.
    /// </summary>
    public string? DamageType { get; set; }

    /// <summary>
    /// defNames de DamageDefs excluidos, separados por coma (p. ej. "Burn,Flame").
    /// Null o vacío = sin exclusiones.
    /// </summary>
    public string? DamageTypesToExclude { get; set; }

    /// <summary>Nombre visible en la pestaña de salud del colono.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción de la absorción.</summary>
    public string? Description { get; set; }

    /// <summary>Si true, el mote flotante muestra el daño absorbido ("-3").</summary>
    public bool ShowAbsorbedMote { get; set; } = true;

    public ReworkDamageSoakAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro de soaks declarativos ([ReworkDamageSoak]).
///
/// El escáner (pasada 2, con Defs cargados) resuelve los defNames de DamageDef a
/// referencias reales y guarda la resolución aquí. El despacho de daño
/// (ReworkDamageRuntime en el mod) solo consulta este registro: nunca toca
/// DefDatabase en el camino caliente.
/// </summary>
public static class ReworkDamageSoakRegistry
{
    public class SoakData
    {
        public string DefName = "";
        public Type HandlerType = null!;
        public float DamageToSoak;
        public string? DamageTypeName;
        public string[] ExcludedTypeNames = Array.Empty<string>();
        public bool ShowAbsorbedMote = true;

        /// <summary>Resolución perezosa del DamageDef filtro (null = todos).</summary>
        public object? DamageTypeDef;

        /// <summary>Resolución perezosa de los DamageDefs excluidos.</summary>
        public object[] ExcludedTypeDefs = Array.Empty<object>();

        public string Label = "";
        public string Description = "";
    }

    private static readonly System.Collections.Generic.Dictionary<string, SoakData> soaks =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra los datos de un soak (lo llama el registrador del framework).
    /// </summary>
    public static void Register(SoakData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        soaks[data.DefName] = data;
    }

    /// <summary>Datos del soak por defName del Hediff, o null si no es un soak.</summary>
    public static SoakData? Get(string defName)
    {
        soaks.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Número de soaks registrados (para early-out en el despacho de daño).</summary>
    public static int Count => soaks.Count;
}
