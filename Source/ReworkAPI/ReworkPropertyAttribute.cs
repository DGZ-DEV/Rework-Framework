using System;

namespace Rework;

/// <summary>
/// Añade una propiedad REAL a una clase del juego (bloque 3.3 de la lista).
///
/// Rework inyecta en el tipo destino una propiedad cuyo getter y setter son
/// FORWARDERS a tus métodos estáticos del mod: leen/escriben tu fuente de verdad
/// (p.ej. un campo [ReworkField]) con cero interfaz. Así `juego.MiPropiedad` queda
/// como una propiedad de verdad para el resto del código.
///
/// Patrón — getter y setter en un solo atributo:
///   [ReworkProperty(Type = "Verse.Pawn", Name = "Rework_Notas")]
///   public static string? Get_Rework_Notas(this Verse.Pawn pawn)
///       => string.Join(", ", pawn.Rework_Logros());
///
///   [ReworkProperty(Type = "Verse.Pawn", Name = "Rework_Notas")]
///   public static void Set_Rework_Notas(this Verse.Pawn pawn, string? value)
///   {
///       pawn.Rework_Logros().Clear();
///       if (value != null) pawn.Rework_Logros().AddRange(...);
///   }
///
/// Convenciones:
///   - Getter: estático público con 0 o 1 parámetro ('this' para instancia), NO-void.
///   - Setter: estático público con 1 o 2 parámetros (último = el valor), void.
///   - El nombre del método DEL MOD debe empezar por Get_ / Set_ + el nombre de la
///     propiedad (o usa Name). Rework empareja por el nombre de la propiedad.
///   - Solo getter → propiedad de solo lectura (set privado); solo setter → de solo
///     escritura (no habitual).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkPropertyAttribute : Attribute
{
    /// <summary>Tipo destino (ruta completa, p.ej. "Verse.Pawn"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre de la propiedad en el tipo destino. Default: el nombre tras "Get_"/"Set_".</summary>
    public string? Name { get; set; }

    public ReworkPropertyAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkProperty("Verse.Pawn")] ≡ [ReworkProperty(Type = "Verse.Pawn")].</summary>
    public ReworkPropertyAttribute(string type)
    {
        Type = type;
    }
}