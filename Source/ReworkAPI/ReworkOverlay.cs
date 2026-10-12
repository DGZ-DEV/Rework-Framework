using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Sistema de dibujo de capas y semi-transparencias visuales directamente sobre el mapa del juego.
/// Permite renderizar overlays de zonas, alcances y diagnósticos en tiempo real.
/// </summary>
public static class ReworkOverlay
{
    public class OverlayDrawer
    {
        public string Id { get; set; } = "";
        public Action<object> DrawAction { get; set; } = null!;
        public bool Enabled { get; set; } = true;
    }

    private static readonly Dictionary<string, OverlayDrawer> drawers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Número de drawers registrados (para early-out en el dispatch).</summary>
    public static int Count => drawers.Count;

    public static void Register(string id, Action<object> drawAction)
    {
        drawers[id] = new OverlayDrawer { Id = id, DrawAction = drawAction, Enabled = true };
    }

    public static void SetEnabled(string id, bool enabled)
    {
        if (drawers.TryGetValue(id, out var d))
            d.Enabled = enabled;
    }

    public static void RenderAll(object mapContext)
    {
        foreach (var d in drawers.Values)
        {
            if (d.Enabled)
            {
                try { d.DrawAction?.Invoke(mapContext); } catch { }
            }
        }
    }
}

/// <summary>
/// Marca un método estático como un overlay visual inyectable en el mapa del juego.
/// El método debe tener firma <c>void Method(object mapContext)</c> donde <paramref
/// name="mapContext"/> es el <see cref="Verse.Map"/> actual. Se dibuja cada frame
/// durante el pase Repaint de <see cref="Verse.MapComponentUtility.MapComponentOnGUI"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkOverlayAttribute : Attribute
{
    /// <summary>Id único del overlay (para enable/disable vía <see cref="ReworkOverlay.SetEnabled"/>).</summary>
    public string Id { get; }

    /// <summary>Nombre legible opcional (para debugging).</summary>
    public string? Label { get; set; }

    /// <summary>Prioridad de dibujo: mayor = se dibuja encima (por defecto 0).</summary>
    public int Priority { get; set; } = 0;

    public ReworkOverlayAttribute(string id)
    {
        Id = id;
    }
}
