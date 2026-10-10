using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Bloque 24.2 — [ReworkOverride]: inyecta en una clase del juego un método que
/// SOBRESCRIBE de verdad un virtual de su clase base. El override se crea ANTES
/// de que el tipo cargue por primera vez: ocupa el slot de la vtable con despacho
/// virtual normal, sin trampolines y sin coste. (Harmony no puede crear miembros
/// ni reorganizar vtables: solo desviar métodos existentes.)
///
/// El override añadido es un FORWARDER cuya lógica vive en el método estático del
/// mod (mismo patrón probado de MethodInjector). Con CallBase=true el forwarder
/// llama primero al base y pasa su resultado como ÚLTIMO parámetro.
///
/// Este inyector NO padece la limitación de InterfaceInjector (§18): el método
/// vive en Assembly-CSharp y solo REFERENCIA al ensamblado del mod (la vtable no
/// contiene tipos del mod), exactamente como los forwarders de [ReworkMethod]
/// que ya funcionan en producción.
/// </summary>
internal static class OverrideInjector
{
    private const string AttrFullName = "Rework.ReworkOverrideAttribute";

    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var entries = new List<(ModifiableAssembly asm, TypeDefinition type, MethodDefinition m, CustomAttribute ca)>();

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
            foreach (var type in ILSurgeryCommon.AllTypes(asm.ModuleDefinition))
                foreach (var m in type.Methods)
                    foreach (var ca in m.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                        entries.Add((asm, type, m, ca));

        if (entries.Count == 0)
        {
            Lg.Verbose("ReworkOverride: ninguna entrada declarada.");
            return;
        }

        int applied = 0;
        foreach (var (asm, type, m, ca) in entries)
        {
            if (!ReworkDevMode.Filter(ILSurgeryCommon.ModIdentity(asm, type), $"{type.FullName}::{m.Name}"))
            {
                Lg.Info($"ReworkOverride: {type.FullName}::{m.Name} SALTO por ReworkDevMode.");
                continue;
            }
            if (ApplyOne(set, asmCSharp, asm, m, ca)) applied++;
        }

        Lg.Info($"[ReworkOverride] {applied}/{entries.Count} override(s) virtual(es) inyectado(s).");
        if (applied > 0)
            DataStore.AppliedPatches.Add($"[ReworkOverride] {applied}/{entries.Count} override(s) virtual(es) inyectado(s).");
    }

    private static bool ApplyOne(
        AssemblySet set, ModifiableAssembly asmCSharp, ModifiableAssembly modAsm,
        MethodDefinition modMethod, CustomAttribute ca)
    {
        var where = $"{modMethod.DeclaringType!.FullName}::{modMethod.Name}";
        var typeName = ILSurgeryCommon.ReadStringProp(ca, "Type") ?? ReadCtorArg(ca, 0);
        var wantedName = ILSurgeryCommon.ReadStringProp(ca, "Name");
        var callBase = ILSurgeryCommon.ReadBoolProp(ca, "CallBase", true);

        if (string.IsNullOrEmpty(typeName))
        {
            Lg.Error($"ReworkOverride {where}: falta Type.");
            return false;
        }

        // ---------- Validación del tipo destino ----------
        var found = ILSurgeryCommon.FindTypeInRewritable(set, asmCSharp, typeName!);
        if (found == null)
        {
            Lg.Error($"ReworkOverride {where}: tipo destino '{typeName}' no hallado.");
            return false;
        }
        var (ownerAsm, targetType) = found.Value;

        if (targetType.IsValueType || targetType.IsInterface)
        {
            Lg.Error($"ReworkOverride {where}: {targetType.FullName} es struct/interface; no admite overrides.");
            return false;
        }
        if (targetType.HasGenericParameters)
        {
            Lg.Error($"ReworkOverride {where}: {targetType.FullName} es genérico; no soportado en v1.");
            return false;
        }
        if (targetType.IsSealed)
        {
            Lg.Error($"ReworkOverride {where}: {targetType.FullName} está sealed. Desellámalo primero con " +
                     "[ReworkUnlock(Type=..., Unseal=true)] en el mismo mod.");
            return false;
        }

        // Nombre del método virtual: explícito, o el del mod sin prefijo "Rework_".
        var methodName = wantedName ?? modMethod.Name;
        if (methodName.StartsWith("Rework_", StringComparison.Ordinal) && wantedName == null)
            methodName = methodName.Substring("Rework_".Length);

        // ---------- Localizar el virtual en la cadena de bases ----------
        var baseMethod = FindBaseVirtual(targetType, methodName, modMethod, callBase);
        if (baseMethod == null)
        {
            Lg.Error($"ReworkOverride {where}: no se halló un virtual '{methodName}' en la cadena de bases de " +
                     $"{targetType.FullName} cuya firma encaje con el método del mod " +
                     $"({DescribeExpected(modMethod)}).");
            return false;
        }
        if (baseMethod.HasGenericParameters)
        {
            Lg.Error($"ReworkOverride {where}: el método base es genérico; no soportado.");
            return false;
        }
        if (baseMethod.Parameters.Any(p => p.ParameterType.IsByReference))
        {
            Lg.Error($"ReworkOverride {where}: el método base tiene parámetros ref/out; no soportado en v1.");
            return false;
        }

        // ¿El propio tipo ya redeclara ese método con la MISMA firma? Para eso está
        // [ReworkPatch]. (Un simple overload con el mismo nombre sí es válido.)
        bool redeclared = targetType.Methods.Any(x =>
            x.Name == methodName
            && !x.HasGenericParameters
            && x.ReturnType.FullName == baseMethod.ReturnType.FullName
            && x.Parameters.Count == baseMethod.Parameters.Count
            && x.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(baseMethod.Parameters.Select(p => p.ParameterType.FullName)));
        if (redeclared)
        {
            // §47 — Idempotencia: el boot re-procesa los ensamblados ya reescritos
            // (segunda generación); lo que encuentra aquí puede ser el FORWARDER
            // que ESTE inyector creó en la pasada anterior. Se reconoce por su
            // cuerpo (delega en el mismo método del mod) y NO es un error.
            var existing = targetType.Methods.FirstOrDefault(x => x.Name == methodName);
            bool forwarderNuestro = existing is { HasBody: true } && existing.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference fmr
                && string.Equals(fmr.FullName, modMethod.FullName, StringComparison.Ordinal));
            if (forwarderNuestro)
            {
                Lg.Info($"[ReworkOverride] {targetType.FullName}::{methodName}: ya estaba inyectado " +
                        $"(forwarder a {modMethod.FullName}); se omite.");
                return false;
            }

            Lg.Error($"ReworkOverride {where}: {targetType.FullName} ya declara '{methodName}' con esa firma; " +
                     "para reescribir un método EXISTENTE usa [ReworkPatch].");
            return false;
        }

