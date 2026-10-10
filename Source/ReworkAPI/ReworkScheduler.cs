using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método estático para ser ejecutado periódicamente en base a intervalos de ticks del motor.
/// Elimina la necesidad de crear un GameComponent solo para tener lógica repetitiva en Tick().
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ReworkScheduleAttribute : Attribute
{
    public int EveryTicks { get; set; } = 0;
    public bool EveryDay { get; set; } = false;
    public bool EverySeason { get; set; } = false;

    public ReworkScheduleAttribute(int everyTicks = 0)
    {
        EveryTicks = everyTicks;
    }
}

/// <summary>
/// Scheduler declarativo de ticks del motor.
///
/// OPTIMIZACIÓN DE RENDEIMIENTO (§53-audit-hooks): los métodos programados se convierten
/// a delegados compilados (Action) vía Delegate.CreateDelegate en el momento del registro,
/// en vez de almacenar MethodInfo e invocar por reflación (MethodInfo.Invoke) en cada
/// tick. Esto elimina el coste de MethodInfo.Invoke + object[] allocation por ejecución.
/// </summary>
public static class ReworkScheduler
{
    private class ScheduleEntry
    {
        /// <summary>Delegado compilado (Action) que reemplaza a MethodInfo.Invoke en el hot path.</summary>
        public Action? Handler;

        /// <summary>MethodInfo de respaldo (solo si Delegate.CreateDelegate falló).</summary>
        public MethodInfo? FallbackMethod;

        public int IntervalTicks;
        public int LastExecutedTick;
    }

    private static readonly List<ScheduleEntry> entries = new();

    /// <summary>Número de entradas programadas (para early-out en el dispatcher global).</summary>
    public static int Count => entries.Count;

    public static void Register(MethodInfo method, int intervalTicks)
    {
        if (method == null || intervalTicks <= 0) return;

        // Cachear delegado compilado en vez de MethodInfo (§53-audit-hooks)
        Action? handler = null;
        try
        {
            // Delegate.CreateDelegate devuelve null si la firma no coincide con Action (p.ej. método con parámetros)
            handler = method.IsStatic
                ? Delegate.CreateDelegate(typeof(Action), method) as Action
                : Delegate.CreateDelegate(typeof(Action), null, method) as Action;
        }
        catch
        {
            handler = null;
        }

        var entry = new ScheduleEntry
        {
            Handler = handler,
            FallbackMethod = handler == null ? method : null,
            IntervalTicks = intervalTicks,
            LastExecutedTick = 0
        };
        entries.Add(entry);
    }

    public static void OnGameTick(int currentTick)
    {
        if (entries.Count == 0) return;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (currentTick - e.LastExecutedTick >= e.IntervalTicks)
            {
                e.LastExecutedTick = currentTick;
                if (e.Handler != null)
                {
                    try { e.Handler(); }
                    catch
                    {
                        // Fallback silencioso: un mod programado no debe romper el tick
                    }
                }
                else if (e.FallbackMethod != null)
                {
                    try { e.FallbackMethod.Invoke(null, null); }
                    catch { }
                }
            }
        }
    }
}
