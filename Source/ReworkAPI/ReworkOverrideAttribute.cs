using System;

namespace Rework;

/// <summary>
/// OVERRIDE VIRTUAL REAL (bloque 24.2): inyecta en una clase del juego un método
/// que SOBRESCRIBE de verdad un método virtual de su clase base (ocupa el slot de
/// la vtable antes de que el tipo cargue). El juego lo llama por despacho virtual
/// normal, como si lo hubiera escrito Ludeon.
///
///   [ReworkOverride(Type = "Verse.Building", CallBase = true)]   // GetInspectString
///   public static string Rework_GetInspectString(Verse.Building b, string baseResult)
///   {
///       return baseResult; // o tu versión: baseResult + "\nMi línea"
///   }
///
/// Firma: tu método debe ser estático; el primer parámetro es el 'this' (la clase
/// que RECIBE el override); después, los parámetros del método base (si tiene) y,
/// con CallBase = true, un ÚLTIMO parámetro con el RESULTADO del método base. Tu
/// tipo de retorno debe coincidir con el del método base (void si es void).
///
/// CallBase = false → el override sustituye por completo al base (tu método no
/// recibe el resultado base). Default: true (encadena con el base).
///
/// Requisitos verificados en la reescritura:
///  - el método base (mismo nombre y firma) debe existir como virtual en alguna
///    clase base de Type, y Type NO debe redeclararlo (para eso está [ReworkPatch]);
///  - el método base no puede ser genérico ni tener parámetros ref/out;
///  - la clase Type debe ser de Assembly-CSharp y no estar sellada.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ReworkOverrideAttribute : Attribute
{
    /// <summary>FullName de la clase que RECIBE el override (p. ej. "Verse.Building"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre del método virtual a sobrescribir. Default: el nombre de tu método
    /// sin el prefijo "Rework_" si lo lleva (p. ej. Rework_GetInspectString → GetInspectString).</summary>
    public string? Name { get; set; }

    /// <summary>true → el override llama primero al base y pasa su resultado como último
    /// parámetro a tu método. false → tu método sustituye al base. Default: true.</summary>
    public bool CallBase { get; set; } = true;

    public ReworkOverrideAttribute()
    {
        Type = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkOverride("Verse.Building")] ≡ [ReworkOverride(Type = "Verse.Building")].</summary>
    public ReworkOverrideAttribute(string type)
    {
        Type = type;
    }
}
