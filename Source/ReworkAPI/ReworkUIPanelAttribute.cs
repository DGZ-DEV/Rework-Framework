using System;

namespace Rework;

/// <summary>
/// Marca un método estático del mod como PANEL DE UI que se inyecta dentro del
/// DoWindowContents(Rect) de una ventana del juego (invención "[ReworkUIPanel] —
/// UI Inyectada").
///
/// Mecánica: en la pasada 1 (reescritura en memoria con Mono.Cecil, antes de que
/// el juego use Assembly-CSharp) se inserta la llamada al método del mod dentro del
/// DoWindowContents del tipo destino. Sin XML, sin Window propia, sin ThingComp y
/// sin escribir un [ReworkPatch] manual: el panel se dibuja con el GUI estándar de
/// Verse (Widgets/Text) CADA FRAME mientras la ventana esté abierta.
///
/// Firmas soportadas del método del mod:
///   static void MiPanel(Window window, Rect inRect)  → recibe la ventana y el rect de contenido
///   static void MiPanel(Rect inRect)                 → solo el rect de contenido
///
/// Propiedades:
///   AtStart  (default false): true → la llamada se inserta al INICIO del
///            DoWindowContents (el panel se dibuja ANTES que el contenido de la
///            ventana, queda debajo); false → se inserta justo antes del ret final
///            (se dibuja DESPUÉS, por encima del contenido — visible sobre la
///            ventana).
///   Priority : orden entre varios paneles sobre la misma ventana (mayor primero,
///            estable por orden de carga y nombre).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkUIPanelAttribute : Attribute
{
    /// <summary>Tipo de la ventana destino en Assembly-CSharp (FullName, ej.
    /// "RimWorld.MainTabWindow_Work" o "Verse.Dialog_MessageBox").</summary>
    public string WindowType { get; }

    /// <summary>true = se dibuja antes que el contenido de la ventana (debajo);
    /// false (default) = se dibuja después, por encima del contenido.</summary>
    public bool AtStart { get; set; }

    /// <summary>Orden entre paneles de la misma ventana (mayor primero).</summary>
    public int Priority { get; set; }

    public ReworkUIPanelAttribute(string windowType)
    {
        WindowType = windowType;
    }
}