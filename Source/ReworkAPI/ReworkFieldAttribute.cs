using System;

namespace Rework;

/// <summary>
/// Marca el accessor de un campo que Rework añadirá a un tipo del juego.
/// El tipo destino es el primer parámetro del método (con 'this').
///
/// MEJORA #1 de Rework sobre Prepatcher (Source/API/PrepatcherFieldAttribute.cs):
/// el nombre del campo será EXACTAMENTE el nombre del método accessor.
/// Prepatcher genera nombres automáticos ("asmShortName + accessorName + MetadataToken.RID",
/// ver Source/Implementation/Process/FieldAdder.cs de Zetrith); Rework deja que
/// el modder controle el nombre (p.ej. "Rework_Corrupcion").
///
/// Default (inicializador): si se indica, Rework inyecta en TODOS los constructores
/// del tipo destino el valor inicial del campo (como un inicializador de C#:
/// "public float x = 1.0f;"). Sin esto, el campo nace en el default de CLR (0/null).
///   [ReworkField(Default = 1.0f)]
///   public static extern ref float Multiplicador(this Pawn p);
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkFieldAttribute : Attribute
{
    /// <summary>
    /// Valor inicial del campo, inyectado en los constructores del tipo destino.
    /// Primitivas (int, float, double, long, bool), string y enums.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// Inicializador POR MÉTODO (estilo Prepatcher ValueInitializer / PatchCtorsWithInitializer):
    /// nombre de un método ESTÁTICO PÚBLICO en la MISMA clase estática que el accessor, que
    /// devuelve el valor inicial del campo. Útil para tipos complejos (List&lt;&gt;, estado).
    ///
    /// La inyección llama a ese método al inicio de cada constructor:
    ///   - 0 parámetros → campo = Metodo();
    ///   - 1 parámetro   → recibe la instancia recién creada → campo = Metodo(this);
    ///
    /// Tiene PRIORIDAD sobre Default (si ambos están, gana este).
    ///   [ReworkField(Initializer = "CrearInventario")]
    ///   public static extern ref List&lt;Item&gt; Inventario(this Pawn p);
    ///   public static List&lt;Item&gt; CrearInventario() => new();
    /// </summary>
    public string? Initializer { get; set; }

    public ReworkFieldAttribute()
    {
    }

    /// <summary>Conveniencia: [ReworkField(1.0f)] ≡ [ReworkField(Default = 1.0f)].</summary>
    public ReworkFieldAttribute(object defaultValue)
    {
        Default = defaultValue;
    }

    /// <summary>
    /// Serialización AUTOMÁTICA del campo en el guardado (Scribe).
    /// Si es true, Rework inyecta la llamada de guardado/carga en el método ExposeData
    /// del tipo destino (p.ej. Verse.Pawn.ExposeData): el campo pasa a viajar en la
    /// partida guardada SIN que el mod escriba un parche Scribe a mano.
    ///
    /// Tipos soportados (v1):
    ///   - Primitivas (int, float, double, long, bool...), string y enums
    ///     → Scribe_Values.Look.
    ///   - List&lt;primitiva/string/enum&gt; → Scribe_Collections.Look (LookMode.Value).
    ///   Cualquier otro tipo se ignorará al cargar (se loguea un error claro) para no
    ///   guardar de forma incorrecta.
    ///
    /// Notas:
    ///   - Solo el mod que CREA el campo (el primero con ese nombre) decide la
    ///     serialización; los mods que se enlazan a un campo compartido no la reafirman.
    ///   - El objetivo es un campo "tipo vanilla": la etiqueta del guardado es el nombre
    ///     del accessor, con forceSave=true y default del CLR.
    ///   [ReworkField(Default = 1.0f, Serialize = true)]
    ///   public static extern ref float Rework_Reputacion(this Pawn p);
    /// </summary>
    public bool Serialize { get; set; }
}
