using System;

namespace Rework;

/// <summary>
/// Sustituye todas las llamadas a un método trivial del juego (cuerpo exacto
/// <c>ldarg.0; ldfld F; ret</c> de instancia, o <c>ldsfld F; ret</c> estático) por el
/// acceso directo al campo, en todos los ensamblados reescritos. La llamada
/// desaparece del IL: Mono no inlinea entre ensamblados, por eso el compilador
/// original no lo hizo.
///
///   [ReworkInline(Type = "Verse.Pawn", Method = "get_Name")]     // 189 call-sites
///   private static void InlineNombre() { }                       // (portador)
///
/// Salvaguardas (el procesador rechaza lo que no cumpla):
///  - el método debe existir y su cuerpo debe ser exactamente el patrón trivial;
///  - no se inlinean métodos virtuales no finales (rompería el polimorfismo);
///  - no se inlinean métodos genéricos;
///  - si el método no es trivial, se reporta con error y no se toca nada.
///
/// Único cambio semántico: con un receptor null, la NullReferenceException se lanza
/// en el acceso al campo en vez de en la llamada.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class ReworkInlineAttribute : Attribute
{
    /// <summary>FullName del tipo destino (p. ej. "Verse.Pawn"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre del método trivial a inlinear (p. ej. "get_Name"). Obligatorio.</summary>
    public string Method { get; set; }

    public ReworkInlineAttribute()
    {
        Type = string.Empty;
        Method = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkInline("Verse.Pawn", "get_Name")] ≡ propiedades.</summary>
    public ReworkInlineAttribute(string type, string method)
    {
        Type = type;
        Method = method;
    }
}