        // ---------- Validación de la firma del mod ----------
        if (!modMethod.IsStatic || !modMethod.IsPublic)
        {
            Lg.Error($"ReworkOverride {where}: el método del mod debe ser 'public static' " +
                     "(lo llama el forwarder desde Assembly-CSharp).");
            return false;
        }
        if (modMethod.HasGenericParameters)
        {
            Lg.Error($"ReworkOverride {where}: el método del mod no puede ser genérico.");
            return false;
        }
        if (modMethod.Parameters.Count == 0)
        {
            Lg.Error($"ReworkOverride {where}: falta el parámetro 'this' (primer parámetro = tipo destino).");
            return false;
        }
        if (modMethod.Parameters.Any(p => p.ParameterType.IsByReference))
        {
            Lg.Error($"ReworkOverride {where}: el método del mod tiene parámetros ref/out; no soportado en v1.");
            return false;
        }

        // Primer parámetro: el tipo destino o una clase base suya (el forwarder pasa 'this').
        var thisParam = modMethod.Parameters[0];
        var thisType = thisParam.ParameterType.Resolve();
        if (thisType == null || !ILSurgeryCommon.IsOrDerivesFrom(targetType, thisType.FullName))
        {
            Lg.Error($"ReworkOverride {where}: el primer parámetro debe ser {targetType.FullName} " +
                     $"(o una de sus bases), no '{thisParam.ParameterType.FullName}'.");
            return false;
        }

        // Parámetros restantes + retorno contra el base.
        int expectedParams = baseMethod.Parameters.Count + (callBase && baseMethod.ReturnType.FullName != "System.Void" ? 1 : 0);
        if (modMethod.Parameters.Count - 1 != expectedParams)
        {
            Lg.Error($"ReworkOverride {where}: se esperaban {expectedParams} parámetros tras 'this' " +
                     $"({baseMethod.Parameters.Count} del base" +
                     (callBase && baseMethod.ReturnType.FullName != "System.Void" ? " + resultado del base" : "") +
                     $"), el mod declara {modMethod.Parameters.Count - 1}.");
            return false;
        }
        for (int i = 0; i < baseMethod.Parameters.Count; i++)
        {
            var want = baseMethod.Parameters[i].ParameterType.FullName;
            var got = modMethod.Parameters[i + 1].ParameterType.FullName;
            if (want != got)
            {
                Lg.Error($"ReworkOverride {where}: parámetro {i + 1} debería ser '{want}' y es '{got}'.");
                return false;
            }
        }
        if (callBase && baseMethod.ReturnType.FullName != "System.Void")
        {
            var wantRet = baseMethod.ReturnType.FullName;
            var gotRet = modMethod.Parameters[^1].ParameterType.FullName;
            if (wantRet != gotRet)
            {
                Lg.Error($"ReworkOverride {where}: con CallBase=true el ÚLTIMO parámetro debe ser el " +
                         $"resultado del base ('{wantRet}'), y es '{gotRet}'.");
                return false;
            }
        }
        if (modMethod.ReturnType.FullName != baseMethod.ReturnType.FullName)
        {
            Lg.Error($"ReworkOverride {where}: el retorno debe ser '{baseMethod.ReturnType.FullName}', " +
                     $"el mod devuelve '{modMethod.ReturnType.FullName}'.");
            return false;
        }

