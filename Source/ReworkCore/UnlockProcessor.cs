using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Bloque 24.3 — [ReworkUnlock]: conversión de miembros/tipos del juego en API
/// pública REAL (private/internal → public, sealed → heredable, métodos →
/// virtuales). Son cambios de METADATOS que se aplican antes de que ningún tipo
/// cargue: la vtable y el layout se construyen después, con la versión final.
///
/// A diferencia de AccessTools/Harmony (reflexión con delegates cacheados por
/// acceso), aquí el mod compila contra el miembro público: sin reflexión, sin
/// coste por acceso, con verificación en compilación.
///
/// Los atributos pueden declararse sobre métodos, clases o el ensamblado
/// (AllowMultiple), así que un mod puede centralizar su "manifiesto de unlocks".
/// </summary>
internal static class UnlockProcessor
{
    private const string AttrFullName = "Rework.ReworkUnlockAttribute";

    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var entries = new List<Entry>();

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            var module = asm.ModuleDefinition;

            // [ReworkUnlock] a nivel de ensamblado.
            foreach (var ca in module.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                entries.Add(new Entry(asm, null, null, ca));

            foreach (var type in ILSurgeryCommon.AllTypes(module))
            {
                // Nivel de clase.
                foreach (var ca in type.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                    entries.Add(new Entry(asm, type, null, ca));

                // Nivel de método (el patrón de los demás atributos Rework).
                foreach (var m in type.Methods)
                    foreach (var ca in m.CustomAttributes.Where(a => a.AttributeType.FullName == AttrFullName))
                        entries.Add(new Entry(asm, type, m, ca));
            }
        }

        if (entries.Count == 0)
        {
            Lg.Verbose("ReworkUnlock: ninguna entrada declarada.");
            return;
        }

        int applied = 0;
        foreach (var e in entries)
        {
            if (!ReworkDevMode.Filter(ILSurgeryCommon.ModIdentity(e.Asm, e.Type),
                                      ILSurgeryCommon.Where(e.Type, e.Method)))
            {
                Lg.Info($"ReworkUnlock: {ILSurgeryCommon.Where(e.Type, e.Method)} SALTO por ReworkDevMode.");
                continue;
            }
            if (ApplyOne(set, asmCSharp, e)) applied++;
        }

        Lg.Info($"[ReworkUnlock] {applied}/{entries.Count} unlock(s) aplicado(s).");
        if (applied > 0)
            DataStore.AppliedPatches.Add($"[ReworkUnlock] {applied}/{entries.Count} unlock(s) aplicado(s).");
    }

