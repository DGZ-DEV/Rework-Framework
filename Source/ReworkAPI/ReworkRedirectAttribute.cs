using System;

namespace Rework;

/// <summary>
/// REDIRECCIÓN GLOBAL DE CALL-SITES (bloque 24.1): reescribe TODAS las llamadas a un
/// método del juego para que apunten a tu método, en TODOS los ensamblados que Rework
/// reescribe (Assembly-CSharp + mods), durante la pasada 1 (antes de que el juego
/// arranque). Harmony solo puede desviar el método DESTINO; aquí se sustituye cada
/// punto de LLAMADA, con coste cero en runtime (el IL final ya apunta a tu método).
///
///   [ReworkRedirect(Type = "Verse.Log", Method = "Message")]
///   public static void InterceptarMensaje(string text)
///   {
///       // tu lógica (contador, filtro, enrutado...)
///       Verse.Log.Message(text); // llamada al ORIGINAL: Rework NO reescribe las
///                                // llamadas dentro del propio método redirector
///   }
///
/// Reglas de la firma: tu método debe ser estático y coincidir EXACTAMENTE con la
/// firma del método destino (mismos tipos de parámetros por nombre completo y mismo
/// tipo de retorno). Si el destino es de INSTANCIA, el primer parámetro de tu método
/// recibe el 'this' (el tipo debe ser el declarante o una de sus clases base).
/// El método destino se busca en Assembly-CSharp por defecto; usa Assembly para
/// apuntar a otro ensamblado cargado (p. ej. otro mod).
///
/// ¡Ojo!: un campo/method de un ensamblado que Rework NO reescribe (UnityEngine,
/// mscorlib, etc.) conserva sus llamadas originales: la reescritura cubre el juego
/// y los mods.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ReworkRedirectAttribute : Attribute
{
    /// <summary>FullName del tipo que DECLARA el método destino (p. ej. "Verse.Log"). Obligatorio.</summary>
    public string Type { get; set; }

    /// <summary>Nombre del método destino (p. ej. "Message"). Obligatorio.</summary>
    public string Method { get; set; }

    /// <summary>Nombre simple del ensamblado donde vive el destino. Default: Assembly-CSharp.</summary>
    public string? Assembly { get; set; }

    public ReworkRedirectAttribute()
    {
        Type = string.Empty;
        Method = string.Empty;
    }

    /// <summary>Conveniencia: [ReworkRedirect("Verse.Log", "Message")] ≡ propiedades Type/Method.</summary>
    public ReworkRedirectAttribute(string type, string method)
    {
        Type = type;
        Method = method;
    }
}
