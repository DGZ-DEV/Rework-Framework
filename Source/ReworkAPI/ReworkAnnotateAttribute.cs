using System;

namespace Rework;

/// <summary>
/// Añade un ATRIBUTO (metadata) a un miembro existente del juego (bloque 4.10/4.11/4.12).
///
/// Rework inyecta una instancia del atributo (de tu mod) en la clase/método/campo del
/// juego. Sirve para marcar/etiquetar miembros (p.ej. para que TU código o el de otros
/// mods los encuentren por reflexión con GetCustomAttribute).
///
///   [ReworkAnnotate(Type = "Verse.Pawn")]                       // clase (4.8/4.10)
///   [ReworkAnnotate(Type = "Verse.Pawn", Member = "Kill")]      // método (4.11)
///   [ReworkAnnotate(Type = "Verse.Pawn", Member = "health")]    // campo (4.12)
///
/// Puedes usar un atributo propio con datos, o uno genérico de marcado:
///   public class ReworkMarcaAttribute : Attribute { public string Nota; }
///   [ReworkAnnotate(Type = "Verse.Pawn", Member = "Kill", Attribute = "ReworkMarca",
///                   CtorArgs = new object[] { "patcheado" })]
///
/// El nombre del atributo (Attribute) se resuelve como el nombre corto del tipo del
/// atributo (p.ej. "ReworkMarca" o "MiMod.ReworkMarca"); si es null se usa un
/// atributo genérico de marcado propio de Rework.
/// Requisito v1: el atributo debe existir en un ensamblado CARGADO (el del mod o el
/// juego) y tener un constructor público sin parámetros o con los args dados.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkAnnotateAttribute : Attribute
{
    /// <summary>Tipo destino (ruta completa, p.ej. "Verse.Pawn"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Si es null → clase; si no, nombre del método o campo del tipo a anotar.</summary>
    public string? Member { get; set; }

    /// <summary>Nombre corto o completo del tipo del atributo a inyectar. Null → marcador genérico.</summary>
    public string? Attribute { get; set; }

    /// <summary>Argumentos del constructor del atributo (opcionales).</summary>
    public object[]? CtorArgs { get; set; }

    public ReworkAnnotateAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkAnnotate("Verse.Pawn")] ≡ [ReworkAnnotate(Type = "Verse.Pawn")].</summary>
    public ReworkAnnotateAttribute(string type)
    {
        Type = type;
    }
}