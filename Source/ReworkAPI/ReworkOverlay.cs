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