    private static bool ApplyOne(AssemblySet set, ModifiableAssembly asmCSharp, Entry e)
    {
        var typeName = ILSurgeryCommon.ReadStringProp(e.Ca, "Type") ?? ReadCtorArg(e.Ca, 0);
        var member = ILSurgeryCommon.ReadStringProp(e.Ca, "Member") ?? ReadCtorArg(e.Ca, 1);
        var makePublic = ILSurgeryCommon.ReadBoolProp(e.Ca, "Public", true);
        var unseal = ILSurgeryCommon.ReadBoolProp(e.Ca, "Unseal", true);
        var makeVirtual = ILSurgeryCommon.ReadBoolProp(e.Ca, "MakeVirtual", false);
        var where = ILSurgeryCommon.Where(e.Type, e.Method);

        if (string.IsNullOrEmpty(typeName))
        {
            Lg.Error($"ReworkUnlock {where}: falta Type.");
            return false;
        }

        var found = ILSurgeryCommon.FindTypeInRewritable(set, asmCSharp, typeName!);
        if (found == null)
        {
            Lg.Error($"ReworkUnlock {where}: tipo destino '{typeName}' no hallado en los ensamblados reescribibles.");
            return false;
        }
        var (ownerAsm, targetType) = found.Value;

        try
        {
            int changes = 0;

            if (string.IsNullOrEmpty(member))
            {
                // ---------- Nivel de TIPO ----------
                if (makePublic)
                {
                    if (targetType.IsNested)
                        targetType.IsNestedPublic = true;
                    else
                        targetType.IsPublic = true;
                    changes++;
                    Lg.Info($"[ReworkUnlock] {targetType.FullName}: ahora {(targetType.IsNested ? "nested public" : "public")}.");
                }
                if (unseal && targetType.IsSealed && !targetType.IsValueType)
                {
                    targetType.IsSealed = false;
                    changes++;
                    Lg.Info($"[ReworkUnlock] {targetType.FullName}: sealed eliminado (heredable).");
                }
                if (changes == 0)
                    Lg.Info($"[ReworkUnlock] {targetType.FullName}: sin cambios necesarios (ya estaba desbloqueado).");
            }
            else
            {
                // ---------- Nivel de MIEMBRO ----------
                var field = targetType.Fields.FirstOrDefault(f => f.Name == member);
                var method = targetType.Methods.FirstOrDefault(m => m.Name == member);
                var prop = targetType.Properties.FirstOrDefault(p => p.Name == member);
                var nested = targetType.NestedTypes.FirstOrDefault(t => t.Name == member);

                if (field != null)
                {
                    if (makePublic && !field.IsPublic)
                    {
                        field.IsPublic = true;
                        changes++;
                        Lg.Info($"[ReworkUnlock] {targetType.FullName}.{field.Name}: campo ahora public.");
                    }
                }
                else if (method != null)
                {
                    if (makePublic && !method.IsPublic)
                    {
                        method.IsPublic = true;
                        changes++;
                        Lg.Info($"[ReworkUnlock] {targetType.FullName}::{method.Name}: método ahora public.");
                    }
                    if (makeVirtual)
                    {
                        if (method.IsStatic)
                        {
                            Lg.Error($"ReworkUnlock {where}: MakeVirtual no aplica a un método estático " +
                                     $"({targetType.FullName}::{method.Name}).");
                        }
                        else if (method.IsVirtual)
                        {
                            Lg.Info($"[ReworkUnlock] {targetType.FullName}::{method.Name}: ya es virtual; nada que hacer.");
                        }
                        else
                        {
                            method.IsVirtual = true;
                            method.IsNewSlot = true;
                            method.IsHideBySig = true;
                            changes++;
                            Lg.Info($"[ReworkUnlock] {targetType.FullName}::{method.Name}: método ahora virtual (nuevo slot).");
                        }
                    }
                }
                else if (prop != null)
                {
                    foreach (var acc in new[] { prop.GetMethod, prop.SetMethod })
                    {
                        if (acc == null) continue;
                        if (makePublic && !acc.IsPublic)
                        {
                            acc.IsPublic = true;
                            changes++;
                        }
                    }
                    if (changes > 0)
                        Lg.Info($"[ReworkUnlock] {targetType.FullName}.{prop.Name}: propiedad ahora public.");
                    else
                        Lg.Info($"[ReworkUnlock] {targetType.FullName}.{prop.Name}: sin cambios necesarios.");
                }
                else if (nested != null)
                {
                    if (makePublic)
                    {
                        nested.IsNestedPublic = true;
                        changes++;
                        Lg.Info($"[ReworkUnlock] {targetType.FullName}/{nested.Name}: tipo anidado ahora nested public.");
                    }
                }
                else
                {
                    Lg.Error($"ReworkUnlock {where}: no se halló el miembro '{member}' en {targetType.FullName}.");
                    return false;
                }

                if (changes == 0)
                    Lg.Info($"[ReworkUnlock] {targetType.FullName}.{member}: sin cambios necesarios (ya era público).");
            }

            if (changes > 0)
            {
                ownerAsm.Modified = true;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Lg.Error($"ReworkUnlock {where}: falló sobre {typeName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Argumento posicional i del ctor ([ReworkUnlock("Tipo", "Miembro")]).</summary>
    private static string? ReadCtorArg(CustomAttribute ca, int index)
    {
        if (ca.ConstructorArguments.Count > index && ca.ConstructorArguments[index].Value is string s)
            return s;
        return null;
    }

    private readonly struct Entry
    {
        public readonly ModifiableAssembly Asm;
        public readonly TypeDefinition? Type;
        public readonly MethodDefinition? Method;
        public readonly CustomAttribute Ca;

        public Entry(ModifiableAssembly asm, TypeDefinition? type, MethodDefinition? method, CustomAttribute ca)
        {
            Asm = asm; Type = type; Method = method; Ca = ca;
        }
    }
}
