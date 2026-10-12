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
/// Elimina la necesidad de parchar métodos repetidamente o hacer polling en Tick.
///
/// OPTIMIZACIÓN DE RENDEIMIENTO (§53-audit-hooks): los suscriptores se almacenan como
/// delegados compilados (Delegate.CreateDelegate / Action&lt;T&gt; directo) en vez de
/// MethodInfo, de modo que Publish<T> invoca el delegado directamente en lugar de
/// MethodInfo.Invoke (reflexión) en cada llamada. Esto elimina el coste de
/// MethodInfo.Invoke + la allocación de object[] por evento publicado.
/// </summary>
public static class ReworkBus
{
    private class Subscription
    {
        /// <summary>Delegado compilado (Action&lt;T&gt;) que reemplaza a MethodInfo.Invoke.
        /// Se resuelve UNA vez en Subscribe/&lt;T&gt; o Register, no en cada Publish.</summary>
        public Delegate? Handler;

        /// <summary>MethodInfo de respaldo (solo si Delegate.CreateDelegate falló).</summary>
        public MethodInfo? FallbackMethod;
        public object? Target;

        public int Priority;
    }

    private static readonly ConcurrentDictionary<Type, List<Subscription>> subscriptions = new();
    private static int totalSubscriptions = 0;

    /// <summary>Número total de suscripciones activas (para early-out rápido).</summary>
    public static int SubscriptionCount => totalSubscriptions;

    /// <summary>
    /// Suscribe una acción fuertemente tipada.
    /// El delegado se almacena directamente — sin MethodInfo.Invoke en el camino caliente.
    /// </summary>
    public static void Subscribe<T>(Action<T> handler, int priority = 0) where T : IReworkEvent
    {
        if (handler == null) return;
        var sub = new Subscription
        {
            Handler = handler,
            Target = handler.Target,
            Priority = priority
        };
        AddSubscription(typeof(T), sub);
    }

    /// <summary>
    /// Registra automáticamente todos los métodos decorados con [ReworkOn] en un objeto o clase estática.
    /// Los métodos se convierten a delegados compilados (Delegate.CreateDelegate) en vez de
    /// almacenar MethodInfo e invocar por reflación en cada Publish.
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
                        Target = instance,
                        Priority = attr.Priority
                    };

                    // Cachear delegado compilado en vez de MethodInfo (§53-audit-hooks).
                    // Intentamos crear Action<evtType>; si falla (firma incompatible),
                    // caemos al MethodInfo de respaldo.
                    try
                    {
                        var actionType = typeof(Action<>).MakeGenericType(evtType);
                        var del = method.IsStatic
                            ? Delegate.CreateDelegate(actionType, method)
                            : Delegate.CreateDelegate(actionType, instance, method);
                        if (del != null)
                        {
                            sub.Handler = del;
                        }
                        else
                        {
                            sub.FallbackMethod = method;
                        }
                    }
                    catch
                    {
                        sub.FallbackMethod = method;
                    }

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
        // Contador simple para early-out en Publish (evita TryGetValue + ToArray si no hay nada)
        System.Threading.Interlocked.Increment(ref totalSubscriptions);
    }

    /// <summary>
    /// ¿Hay suscriptores para el tipo de evento T? Usado para early-out antes de
    /// alocar el evento o entrar en try/catch (§53-audit-hooks).
    /// </summary>
    public static bool HasSubscribers<T>() where T : IReworkEvent
        => subscriptions.TryGetValue(typeof(T), out var list) && list.Count > 0;

    /// <summary>
    /// §48 — Hook de error conectable: ReworkMod lo cablea a Lg.Error. Antes, las
    /// excepciones de los manejadores se tragaban en SILENCIO (catch vacío) y un
    /// handler roto era invisible fuera de builds DEBUG.
    /// </summary>
    public static System.Action<string, System.Exception>? HandlerError;

    /// <summary>
    /// Publica un evento a todos los suscriptores registrados.
    /// Invoca delegados compilados directamente (sin MethodInfo.Invoke en el hot path).
    /// </summary>
    public static void Publish<T>(T evt) where T : IReworkEvent
    {
        if (evt == null || totalSubscriptions == 0) return;
        Type evtType = evt.GetType();

        if (subscriptions.TryGetValue(evtType, out var list))
        {
            Subscription[] snapshot;
            lock (list) { snapshot = list.ToArray(); }

            foreach (var sub in snapshot)
            {
                if (sub.Handler is Action<T> typed)
                {
                    // Camino rápido: delegado fuertemente tipado, sin boxing ni reflexión
                    try { typed(evt); }
                    catch (System.Exception e)
                    {
                        // §48: error ruidoso vía hook conectable (ReworkMod lo cablea a Lg)
                        try { HandlerError?.Invoke($"[ReworkBus] Manejador '{typed.Method.Name}' falló al procesar {evtType.Name}", e); }
                        catch { /* el log nunca debe romper el dispatch */ }
                    }
                }
                else if (sub.FallbackMethod != null)
                {
                    // Respaldo: MethodInfo.Invoke (solo si CreateDelegate falló en registro)
                    try
                    {
                        sub.FallbackMethod.Invoke(sub.Target, new object[] { evt });
                    }
                    catch (System.Exception e)
                    {
                        try { HandlerError?.Invoke($"[ReworkBus] Manejador '{sub.FallbackMethod.Name}' falló al procesar {evtType.Name}", e); }
                        catch { /* el log nunca debe romper el dispatch */ }
                    }
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
        System.Threading.Interlocked.Exchange(ref totalSubscriptions, 0);
    }
}
