using System;

namespace Rework;

/// <summary>
/// Añade un método REAL a una clase del juego (bloque 3.1/3.2 de la lista).
///
/// Rework inyecta en el tipo destino un método cuyo cuerpo es un FORWARDER a tu
/// método estático: toda la lógica vive en tu mod, el juego solo gana el miembro.
/// Así queda como un método de verdad (visible por reflexión, invocable desde
/// código del juego / otros mods como `pawn.MiMetodo()`).
///
/// Patrón (método de INSTANCIA — el primer parámetro es el 'this' del juego):
///   [ReworkMethod(Type = "Verse.Pawn")]
///   public static void Rework_Cumpleaños(this Verse.Pawn pawn, string nota)
///   {
///       pawn.Rework_Logros().Add("cumpleaños: " + nota);
///   }
///
/// Patrón (método ESTÁTICO — sin 'this'):
///   [ReworkMethod(Type = "Verse.Game", Instance = false)]
///   public static void Rework_EstadisticasTotales() { ... }
///
/// Reglas: el método del mod debe ser estático y público (acceso entre ensamblados).
/// Para instancia, el primer parámetro DEBE ser el tipo destino. Si ya existe un
/// método con el mismo nombre+firma en el tipo destino, se reporta y se omite.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkMethodAttribute : Attribute
{
    /// <summary>Tipo destino (ruta completa, p.ej. "Verse.Pawn"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre del método en el tipo destino. Default: el nombre del método del mod.</summary>
    public string? Name { get; set; }

    /// <summary>true → método de instancia (primer parámetro = el 'this' del juego).
    /// false → método estático añadido al tipo destino. Default: true.</summary>
    public bool Instance { get; set; } = true;

    public ReworkMethodAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkMethod("Verse.Pawn")] ≡ [ReworkMethod(Type = "Verse.Pawn")].</summary>
    public ReworkMethodAttribute(string type)
    {
        Type = type;
    }
}