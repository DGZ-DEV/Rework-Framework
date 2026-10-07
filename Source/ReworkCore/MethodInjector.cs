using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Rework.Core;

/// <summary>
/// Bloque 3.1/3.2/3.3: añade MÉTODOS y PROPIEDADES reales a tipos del juego.
///
/// Mecánica: el miembro que se añade al tipo destino es un FORWARDER cuyo cuerpo
/// llama al método estático del mod (que vive en el ensamblado del mod, cargado).
/// Toda la lógica queda en el mod; el juego solo gana el miembro. Queda como un
/// miembro REAL: visible por reflexión, invocable desde el juego/otros mods.
/// </summary>
internal static class MethodInjector
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
                    var methodAttr = m.CustomAttributes.FirstOrDefault(
                        a => a.AttributeType.FullName == "Rework.ReworkMethodAttribute");
                    if (methodAttr != null)
                        InjectMethod(asmCSharp, module, m, methodAttr);

                    var propAttr = m.CustomAttributes.FirstOrDefault(
                        a => a.AttributeType.FullName == "Rework.ReworkPropertyAttribute");
                    if (propAttr != null)
                        InjectProperty(asmCSharp, module, m, propAttr);
                }
            }
        }
    }

    // ---------- MÉTODOS (3.1 / 3.2) ----------

    private static void InjectMethod(
        ModifiableAssembly asmCSharp, ModuleDefinition module, MethodDefinition m, CustomAttribute ca)
    {
        var typeName = ReadStringProp(ca, "Type") ?? ReadCtorString(ca);
        var targetName = ReadStringProp(ca, "Name");
        var isInstance = ReadBoolProp(ca, "Instance", true);
        if (string.IsNullOrEmpty(typeName))
        {
            Lg.Error($"ReworkMethod {m.FullName}: falta Type.");
            return;
        }

        var targetType = module.Types.FirstOrDefault(t => t.FullName == typeName);
        if (targetType == null)
        {
            Lg.Error($"ReworkMethod {m.FullName}: tipo destino '{typeName}' no hallado en Assembly-CSharp.");
            return;
        }

        try
        {
            var methodName = targetName ?? m.Name;
            if (targetType.Methods.Any(x => x.Name == methodName))
            {
                Lg.Error($"ReworkMethod {m.FullName}: ya existe '{targetType.FullName}::{methodName}'; se omite.");
                return;
            }

            var modRef = module.ImportReference(m);

            // El método que se añade: misma firma que el del mod, pero sin el 'this'
            // de instancia (si Instance=true) y público.
            var newMethod = new MethodDefinition(
                methodName,
                MethodAttributes.Public | MethodAttributes.HideBySig | (isInstance ? 0 : MethodAttributes.Static),
                module.ImportReference(m.ReturnType));

            newMethod.Parameters.Clear();
            int first = isInstance ? 1 : 0;
            for (int i = first; i < m.Parameters.Count; i++)
                newMethod.Parameters.Add(new ParameterDefinition(
                    m.Parameters[i].Name, 0, module.ImportReference(m.Parameters[i].ParameterType)));

            // Cuerpo forwarder construido SOBRE newMethod (sus parámetros coinciden con
            // los ldarg): carga los argumentos del método nuevo y llama al mod.
            // OJO (lección ERRORES.md): el opcode Ldarg con operand NO es un int/byte
            // (daba "ArgumentException: opcode"); usar el ParameterDefinition real, o la
            // forma corta Ldarg_0..Ldarg_3 para 'this'.
            var body = new MethodBody(newMethod);
            var il = body.GetILProcessor();
            if (isInstance)
            {
                il.Emit(OpCodes.Ldarg_0); // 'this'
                for (int i = 1; i <= newMethod.Parameters.Count; i++)
                    il.Emit(OpCodes.Ldarg, newMethod.Parameters[i - 1]);
            }
            else
            {
                for (int i = 0; i < newMethod.Parameters.Count; i++)
                    il.Emit(OpCodes.Ldarg, newMethod.Parameters[i]);
            }
            il.Emit(OpCodes.Call, modRef);
            il.Emit(OpCodes.Ret);
            newMethod.Body = body;

            targetType.Methods.Add(newMethod);
            asmCSharp.Modified = true;
            Lg.Info($"Método añadido: {(isInstance ? "instancia" : "estático")} {targetType.FullName}::{methodName} → {m.FullName}");
        }
        catch (Exception e)
        {
            Lg.Error($"ReworkMethod {m.FullName}: no se pudo inyectar: {e.Message}");
        }
    }

    // ---------- PROPIEDADES (3.3) ----------

    private static void InjectProperty(
        ModifiableAssembly asmCSharp, ModuleDefinition module, MethodDefinition m, CustomAttribute ca)
    {
        var typeName = ReadStringProp(ca, "Type") ?? ReadCtorString(ca);
        var propName = ReadStringProp(ca, "Name");
        if (string.IsNullOrEmpty(typeName))
        {
            Lg.Error($"ReworkProperty {m.FullName}: falta Type.");
            return;
        }

        var targetType = module.Types.FirstOrDefault(t => t.FullName == typeName);
        if (targetType == null)
        {
            Lg.Error($"ReworkProperty {m.FullName}: tipo destino '{typeName}' no hallado en Assembly-CSharp.");
            return;
        }

        // Emparejar por prefijo Get_/Set_ o por Name explícito.
        var derived = DerivePropName(m.Name, out var isGetter, out var isSetter);
        if (propName == null)
            propName = derived;
        if (string.IsNullOrEmpty(propName) || (!isGetter && !isSetter))
        {
            Lg.Error($"ReworkProperty {m.FullName}: prefijo Get_/Set_ o Name requeridos.");
            return;
        }

        try
        {
            var modRef = module.ImportReference(m);
            var existingProp = targetType.Properties.FirstOrDefault(p => p.Name == propName);

            // Tipo de la propiedad: retorno del getter o último parámetro del setter.
            TypeReference propType;
            if (isSetter && m.Parameters.Count >= 1)
                propType = module.ImportReference(m.Parameters[m.Parameters.Count - 1].ParameterType);
            else
                propType = module.ImportReference(m.ReturnType);

            var prop = existingProp ?? new PropertyDefinition(propName, PropertyAttributes.None, propType);
            if (existingProp == null)
                targetType.Properties.Add(prop);

            // Accessor forwarder. Instancia: el método del mod recibe 'this' como primer
            // parámetro (Get_X(this Pawn) / Set_X(this Pawn, value)).
            var isInstanceMethod = m.Parameters.Count >= 1
                && m.Parameters[0].ParameterType.FullName == targetType.FullName;

            if (isGetter)
            {
                if (prop.GetMethod != null)
                {
                    Lg.Error($"ReworkProperty {m.FullName}: {propName} ya tiene getter; se omite.");
                    return;
                }
                var getter = new MethodDefinition(
                    $"get_{propName}",
                    MethodAttributes.Public | MethodAttributes.HideBySig,
                    propType);
                getter.Body = BuildAccessor(getter, modRef, isInstanceMethod, isSetter: false);
                prop.GetMethod = getter;
                targetType.Methods.Add(getter);
                Lg.Info($"Propiedad {targetType.FullName}::{propName} → getter (forwarder a {m.FullName})");
            }
            else if (isSetter)
            {
                if (prop.SetMethod != null)
                {
                    Lg.Error($"ReworkProperty {m.FullName}: {propName} ya tiene setter; se omite.");
                    return;
                }
                var setter = new MethodDefinition(
                    $"set_{propName}",
                    MethodAttributes.Public | MethodAttributes.HideBySig,
                    module.TypeSystem.Void);
                setter.Parameters.Add(new ParameterDefinition("value", 0, propType));
                setter.Body = BuildAccessor(setter, modRef, isInstanceMethod, isSetter: true);
                prop.SetMethod = setter;
                targetType.Methods.Add(setter);
                Lg.Info($"Propiedad {targetType.FullName}::{propName} → setter (forwarder a {m.FullName})");
            }

            asmCSharp.Modified = true;
        }
        catch (Exception e)
        {
            Lg.Error($"ReworkProperty {m.FullName}: no se pudo inyectar: {e.Message}");
        }
    }

    private static MethodBody BuildAccessor(
        MethodDefinition accessor, MethodReference modRef, bool isInstanceMethod, bool isSetter)
    {
        var body = new MethodBody(accessor);
        var il = body.GetILProcessor();

        // this
        if (isInstanceMethod)
            il.Emit(OpCodes.Ldarg_0);
        // value (setter)
        if (isSetter)
            il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, modRef);
        if (!isSetter && !isInstanceMethod)
        {
            // getter estático: el mod no recibe args, el resultado queda en la pila
        }
        il.Emit(OpCodes.Ret);
        body.InitLocals = false;
        return body;
    }

    // ---------- Helpers ----------

    private static string? DerivePropName(string modMethodName, out bool isGetter, out bool isSetter)
    {
        isGetter = modMethodName.StartsWith("Get_", StringComparison.Ordinal);
        isSetter = modMethodName.StartsWith("Set_", StringComparison.Ordinal);
        if (isGetter) return modMethodName.Substring("Get_".Length);
        if (isSetter) return modMethodName.Substring("Set_".Length);
        return null;
    }

    private static string? ReadStringProp(CustomAttribute ca, string name)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is string s)
                return s;
        return null;
    }

    private static bool ReadBoolProp(CustomAttribute ca, string name, bool def)
    {
        foreach (var p in ca.Properties)
            if (p.Name == name && p.Argument.Value is bool b)
                return b;
        return def;
    }

    private static string? ReadCtorString(CustomAttribute ca)
    {
        if (ca.ConstructorArguments.Count > 0 && ca.ConstructorArguments[0].Value is string s)
            return s;
        return null;
    }
}