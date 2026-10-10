using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Rework.Core;

/// <summary>
/// Utilidades compartidas por los procesadores de cirugía IL (bloque 24):
/// ReworkRedirect (call-sites), ReworkOverride (overrides virtuales),
/// ReworkUnlock (visibilidad), ReworkInline (inline de getters triviales) y
/// ReworkConst (plegado de constantes).
///
/// Todos comparten las mismas invariantes del motor:
///  - solo se reescriben ensamblados que PARTICIPAN EN EL SWAP (SourceAssembly != null):
///    Assembly-CSharp y los ensamblados de mods. Los de sistema (UnityEngine,
///    mscorlib...) se añadieron solo para resolver referencias y JAMÁS se tocan;
///  - los atributos se leen por Cecil en los ensamblados con ProcessAttributes;
///  - toda modificación marca asm.Modified = true (necesario para el reload).
/// </summary>
internal static class ILSurgeryCommon
{
    /// <summary>
    /// Ensamblados donde se puede reescribir IL: los que se recargan en el swap.
    /// Assembly-CSharp primero (determinismo), después el resto (set order).
    /// </summary>
    internal static IEnumerable<ModifiableAssembly> RewritableAssemblies(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        yield return asmCSharp;
        foreach (var a in set.AllAssemblies)
        {
            if (a == asmCSharp) continue;
            if (a.SourceAssembly == null) continue; // sistema/motor: solo resolución de refs
            yield return a;
        }
    }

    /// <summary>Todos los tipos del módulo, incluidos los anidados (recursivo).</summary>
    internal static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var t in module.Types)
        {
            yield return t;
            foreach (var n in Nested(t))
                yield return n;
        }
    }

    private static IEnumerable<TypeDefinition> Nested(TypeDefinition t)
    {
        if (!t.HasNestedTypes) yield break;
        foreach (var n in t.NestedTypes)
        {
            yield return n;
            foreach (var nn in Nested(n))
                yield return nn;
        }
    }

    /// <summary>Busca un tipo por FullName en un módulo (incluye anidados).</summary>
    internal static TypeDefinition? FindType(ModuleDefinition module, string fullName) =>
        AllTypes(module).FirstOrDefault(t => t.FullName == fullName);

    /// <summary>
    /// Busca un tipo por FullName en el ensamblado destino indicado y, si no está,
    /// en el resto de ensamblados REESCRIBIBLES (p. ej. un mod). Nunca en los de
    /// sistema. Devuelve también el ensamblado dueño (para marcar Modified).
    /// </summary>
    internal static (ModifiableAssembly Asm, TypeDefinition Type)? FindTypeInRewritable(
        AssemblySet set, ModifiableAssembly asmCSharp, string fullName)
    {
        var t = FindType(asmCSharp.ModuleDefinition, fullName);
        if (t != null) return (asmCSharp, t);

        foreach (var a in RewritableAssemblies(set, asmCSharp))
        {
            if (a == asmCSharp) continue;
            t = FindType(a.ModuleDefinition, fullName);
            if (t != null) return (a, t);
        }
        return null;
    }

    /// <summary>Nombre del "mod" que declara un atributo (para ReworkDevMode.Filter),
    /// siguiendo el mismo criterio que FreePatcher: namespace del tipo en runtime,
    /// o namespace Cecil si el tipo aún no se puede resolver.</summary>
    internal static string ModIdentity(ModifiableAssembly asm, TypeDefinition? declaringType)
    {
        if (asm.SourceAssembly != null && declaringType != null)
        {
            try
            {
                var rtType = asm.SourceAssembly.GetType(declaringType.FullName, throwOnError: false);
                if (rtType != null) return rtType.Namespace ?? declaringType.Namespace ?? asm.OwnerName;
            }
            catch { /* sin SourceAssembly: fallback abajo */ }
        }
        return declaringType?.Namespace ?? asm.OwnerName;
    }

    /// <summary>Apellido del declarante para mensajes de log: "Tipo::Metodo" o "Tipo".</summary>
    internal static string Where(TypeDefinition? type, MethodDefinition? method) =>
        method != null ? $"{type?.FullName}::{method.Name}" : type?.FullName ?? "(ensamblado)";

    // ---------- Lectura de propiedades de CustomAttribute ----------

    internal static string? ReadStringProp(CustomAttribute ca, string name)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is string s)
                return s;
        return null;
    }

    internal static bool ReadBoolProp(CustomAttribute ca, string name, bool def)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is bool b)
                return b;
        return def;
    }

    /// <summary>
    /// Desenvuelve un valor de atributo object: los argumentos tipados llegan
    /// envueltos en CustomAttributeArgument (a veces anidados); esto devuelve el
    /// valor en bruto (int, string, etc.).
    /// </summary>
    internal static object? Unwrap(object? v)
    {
        while (v is CustomAttributeArgument caa)
            v = caa.Value;
        return v;
    }

    /// <summary>
    /// Recorre la cadena de clases base (por Cecil) y devuelve true si
    /// derived.FullName == baseName en algún punto.
    /// </summary>
    internal static bool IsOrDerivesFrom(TypeDefinition derived, string baseFullName)
    {
        var t = (TypeReference?)derived;
        int guard = 0;
        while (t != null && guard++ < 64)
        {
            if (t.FullName == baseFullName) return true;
            try { t = t.Resolve()?.BaseType; }
            catch { return false; }
        }
        return false;
    }
}
