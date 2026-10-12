using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Registrador de grupos de variantes ([ReworkToggleDef]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// valida cada grupo (2+ variantes existentes) y lo guarda con índice
/// inverso por def. Además registra el gizmo genérico del framework (una vez):
/// cualquier variante seleccionada (o arma del colono) muestra "Cambiar
/// variante" con el menú de opciones.
///
/// La lista es explícita (no nomenclatura `_TOGGLEDEF_`): renombrar un def sin
/// actualizar la lista da error claro aquí, no un grupo roto en silencio.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkToggleDefRegistrar
{
    private static bool gizmoRegistered;

    static ReworkToggleDefRegistrar()
    {
        RegisterAll();
        RegisterGizmo();
    }

    public static void RegisterAll()
    {
        int count = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass) continue;
                    foreach (var attr in type.GetCustomAttributes<ReworkToggleDefAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkToggleDefRegistrar: {count} grupo(s) [ReworkToggleDef] registrado(s).");
    }

    private static bool Register(ReworkToggleDefAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.GroupId))
            {
                Lg.Error($"[ReworkToggleDef] '{carrierType.FullName}': GroupId vacío; se ignora.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(attr.Variants))
            {
                Lg.Error($"[ReworkToggleDef] '{attr.GroupId}': Variants vacío (mínimo 2); se ignora.");
                return false;
            }

            var names = new List<string>();
            foreach (var v in attr.Variants.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = v.Trim();
                if (t.Length > 0) names.Add(t);
            }
            if (names.Count < 2)
            {
                Lg.Error($"[ReworkToggleDef] '{attr.GroupId}': necesita 2+ variantes, hay {names.Count}; se ignora.");
                return false;
            }

            var defs = new List<object>();
            foreach (var name in names)
            {
                var td = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (td == null)
                {
                    Lg.Error($"[ReworkToggleDef] '{attr.GroupId}': variante '{name}' no existe; el grupo NO se registra hasta corregirlo.");
                    return false;
                }
                defs.Add(td);
            }

            ReworkToggleDefRegistry.Register(new ReworkToggleDefRegistry.ToggleGroup
            {
                GroupId = attr.GroupId,
                VariantDefNames = names.ToArray(),
                VariantDefs = defs.ToArray(),
                GizmoLabel = string.IsNullOrWhiteSpace(attr.GizmoLabel) ? "Cambiar variante" : attr.GizmoLabel.Trim(),
            });
            Lg.Info($"[ReworkToggleDef] Grupo '{attr.GroupId}' registrado ({names.Count} variantes).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkToggleDef] Error registrando '{attr.GroupId}': {e.Message}");
            return false;
        }
    }

    private static void RegisterGizmo()
    {
        if (gizmoRegistered) return;
        gizmoRegistered = true;
        ReworkToggleDefGizmo.EnsureRegistered();
    }
}

/// <summary>
/// Gizmo genérico del framework para variantes: aparece en cualquier Thing
/// variante (o en colonos con arma variante) y abre el menú de cambio.
/// Se registra una vez para Thing y Pawn, con condición: sin variantes, el
/// botón ni se crea (VisibleIf). Los mods solo declaran grupos.
/// </summary>
public static class ReworkToggleDefGizmo
{
    private static bool registered;

    public static void EnsureRegistered()
    {
        if (registered) return;
        registered = true;
        var method = typeof(ReworkToggleDefGizmo).GetMethod(nameof(OnGizmo),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        System.Func<object, bool> visible = target =>
        {
            if (!ReworkToggleDefRuntime.HasActive) return false;
            var thing = ReworkToggleDefRuntime.ResolveThing(target);
            if (thing?.def == null) return false;
            var group = ReworkToggleDefRegistry.GetByDef(thing.def.defName);
            return group != null && group.VariantDefs.Length >= 2;
        };
        ReworkGizmoRegistry.RegisterConditional("Thing", "Cambiar variante", "Cambia a otra variante del grupo.", method, visible);
        ReworkGizmoRegistry.RegisterConditional("Pawn", "Cambiar variante", "Cambia el arma a otra variante del grupo.", method, visible);
    }

    public static void OnGizmo(object target)
    {
        try
        {
            if (!ReworkToggleDefRuntime.HasActive) return;
            var thing = ReworkToggleDefRuntime.ResolveThing(target);
            if (thing?.def == null) return;
            var group = ReworkToggleDefRegistry.GetByDef(thing.def.defName);
            if (group == null || group.VariantDefs.Length < 2) return;

            var options = new List<FloatMenuOption>();
            foreach (var v in group.VariantDefs)
            {
                if (!(v is ThingDef td)) continue;
                if (td == thing.def) continue;
                var capturedThing = thing;
                var capturedDef = td;
                options.Add(new FloatMenuOption(
                    td.label ?? td.defName,
                    () => ReworkToggleDefRuntime.SwapTo(capturedThing, capturedDef)));
            }
            if (options.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(options));
        }
        catch (Exception e)
        {
            Rework.Core.Lg.Error($"[ReworkToggleDef] Gizmo falló: {e.Message}");
        }
    }
}
