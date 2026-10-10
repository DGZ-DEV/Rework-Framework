using System;

namespace Rework;

/// <summary>
/// Marca un método estático como callback reactivo cuando cambia el valor de un campo inyectado ([ReworkField]).
///
/// Mecanismo REAL (§48 — doc honesta): la AUTO-DETECCIÓN es POLLING por tick —
/// ReworkWatchRegistry.AutoTick muestrea cada tick las instancias vivas y compara
/// el valor actual con el anterior por reflexión; si cambia, llama tu método y
/// publica un evento en ReworkBus. Limitación de la auto-detección: solo muestrea
/// instancias de Pawn y Map; campos en otros tipos requieren que notifiques a
/// mano con ReworkWatch.NotifyChanged(target, campo, old, new) desde tu propio
/// código (esa llamada manual no publica el evento al bus).
///
/// Firma esperada del método observado:
///   public static void OnXpChanged(Pawn pawn, float oldValue, float newValue)
/// o simplemente:
///   public static void OnXpChanged(Pawn pawn)
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkWatchAttribute : Attribute
{
    /// <summary>
    /// Nombre exacto del campo o método accessor observado (ej. "Rework_XpMultiplier").
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Tipo contenedor donde reside el campo inyectado (opcional si se infiere del primer parámetro).
    /// </summary>
    public Type? TargetType { get; set; }

    public ReworkWatchAttribute(string fieldName, Type? targetType = null)
    {
        FieldName = fieldName;
        TargetType = targetType;
    }
}

/// <summary>
/// Fachada estática pública accesible desde cualquier mod externo para notificar o consultar cambios observados.
/// </summary>
public static class ReworkWatch
{
    public static Action<object, string, object?, object?>? Notifier { get; set; }

    public static void NotifyChanged(object target, string fieldName, object? oldValue, object? newValue)
    {
        Notifier?.Invoke(target, fieldName, oldValue, newValue);
    }
}

/// <summary>
/// Evento que se publica automáticamente en el ReworkBus cuando un campo observado cambia de valor.
/// </summary>
public sealed class ReworkFieldChangedEvent<TTarget, TVal> : ReworkEventBase
{
    public TTarget Target { get; }
    public string FieldName { get; }
    public TVal OldValue { get; }
    public TVal NewValue { get; }

    public ReworkFieldChangedEvent(TTarget target, string fieldName, TVal oldValue, TVal newValue)
    {
        Target = target;
        FieldName = fieldName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}
