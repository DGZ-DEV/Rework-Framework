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
/// </summary>
public static class ReworkScheduler
{
    private class ScheduleEntry
    {
        public MethodInfo Method = null!;
        public int IntervalTicks;
        public int LastExecutedTick;
    }

    private static readonly List<ScheduleEntry> entries = new();

    public static void Register(MethodInfo method, int intervalTicks)
    {
        if (method == null || intervalTicks <= 0) return;
        entries.Add(new ScheduleEntry
        {
            Method = method,
            IntervalTicks = intervalTicks,
            LastExecutedTick = 0
        });
    }

    public static void OnGameTick(int currentTick)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (currentTick - e.LastExecutedTick >= e.IntervalTicks)
            {
                e.LastExecutedTick = currentTick;
                try
                {
                    e.Method.Invoke(null, null);
                }
                catch
                {
                    // Fallback silencioso en API pública
                }
            }
        }
    }
}
