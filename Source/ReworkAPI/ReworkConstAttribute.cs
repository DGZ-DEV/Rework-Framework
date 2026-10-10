using System;

namespace Rework;

/// <summary>
/// REESCRITURA DE CONSTANTES (bloque 24.5): pliega TODOS los <c>ldsfld</c> de un
/// campo <c>static readonly</c> del juego a un VALOR constante, en todos los
/// ensamblados reescritos. Se ejecuta en la pasada 1: el valor nace ya con tu
/// cambio, en el único momento en que es posible — después del boot, los
/// call-sites ya tienen el valor viejo grabado y ningún runtime-patch puede
/// alcanzarlos (los <c>const</c> de IL ni siquiera existen como lectura de campo:
/// están quemados en cada llamada).
///
///   [ReworkConst(Type = "RimWorld.JobDriver_AcceptRole", Field = "FacingUpdateInterval", Value = 40)]
///   private static void MiConstante() { }   // (portador)
///
/// Soporta valores: bool, int, long, float, double, string (null permitido para
/// tipos de referencia) y campos de enum (valor entero). El .cctor original se
/// conserva (el campo sigue valiendo lo mismo para quien lo lea por reflexión);
/// lo que cambia es lo que el CÓDIGO ve en cada lectura.
///
/// Aviso: cambiar el valor de una constante del juego CAMBIA EL JUEGO (es
/// exactamente su utilidad); verifícalo en partida.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class ReworkConstAttribute : Attribute
{
    /// <summary>FullName del tipo destino (p. ej. "RimWorld.JobDriver_AcceptRole"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre del campo static readonly. Obligatorio.</summary>
    public string Field { get; set; }

    /// <summary>Valor nuevo (bool, int, long, float, double, string o null). Obligatorio.</summary>
    public object? Value { get; set; }

    public ReworkConstAttribute()
    {
        Type = string.Empty;
        Field = string.Empty;
        Value = null;
    }

    /// <summary>Conveniencia: [ReworkConst("Tipo", "Campo", 40)] ≡ propiedades.</summary>
    public ReworkConstAttribute(string type, string field, object? value)
    {
        Type = type;
        Field = field;
        Value = value;
    }
}
