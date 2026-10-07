using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Rework.Core;

/// <summary>
/// Bloque 4.1/4.4–4.8: añade INTERFACES (definidas por el mod) a tipos del juego.
///
/// ⚠️ DESHABILITADO (NO se usa en el pipeline — ver GameProcessing): añadir una interfaz
/// de un mod a una clase del juego (p.ej. Verse.Pawn) rompe la vtable en runtime:
/// `TypeLoadException: VTable setup of type Verse.Pawn failed` + `Could not load type
/// 'Verse.Pawn[]'`. Causa: la interfaz vive en el ensamblado del mod, que se RECARGA
/// (identidad duplicada: original refonly + copia nueva), y Mono no la resuelve a tiempo
/// al construir la vtable de la clase. Es una limitación estructural del modelo en
/// memoria, no un fallo puntual. Se conserva este archivo como registro del hallazgo.
/// Los ATRIBUTOS (4.10/4.11/4.12) SÍ funcionan (no tocan la vtable).
///
/// La interfaz debe estar declarada en el ensamblado del mod (con [ReworkInterface(Type)]).
/// </summary>
internal static class InterfaceInjector
{
    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            // Recorrer tipos de nivel superior Y anidados (una interfaz [ReworkInterface]
            // puede vivir dentro de una clase estática del mod).
            foreach (var iface in GetAllInterfaces(asm.ModuleDefinition.Types))
            {
                var ca = iface.CustomAttributes.FirstOrDefault(
                    a => a.AttributeType.FullName == "Rework.ReworkInterfaceAttribute");
                if (ca == null)
                    continue;

                var typeName = ReadStringProp(ca, "Type") ?? ReadCtorString(ca);
                if (string.IsNullOrEmpty(typeName))
                {
                    Lg.Error($"ReworkInterface {iface.FullName}: falta Type.");
                    continue;
                }

                var targetType = module.Types.FirstOrDefault(t => t.FullName == typeName && !t.IsInterface);
                if (targetType == null)
                {
                    Lg.Error($"ReworkInterface {iface.FullName}: tipo destino '{typeName}' no hallado.");
                    continue;
                }

                try
                {
                    // Límites v1: nada de genéricos ni nested en la interfaz.
                    if (iface.GenericParameters.Count > 0)
                    {
                        Lg.Error($"ReworkInterface {iface.FullName}: v1 no soporta interfaces genéricas; se omite.");
                        continue;
                    }

                    // Ya está registrada → omitir.
                    if (targetType.Interfaces.Any(i => i.InterfaceType.FullName == iface.FullName || i.InterfaceType.Name == iface.Name))
                    {
                        Lg.Info($"ReworkInterface {iface.FullName}: ya registrada en {targetType.FullName}; se omite.");
                        continue;
                    }

                    // Validar los miembros de la interfaz (4.9): cada miembro debe existir
                    // como método en el tipo destino (los [ReworkMethod] se añadieron antes
                    // en el pipeline) o como propiedad.
                    var missing = new List<string>();
                    foreach (var m in iface.Methods)
                    {
                        bool ok = targetType.Methods.Any(xm =>
                            xm.Name == m.Name && xm.Parameters.Count == m.Parameters.Count);
                        if (!ok)
                            missing.Add(m.Name);
                    }
                    foreach (var p in iface.Properties)
                    {
                        bool ok = targetType.Properties.Any(xp => xp.Name == p.Name);
                        if (!ok)
                            missing.Add(p.Name);
                    }
                    if (missing.Count > 0)
                    {
                        Lg.Error($"ReworkInterface {iface.FullName}: {targetType.FullName} NO implementa " +
                                 $"los miembros [{string.Join(", ", missing)}]. Añádelos con [ReworkMethod] " +
                                 "o renombra; la interfaz se omite.");
                        continue;
                    }

                    // Registrar la interfaz (importada al módulo destino).
                    var ifaceRef = module.ImportReference(iface);
                    targetType.Interfaces.Add(new InterfaceImplementation(ifaceRef));
                    asmCSharp.Modified = true;
                    Lg.Info($"Interfaz añadida: {targetType.FullName} : {iface.FullName} (miembros validados ✓)");
                }
                catch (Exception e)
                {
                    Lg.Error($"ReworkInterface {iface.FullName}: no se pudo añadir: {e.Message}");
                }
            }
        }
    }

    /// <summary>Interfaces de una colección de tipos, incluyendo las anidadas.</summary>
    private static IEnumerable<TypeDefinition> GetAllInterfaces(IEnumerable<TypeDefinition> inTypes)
    {
        foreach (var t in inTypes)
        {
            if (t.IsInterface)
                yield return t;
            foreach (var nt in t.NestedTypes)
            {
                if (nt.IsInterface)
                    yield return nt;
            }
        }
    }

    private static string? ReadStringProp(CustomAttribute ca, string name)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is string s)
                return s;
        return null;
    }

    private static string? ReadCtorString(CustomAttribute ca)
    {
        if (ca.ConstructorArguments.Count > 0 && ca.ConstructorArguments[0].Value is string s)
            return s;
        return null;
    }
}