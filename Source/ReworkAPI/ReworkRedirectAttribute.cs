using System;

namespace Rework;

/// <summary>
/// Reescribe todas las llamadas a un método del juego para que apunten a tu método,
/// en todos los ensamblados que Rework reescribe (Assembly-CSharp + mods), durante la
/// pasada 1, antes de que el juego arranque. El IL final apunta a tu método: no queda
/// ningún desvío en runtime.
///
///   [ReworkRedirect(Type = "Verse.Log", Method = "Message")]
///   public static void InterceptarMensaje(string text)
///   {
///       // tu lógica (contador, filtro, enrutado...)
///       Verse.Log.Message(text); // llamada al original: Rework no reescribe las
///                                // llamadas dentro del propio método redirector
///   }
///
/// Reglas de la firma: tu método debe ser estático y coincidir exactamente con la
/// firma del método destino (mismos tipos de parámetros por nombre completo y mismo
/// tipo de retorno). Si el destino es de instancia, el primer parámetro de tu método
/// recibe el 'this' (el tipo debe ser el declarante o una de sus clases base).
/// El método destino se busca en Assembly-CSharp por defecto; usa Assembly para
/// apuntar a otro ensamblado cargado (p. ej. otro mod).
///
/// Aviso: los call-sites que viven en un ensamblado que Rework no reescribe
/// (UnityEngine, mscorlib, etc.) conservan la llamada original — la reescritura cubre
/// el juego y los mods.
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
