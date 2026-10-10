using System;

namespace Rework;

/// <summary>
/// CIRUGÍA IL: INLINE DE GETTERS (bloque 24.4): sustituye TODAS las llamadas a un
/// método trivial del juego (cuerpo exacto <c>ldarg.0; ldfld F; ret</c> de
/// instancia, o <c>ldsfld F; ret</c> estático) por el acceso directo al campo, en
/// todos los ensamblados reescritos. El coste de la llamada desaparece del IL
/// final; nadie puede "des-inlinear" un getter caliente mejor que el compilador
/// original, que no lo hizo porque está en otro ensamblado.
///
///   [ReworkInline(Type = "Verse.Pawn", Method = "get_Name")]     // 189 call-sites
///   private static void InlineNombre() { }                       // (portador)
///
/// Salvaguardas (el procesador las verifica y RECHAZA lo que no cumpla):
///  - el método debe existir y su cuerpo debe ser EXACTAMENTE el patrón trivial;
///  - NO se inlinean métodos virtuales no finales (rompería el polimorfismo);
///  - no se inlinean métodos genéricos;
///  - si el método no es trivial, se reporta con error y NO se toca nada.
///
/// Cambio semántico único y aceptado: con callvirt sobre un receptor null, la
/// excepción pasa de lanzarse en la llamada a lanzarse en el acceso al campo —
/// misma NullReferenceException, mismo punto de fallo práctico.
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
