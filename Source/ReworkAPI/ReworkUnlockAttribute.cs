using System;

namespace Rework;

/// <summary>
/// UNLOCK DE VISIBILIDAD (bloque 24.3): convierte miembros y tipos del juego en
/// API pública REAL — no reflexión: el propio Assembly-CSharp queda reescrito con
/// private/internal → public, clases sealed → heredables y métodos normales →
/// virtuales. Los mods escriben `pawn.jobs.curJob` directamente: compilable,
/// autocompletable, coste cero.
///
/// Se puede declarar sobre un método, una clase o el ensamblado entero, y admite
/// MÚLTIPLES instancias (un "manifiesto" de unlocks):
///
///   internal static class MisUnlocks
///   {
///       [ReworkUnlock(Type = "Verse.AI.Group.Lord", Member = "curJob")]
///       private static void Unlock1() { }          // (el método es solo el portador)
///
///       [ReworkUnlock(Type = "RimWorld.CompActivity", Unseal = true)]
///       private static void Unlock2() { }
///   }
///
/// Semántica:
///  - Member == null → opera sobre el TIPO: Public = true lo hace público
///    (tipos internos y anidados); Unseal = true le quita el sealed.
///  - Member = nombre → opera sobre ese campo, método o propiedad del tipo:
///    Public = true lo hace público; MakeVirtual = true (solo métodos de
///    instancia no virtuales) lo convierte en virtual para que otros mods
///    puedan sobrescribirlo.
/// Los cambios son de METADATOS (la vtable se construye tras la reescritura, con
/// la versión final), por lo que son seguros: no alteran ningún comportamiento
/// existente, solo abren la API.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class ReworkUnlockAttribute : Attribute
{
    /// <summary>FullName del tipo destino (p. ej. "Verse.AI.Group.Lord"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Miembro a desbloquear (campo, método o propiedad). Null → el tipo entero.</summary>
    public string? Member { get; set; }

    /// <summary>Hacer público el tipo o el miembro. Default: true.</summary>
    public bool Public { get; set; } = true;

    /// <summary>(Solo nivel de tipo) quitar el sealed para permitir herencia. Default: true.</summary>
    public bool Unseal { get; set; } = true;

    /// <summary>(Solo métodos de instancia no virtuales) convertirlo en virtual.
    /// El método sigue comportándose igual; solo se abre a overrides futuros.</summary>
    public bool MakeVirtual { get; set; }

    public ReworkUnlockAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkUnlock("Verse.AI.Group.Lord", "curJob")] ≡ propiedades.</summary>
    public ReworkUnlockAttribute(string type, string? member = null)
    {
        Type = type;
        Member = member;
    }
}
