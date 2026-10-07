using System;

namespace Rework;

/// <summary>
/// Marca un "parche libre" ([ReworkPatch], Fase 3): Rework lo invoca DURANTE la
/// reescritura (pasada 1, en memoria), antes de que el juego use los ensamblados.
/// Equivalente a FreePatch de Prepatcher, pero SIN Harmony.
///
/// PRIORIDAD (Fase 4): los parches se aplican en orden de Priority DESCENDENTE
/// (mayor número primero). Default 0. Usa [ReworkPatch(100)] para forzar que un
/// parche corra antes que otro.
///
/// DOS MODOS:
///
/// 1) Modo directo (ModuleDefinition): firma
///      [ReworkPatch]
///      public static void MiParche(ModuleDefinition module)
///    Recibe el ModuleDefinition de Assembly-CSharp y lo modifica con Cecil puro.
///
/// 2) Modo TRANSPILER amigable (Fase 4): indica el método destino con Type+Method
///    y recibe sus instrucciones para devolverlas modificadas (estilo transpiler de
///    Harmony, pero en tiempo de reescritura y con Cecil). Firma:
///      [ReworkPatch(Type = "Verse.Root_Entry", Method = "Start", Priority = 10)]
///      public static IEnumerable<Mono.Cecil.Cil.Instruction> MiTranspilador(
///          IEnumerable<Mono.Cecil.Cil.Instruction> instrs, ModuleDefinition module)
///    Devuelve la lista de instrucciones modificada; Rework reconstruye el cuerpo.
///    (Nota: los manejadores de excepción del método original no se conservan.)
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReworkPatchAttribute : Attribute
{
    /// <summary>Orden de aplicación: mayor número → se aplica antes. Default 0.</summary>
    public int Priority { get; set; }

    /// <summary>FullName del tipo destino (solo modo transpiler).</summary>
    public string? Type { get; set; }

    /// <summary>Nombre del método destino (solo modo transpiler).</summary>
    public string? Method { get; set; }

    public ReworkPatchAttribute()
    {
    }

    /// <summary>Conveniencia: [ReworkPatch(100)] ≡ [ReworkPatch(Priority = 100)].</summary>
    public ReworkPatchAttribute(int priority)
    {
        Priority = priority;
    }
}