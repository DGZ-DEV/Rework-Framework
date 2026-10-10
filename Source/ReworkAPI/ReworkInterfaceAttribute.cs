using System;

namespace Rework;

/// <summary>
/// Añade una INTERFAZ (definida por tu mod) a un tipo del juego (bloque 4.1/4.4–4.8).
///
/// Marca una interfaz de tu mod con el tipo destino: Rework registra la interfaz en la
/// clase del juego, de modo que `pawn is MiInterfaz` funciona y el cast polimórfico
/// también (`((MiInterfaz)pawn).Metodo()`).
///
/// Requisito: la clase destino DEBE implementar ya los miembros de la interfaz. Para
/// añadirlos usa [ReworkMethod] con el MISMO nombre (la interfaz se valida y se omite
/// con error si falta algún miembro — 4.9 v1: la implementación la pones tú con
/// ReworkMethod, no automáticamente).
///
///   [ReworkInterface(Type = "Verse.Pawn")]
///   public interface IReworkContadorLogros { int CountLogros(); }
///
///   [ReworkMethod(Type = "Verse.Pawn", Name = "CountLogros")]
///   public static int CountLogros(this Verse.Pawn pawn) => pawn.Rework_Logros().Count;
///
/// Límites v1: interfaces SIN genéricos y con métodos/propiedades simples. El tipo
/// destino NO puede ser genérico ni nested.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public class ReworkInterfaceAttribute : Attribute
{
    /// <summary>Tipo destino (ruta completa, p.ej. "Verse.Pawn"). Obligatorio.</summary>
    public string Type { get; set; }

    public ReworkInterfaceAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkInterface("Verse.Pawn")] ≡ [ReworkInterface(Type = "Verse.Pawn")].</summary>
    public ReworkInterfaceAttribute(string type)
    {
        Type = type;
    }
}