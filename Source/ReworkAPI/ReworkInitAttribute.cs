using System;

namespace Rework;

/// <summary>
/// Marca un método ESTÁTICO para que Rework lo ejecute UNA vez al arrancar (al final
/// de la pasada 2, con el juego ya operativo y tus campos/parches aplicados).
/// Equivalente funcional a los init patches / [PrepatcherInit] de Prepatcher.
///
/// Útil para preparar estado estático propio, tablas de búsqueda, advertencias de
/// compatibilidad, migraciones de datos… con ORDEN y CONTENCIÓN de errores (si uno
/// lanza, se reporta al Rework.log y se sigue; no revienta el boot).
///
/// Orden de ejecución: Priority descendente → orden de carga del mod → nombre.
///   [ReworkInit]
///   public static void Preparar() { ... }
///   [ReworkInit(10)]   // prioridad explícita (mayor primero)
///   public static void Otro() { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkInitAttribute : Attribute
{
    /// <summary>Prioridad de ejecución (mayor → primero). Default 0.</summary>
    public int Priority { get; set; }

    public ReworkInitAttribute()
    {
    }

    /// <summary>Conveniencia: [ReworkInit(10)] ≡ [ReworkInit(Priority = 10)].</summary>
    public ReworkInitAttribute(int priority)
    {
        Priority = priority;
    }
}