using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Rework;

/// <summary>
/// Marca un método estático o de instancia para suscribirse reactivamente a un evento del ReworkBus.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ReworkOnAttribute : Attribute
{
    public Type? EventType { get; }
    public int Priority { get; set; } = 0; // Mayor prioridad = se ejecuta primero

    public ReworkOnAttribute(Type? eventType = null)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Interfaz base para todos los eventos del bus reactivo.
/// </summary>
public interface IReworkEvent
{
    DateTime Timestamp { get; }
}

/// <summary>
/// Evento base con timestamp automático.
/// </summary>
public abstract class ReworkEventBase : IReworkEvent
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Evento que se dispara en cada Tick del juego.
/// </summary>
public sealed class ReworkGameTickEvent : ReworkEventBase
{
    public int TicksGame { get; }
    public ReworkGameTickEvent(int ticksGame) => TicksGame = ticksGame;
}

/// <summary>
/// Evento de ciclo de vida (inicios, cargas, mapas generados).
/// </summary>
public sealed class ReworkLifecycleEvent : ReworkEventBase
{
    public string Name { get; }
    public object? Context { get; }
    public ReworkLifecycleEvent(string name, object? context = null)
    {
        Name = name;
        Context = context;
    }
}

/// <summary>
/// Bus de eventos reactivo puro (Publish / Subscribe push) para RimWorld.
/// Elimina la necesidad de parchar métodos repetitivamente o hacer polling en Tick.
/// </summary>
public static class ReworkBus
{
    private class Subscription
    {
        public MethodInfo Method = null!;
        public object? Target;
        public int Priority;
    }

    private static readonly ConcurrentDictionary<Type, List<Subscription>> subscriptions = new();

    /// <summary>
    /// Suscribe una acción fuertemente tipada.
    /// </summary>
    public static void Subscribe<T>(Action<T> handler, int priority = 0) where T : IReworkEvent
    {
        if (handler == null) return;
        var sub = new Subscription
        {
            Method = handler.Method,
            Target = handler.Target,
            Priority = priority
        };
        AddSubscription(typeof(T), sub);
    }

    /// <summary>
    /// Registra automáticamente todos los métodos decorados con [ReworkOn] en un objeto o clase estática.
    /// </summary>
    public static void Register(object targetOrType)
    {
        if (targetOrType == null) return;
        Type type = targetOrType is Type t ? t : targetOrType.GetType();
        object? instance = targetOrType is Type ? null : targetOrType;

        var flags = BindingFlags.Public | BindingFlags.NonPublic | (instance != null ? BindingFlags.Instance : BindingFlags.Static);
        foreach (var method in type.GetMethods(flags))
        {
            var attrs = method.GetCustomAttributes<ReworkOnAttribute>();
            foreach (var attr in attrs)
            {
                Type? evtType = attr.EventType;
                if (evtType == null)
                {
                    var pars = method.GetParameters();
                    if (pars.Length > 0 && typeof(IReworkEvent).IsAssignableFrom(pars[0].ParameterType))
                    {
                        evtType = pars[0].ParameterType;
                    }
                }

                if (evtType != null)
                {
                    var sub = new Subscription
                    {
                        Method = method,
                        Target = instance,
                        Priority = attr.Priority
                    };
                    AddSubscription(evtType, sub);
                }
            }
        }
    }

    private static void AddSubscription(Type eventType, Subscription sub)
    {
        subscriptions.AddOrUpdate(eventType,
            _ => new List<Subscription> { sub },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(sub);
                    list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                }
                return list;
            });
    }

    /// <summary>
    /// Publica un evento a todos los suscriptores registrados.
    /// </summary>
    public static void Publish<T>(T evt) where T : IReworkEvent
    {
        if (evt == null) return;
        Type evtType = evt.GetType();

        if (subscriptions.TryGetValue(evtType, out var list))
        {
            Subscription[] snapshot;
            lock (list) { snapshot = list.ToArray(); }

            foreach (var sub in snapshot)
            {
                try
                {
                    sub.Method.Invoke(sub.Target, new object[] { evt });
                }
                catch (Exception e)
                {
                    // Manejo seguro en API pura sin acoplamiento
                    #if DEBUG
                    Console.WriteLine($"[ReworkBus] Error al procesar evento {evtType.Name} en {sub.Method.Name}: {e.InnerException ?? e}");
                    #endif
                }
            }
        }
    }

    /// <summary>
    /// Limpia todas las suscripciones (útil para reload o tests).
    /// </summary>
    public static void Clear()
    {
        subscriptions.Clear();
    }
}
