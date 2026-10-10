using System;

namespace Rework;

/// <summary>
/// Contrato para clases que manejan estado en caliente durante un ciclo de ReworkLive.
/// Permite limpiar suscripciones, temporizadores y referencias antes del hot-reload,
/// y restaurar el estado limpio después de recargar.
/// </summary>
public interface IReworkLiveReloadable
{
    /// <summary>
    /// Se invoca ANTES de descargar/sustituir la lógica anterior.
    /// Debe cancelar tareas en background, desuscribir eventos y limpiar cachés.
    /// </summary>
    void OnBeforeLiveReload();

    /// <summary>
    /// Se invoca DESPUÉS de que la nueva lógica está lista y cargada.
    /// Debe re-inicializar el estado y re-conectar eventos.
    /// </summary>
    void OnAfterLiveReload();
}

/// <summary>
/// Marca una clase para ser descubierta y gestionada automáticamente por el monitor ReworkLive.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkLiveAttribute : Attribute
{
    /// <summary>Identificador del módulo o mod monitoreado.</summary>
    public string ModuleId { get; set; } = "Default";

    public ReworkLiveAttribute(string moduleId = "Default")
    {
        ModuleId = moduleId;
    }
}

/// <summary>
/// Fachada pública de ReworkLive para registrar manejadores y consultar estado desde cualquier mod.
/// </summary>
public static class ReworkLive
{
    public static event Action? BeforeReload;
    public static event Action? AfterReload;

    public static void TriggerBeforeReload() => BeforeReload?.Invoke();
    public static void TriggerAfterReload() => AfterReload?.Invoke();
}

