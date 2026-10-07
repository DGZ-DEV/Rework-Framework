using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Rework.Core;

/// <summary>
/// Bloque 4.10/4.11/4.12: añade ATRIBUTOS (metadata) a clases/métodos/campos existentes.
///
/// Lee [ReworkAnnotate(Type, Member, Attribute, CtorArgs)] de los métodos del mod:
///   - Member == null → anota la CLASE.
///   - Member → busca el método o campo con ese nombre y lo anota.
/// El atributo inyectado puede ser uno propio del mod (resuelto por nombre) o el
/// marcador genérico Rework.Core.ReworkMarkAttribute si no se indica.
/// </summary>
internal static class AnnotateInjector
{
    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            foreach (var type in asm.ModuleDefinition.Types)
            {
                foreach (var m in type.Methods)
                {
                    var ca = m.CustomAttributes.FirstOrDefault(
                        a => a.AttributeType.FullName == "Rework.ReworkAnnotateAttribute");
                    if (ca == null)
                        continue;

                    var typeName = ReadStringProp(ca, "Type") ?? ReadCtorString(ca);
                    var memberName = ReadStringProp(ca, "Member");
                    var attrName = ReadStringProp(ca, "Attribute");
                    var ctorArgs = ReadCtorArgs(ca);
                    if (string.IsNullOrEmpty(typeName))
                    {
                        Lg.Error($"ReworkAnnotate {m.FullName}: falta Type.");
                        continue;
                    }

                    var targetType = module.Types.FirstOrDefault(t => t.FullName == typeName);
                    if (targetType == null)
                    {
                        Lg.Error($"ReworkAnnotate {m.FullName}: tipo destino '{typeName}' no hallado.");
                        continue;
                    }

                    try
                    {
                        // Resolver el tipo del atributo a inyectar.
                        var attrTypeRef = ResolveAttributeType(attrName, module);
                        if (attrTypeRef == null)
                        {
                            Lg.Error($"ReworkAnnotate {m.FullName}: no se pudo resolver el atributo " +
                                     $"'{attrName ?? "(genérico)"}'; se omite.");
                            continue;
                        }

                        var ic = BuildAttribute(attrTypeRef, ctorArgs, module);

                        if (string.IsNullOrEmpty(memberName))
                        {
                            if (targetType.CustomAttributes.Any(a => a.AttributeType.FullName == attrTypeRef.FullName))
                            {
                                Lg.Info($"ReworkAnnotate: {targetType.FullName} ya tiene {attrTypeRef.Name}; se omite.");
                                continue;
                            }
                            targetType.CustomAttributes.Add(ic);
                            Lg.Info($"Atributo añadido a clase: {targetType.FullName} ← {attrTypeRef.Name}");
                        }
                        else
                        {
                            var targetMethod = targetType.Methods.FirstOrDefault(x => x.Name == memberName);
                            var targetField = targetType.Fields.FirstOrDefault(x => x.Name == memberName);
                            if (targetMethod != null)
                            {
                                targetMethod.CustomAttributes.Add(ic);
                                Lg.Info($"Atributo añadido a método: {targetType.FullName}::{memberName} ← {attrTypeRef.Name}");
                            }
                            else if (targetField != null)
                            {
                                targetField.CustomAttributes.Add(ic);
                                Lg.Info($"Atributo añadido a campo: {targetType.FullName}.{memberName} ← {attrTypeRef.Name}");
                            }
                            else
                            {
                                Lg.Error($"ReworkAnnotate {m.FullName}: no se halló '{targetType.FullName}::{memberName}'; se omite.");
                                continue;
                            }
                        }
                        asmCSharp.Modified = true;
                    }
                    catch (Exception e)
                    {
                        Lg.Error($"ReworkAnnotate {m.FullName}: no se pudo inyectar: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>Resuelve el tipo del atributo por nombre corto/completo entre los ensamblados
    /// cargados y el juego. Null → no resuelto.</summary>
    private static TypeReference? ResolveAttributeType(string? attrName, ModuleDefinition module)
    {
        // Marcador genérico por defecto (el tipo está en ReworkCore, cargado).
        if (string.IsNullOrEmpty(attrName))
            return module.ImportReference(typeof(ReworkMarkAttribute));

        // Buscar por nombre corto o completo en los tipos de Assembly-CSharp y en los
        // mods procesados (ya importados al módulo).
        try
        {
            var byFull = module.Types.FirstOrDefault(t => t.FullName == attrName);
            if (byFull != null)
                return byFull;
            var byName = module.Types.FirstOrDefault(t => t.Name == attrName && t.IsEnum == false);
            if (byName != null)
                return byName;

            // Fallback: buscar en los ensamblados cargados (mods) y re-importar.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? rt;
                try { rt = asm.GetType(attrName, throwOnError: false); }
                catch { continue; }
                rt ??= asm.GetTypes().FirstOrDefault(t => t.Name == attrName);
                if (rt != null)
                    return module.ImportReference(rt);
            }
        }
        catch
        {
            // sin resolver
        }
        return null;
    }

    private static CustomAttribute BuildAttribute(TypeReference attrType, object[]? ctorArgs, ModuleDefinition module)
    {
        // Buscar un ctor que encaje con los args dados (o el sin args).
        var ctor = attrType.Resolve()?.Methods.FirstOrDefault(cm => cm.IsConstructor && cm.Parameters.Count == (ctorArgs?.Length ?? 0));
        if (ctor == null)
            ctor = attrType.Resolve()?.Methods.FirstOrDefault(cm => cm.IsConstructor);
        if (ctor == null)
            throw new InvalidOperationException($"Sin ctor público para {attrType.FullName}");

        var ic = new CustomAttribute(module.ImportReference(ctor));
        if (ctorArgs != null)
        {
            foreach (var a in ctorArgs)
            {
                var t = module.TypeSystem.Object;
                var val = a;
                // Primitivas: convertir al tipo esperado por el parámetro.
                var paramIdx = ic.ConstructorArguments.Count;
                var paramType = ctor.Parameters[paramIdx].ParameterType;
                ic.ConstructorArguments.Add(new CustomAttributeArgument(module.ImportReference(paramType), ConvertArg(a, paramType)));
            }
        }
        return ic;
    }

    private static object ConvertArg(object a, TypeReference paramType)
    {
        try
        {
            var pt = paramType.Resolve();
            if (pt != null && pt.IsEnum)
            {
                // typeof(int) es el subyacente de la mayoría de enums; si el valor ya es
                // el tipo subyacente correcto se acepta tal cual.
                return a;
            }
        }
        catch { }
        switch (paramType.MetadataType)
        {
            case MetadataType.String: return System.Convert.ToString(a);
            case MetadataType.Int32: return System.Convert.ToInt32(a);
            case MetadataType.Single: return System.Convert.ToSingle(a);
            case MetadataType.Double: return System.Convert.ToDouble(a);
            case MetadataType.Boolean: return System.Convert.ToBoolean(a);
            default: return a;
        }
    }

    private static string? ReadStringProp(CustomAttribute ca, string name)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is string s)
                return s;
        return null;
    }

    private static object[]? ReadCtorArgs(CustomAttribute ca)
    {
        foreach (var p in ca.Properties)
        {
            if (p.Name != "CtorArgs" || p.Argument.Value == null)
                continue;
            var v = p.Argument.Value;
            // Puede llegar como CustomAttributeArgument[] (array).
            if (v is CustomAttributeArgument[] arr)
                return arr.Select(x => x.Value).ToArray();
            if (v is object[] oa)
                return oa;
            return new[] { v };
        }
        return null;
    }

    private static string? ReadCtorString(CustomAttribute ca)
    {
        if (ca.ConstructorArguments.Count > 0 && ca.ConstructorArguments[0].Value is string s)
            return s;
        return null;
    }
}