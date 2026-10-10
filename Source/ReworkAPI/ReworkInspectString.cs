using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Marca un método para añadir una línea de información dinámica en la caja de inspección de una entidad
/// (Pawns, Edificios, etc.) sin sobreescribir GetInspectString.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkInspectStringAttribute : Attribute
{
    public string TargetType { get; }

    public ReworkInspectStringAttribute(string targetType = "Pawn")
    {
        TargetType = targetType;
    }
}

/// <summary>
/// Registro y despachador de líneas de inspección declarativas.
/// </summary>
public static class ReworkInspectStringRegistry
{
    private static readonly List<(string targetType, Func<object, string?> handler)> handlers = new();

    /// <summary>Número de manejadores registrados (para early-out en el dispatch).</summary>
    public static int HandlerCount => handlers.Count;

    public static void Register(string targetType, Func<object, string?> handler)
    {
        handlers.Add((targetType, handler));
    }

    public static string GetAppendedString(object target)
    {
        if (target == null) return string.Empty;
        string typeName = target.GetType().Name;
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < handlers.Count; i++)
        {
            var (targetType, handler) = handlers[i];
            if (string.Equals(targetType, typeName, StringComparison.OrdinalIgnoreCase) || targetType == "*")
            {
                try
                {
                    var line = handler(target);
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(line);
                    }
                }
                catch { }
            }
        }
        return sb.ToString();
    }
}