        // ---------- Construir el override (forwarder) ----------
        try
        {
            var module = ownerAsm.ModuleDefinition;
            var modRef = module.ImportReference(modMethod);
            var baseRef = module.ImportReference(baseMethod);

            var access = baseMethod.Attributes & MethodAttributes.MemberAccessMask;
            var override_ = new MethodDefinition(
                methodName,
                access | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                module.ImportReference(baseMethod.ReturnType))
            {
                HasThis = true
            };
            foreach (var p in baseMethod.Parameters)
                override_.Parameters.Add(new ParameterDefinition(
                    p.Name, p.Attributes & ~ParameterAttributes.Optional, module.ImportReference(p.ParameterType)));

            var body = new MethodBody(override_) { InitLocals = true };
            var il = body.GetILProcessor();

            void LdThisAndArgs()
            {
                il.Emit(OpCodes.Ldarg_0);
                for (int i = 0; i < override_.Parameters.Count; i++)
                    il.Emit(OpCodes.Ldarg, override_.Parameters[i]);
            }

            if (callBase)
            {
                // base.M(...)  → el resultado (si no es void) se guarda en local 0.
                LdThisAndArgs();
                il.Emit(OpCodes.Call, baseRef);

                VariableDefinition? baseResult = null;
                if (baseMethod.ReturnType.FullName != "System.Void")
                {
                    baseResult = new VariableDefinition(module.ImportReference(baseMethod.ReturnType));
                    body.Variables.Add(baseResult);
                    il.Emit(OpCodes.Stloc, baseResult);
                }

                // Mod.M(this, args..., [baseResult])
                LdThisAndArgs();
                if (baseResult != null)
                    il.Emit(OpCodes.Ldloc, baseResult);
                il.Emit(OpCodes.Call, modRef);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                LdThisAndArgs();
                il.Emit(OpCodes.Call, modRef);
                il.Emit(OpCodes.Ret);
            }

            override_.Body = body;
            targetType.Methods.Add(override_);
            ownerAsm.Modified = true;

            var baseChain = $"{baseMethod.DeclaringType!.FullName}::{baseMethod.Name}";
            Lg.Info($"[ReworkOverride] {targetType.FullName}::{methodName} ahora SOBREESCRIBE a " +
                    $"{baseChain} (CallBase={callBase}) → {modMethod.FullName}");
            DataStore.AppliedPatches.Add($"[ReworkOverride] {targetType.FullName}::{methodName} → {modMethod.FullName} " +
                                         $"(base {baseChain}, CallBase={callBase})");
            return true;
        }
        catch (Exception ex)
        {
            Lg.Error($"ReworkOverride {where}: no se pudo inyectar: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Busca en la cadena de clases base de targetType el VIRTUAL con ese nombre cuya
    /// firma encaje con el método del mod. Devuelve el MethodDefinition del que más
    /// cerca está en la cadena (la primera coincidencia subiendo).
    /// </summary>
    private static MethodDefinition? FindBaseVirtual(
        TypeDefinition targetType, string methodName, MethodDefinition modMethod, bool callBase)
    {
        var t = targetType.BaseType;
        int guard = 0;
        while (t != null && guard++ < 64)
        {
            TypeDefinition? td;
            try { td = t.Resolve(); }
            catch { return null; }
            if (td == null) return null;

            foreach (var m in td.Methods)
            {
                if (m.Name != methodName || !m.IsVirtual || m.HasGenericParameters) continue;
                if (SignatureMatches(m, modMethod, callBase)) return m;
            }
            t = td.BaseType;
        }
        return null;
    }

    private static bool SignatureMatches(MethodDefinition baseMethod, MethodDefinition modMethod, bool callBase)
    {
        int modParams = modMethod.Parameters.Count - 1; // sin 'this'
        bool baseRet = baseMethod.ReturnType.FullName != "System.Void";
        int expected = baseMethod.Parameters.Count + (callBase && baseRet ? 1 : 0);
        if (modParams != expected) return false;

        for (int i = 0; i < baseMethod.Parameters.Count; i++)
            if (baseMethod.Parameters[i].ParameterType.FullName != modMethod.Parameters[i + 1].ParameterType.FullName)
                return false;
        if (callBase && baseRet)
        {
            if (modMethod.Parameters[^1].ParameterType.FullName != baseMethod.ReturnType.FullName) return false;
        }
        return modMethod.ReturnType.FullName == baseMethod.ReturnType.FullName;
    }

    private static string DescribeExpected(MethodDefinition modMethod)
    {
        var ps = string.Join(", ", modMethod.Parameters.Select(p => p.ParameterType.FullName));
        return $"({ps}) → {modMethod.ReturnType.FullName}";
    }

    private static string? ReadCtorArg(CustomAttribute ca, int index)
    {
        if (ca.ConstructorArguments.Count > index && ca.ConstructorArguments[index].Value is string s)
            return s;
        return null;
    }
}
