using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Salud del framework (bloque 17.1/17.2/17.12): acumula los fallos que hubo durante la
/// pasada 1 (reescritura) y los muestra al usuario en la pasada 2 como un resumen claro.
/// Cada parche/init va envuelto en try/catch (recuperación parcial, 17.2): un fallo no
/// rompe el boot, solo se registra aquí.
/// </summary>
public static class ReworkHealth
{
    private static readonly List<(string component, string message)> failures = new();
    private static readonly object gate = new();

    /// <summary>Registra un fallo de un componente (parche/init/hook).</summary>
    public static void RecordFailure(string component, string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        lock (gate)
            failures.Add((component, message));
    }

    /// <summary>Nº de fallos registrados.</summary>
    public static int FailureCount
    {
        get { lock (gate) return failures.Count; }
    }

    /// <summary>¿Hubo algún fallo en la pasada 1?</summary>
    public static bool HasFailures => FailureCount > 0;

    /// <summary>Resumen legible de los fallos (para mostrar al usuario, 17.12).</summary>
    public static string Summary()
    {
        lock (gate)
        {
            if (failures.Count == 0)
                return "Rework: 0 fallos.";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Rework: {failures.Count} fallo(s) al reescribir (el boot siguió; ve abajo):");
            foreach (var f in failures)
                sb.AppendLine($"  - [{f.component}] {f.message}");
            return sb.ToString();
        }
    }

    /// <summary>Limpia los acumulados (p.ej. entre sesiones de diagnóstico).</summary>
    public static void Reset()
    {
        lock (gate)
            failures.Clear();
    }
}