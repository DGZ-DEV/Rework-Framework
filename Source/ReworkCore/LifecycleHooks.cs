using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Rework;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Ciclo de vida declarativo ([ReworkHook]): Rework descubre los métodos estáticos
/// marcados con [ReworkHook(ReworkHookPoint.X)] en los mods integrados (Caso A) y
/// inyecta su llamada al INICIO del método del juego que representa el evento
/// (antes de cualquier región try/catch, mismo patrón que los inicializadores).
///
/// Contrato del hook: estático, público, void; 0 parámetros (sin contexto) o
/// 1 parámetro (el 'this' del evento, p.ej. Verse.Pawn en PawnDied). Si no cumple,
/// se reporta y se omite (no rompe el boot).
/// </summary>
internal static class LifecycleHooks
{
    /// <summary>Punto → (tipo, método, índice del contexto). ctxArg = -1 → 'this'
    /// (ldarg.0); si no, es el índice del parámetro del método destino que se pasa
    /// al hook (p.ej. set_CurrentMap tiene el mapa nuevo en el parámetro 1).</summary>
    private static readonly Dictionary<string, (string type, string method, int ctxArg)> Map =
        new()
        {
            ["GameStart"] = ("Verse.Game", "LoadGame", -1),
            ["GameInitialized"] = ("Verse.Game", "FinalizeInit", -1),
            ["MapGenerated"] = ("Verse.Map", "FinalizeLoading", -1),
            ["MapInitialized"] = ("Verse.Map", "FinalizeInit", -1),
            ["PawnDied"] = ("Verse.Pawn", "Kill", -1),
            ["PawnSpawned"] = ("Verse.Pawn", "SpawnSetup", -1),
            ["IncidentFired"] = ("RimWorld.IncidentWorker", "TryExecute", -1),
            ["MapTick"] = ("Verse.Map", "MapPreTick", -1),
            ["PawnTick"] = ("Verse.Pawn", "Tick", -1),
            ["GameComponentTick"] = ("Verse.GameComponent", "GameComponentTick", -1),
            ["MapComponentTick"] = ("Verse.MapComponent", "MapComponentTick", -1),
            ["WorldComponentTick"] = ("RimWorld.Planet.WorldComponent", "WorldComponentTick", -1),
            ["CurrentMapChanged"] = ("Verse.Game", "set_CurrentMap", 1),   // el mapa NUEVO
            ["CaravanSpawned"] = ("RimWorld.Planet.Caravan", ".ctor", -1), // this (en construcción)
            ["NewGameStarted"] = ("Verse.Game", "InitNewGame", -1),
            ["PawnCreated"] = ("Verse.Pawn", "PostMake", -1),
            ["StorytellerTick"] = ("RimWorld.Storyteller", "StorytellerTick", -1),
            ["GameEnded"] = ("Verse.Game", "Dispose", -1),
            ["GameSaving"] = ("Verse.GameDataSaveLoader", "SaveGame", -1),
            ["BabyBorn"] = ("RimWorld.PregnancyUtility", "ApplyBirthOutcome", 5),  // la madre (param 5)
            ["CaravanEnteredMap"] = ("RimWorld.Planet.CaravanEnterMapUtility", "Enter", 2), // el Map (param 2)
            ["PawnLeftToCaravan"] = ("RimWorld.Planet.CaravanExitMapUtility", "ExitMapAndJoinOrCreateCaravan", 1), // el Pawn (param 1)
        };

    internal static void Process(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            foreach (var hook in FindHooks(asm.ModuleDefinition.Types))
            {
                var attr = hook.CustomAttributes.FirstOrDefault(
                    a => a.AttributeType.FullName == "Rework.ReworkHookAttribute");
                if (attr == null) continue;

                string point = null;
                foreach (var n in attr.Properties)
                    if (n.Name == "Point" && n.Argument.Value != null)
                        point = EnumNameOf(n.Argument.Value);
                if (point == null && attr.ConstructorArguments.Count > 0)
                    point = EnumNameOf(attr.ConstructorArguments[0].Value);
                if (point == null || !Map.TryGetValue(point, out var target))
                {
                    Lg.Error($"Hook {hook.FullName}: punto '{point}' no soportado; se omite.");
                    continue;
                }

                // Validar contrato
                if (!hook.IsStatic || !hook.IsPublic || hook.ReturnType.MetadataType != MetadataType.Void)
                {
                    Lg.Error($"Hook {hook.FullName}: debe ser estático, público y void; se omite.");
                    continue;
                }
                if (hook.Parameters.Count > 1)
                {
                    Lg.Error($"Hook {hook.FullName}: soporta 0 o 1 parámetro (el contexto), no {hook.Parameters.Count}; se omite.");
                    continue;
                }

                // Método destino en Assembly-CSharp
                var targetType = module.Types.FirstOrDefault(t => t.FullName == target.type);
                var targetMethod = targetType?.Methods.FirstOrDefault(m =>
                {
                    if (target.method == ".ctor")
                        return m.IsConstructor && !m.IsStatic && m.HasBody;
                    return m.Name == target.method && m.HasBody;
                });
                if (targetMethod == null)
                {
                    Lg.Error($"Hook {hook.FullName}: no se halló {target.type}.{target.method} para enganchar; se omite.");
                    continue;
                }

                // (Opcional) validar el tipo del parámetro de contexto
                if (hook.Parameters.Count == 1)
                {
                    var wantCtx = hook.Parameters[0].ParameterType.FullName;
                    // ctxArg: -1 → 'this' (declaringType); N → parámetro IL (Parameters[N-1]).
                    var haveCtx = target.ctxArg == -1
                        ? targetMethod.DeclaringType.FullName
                        : targetMethod.Parameters[target.ctxArg - 1].ParameterType.FullName;
                    if (wantCtx != haveCtx)
                    {
                        Lg.Error($"Hook {hook.FullName}: el parámetro debe ser '{haveCtx}' (el contexto del punto), no '{wantCtx}'; se omite.");
                        continue;
                    }
                }

                try
                {
                    // 17.8/17.9 — Modo desarrollador: ReworkDevMode.OnlyPatch / OnlyMod filtran
                    // los hooks que se aplican (para aislar un fallo o revertir uno sin quitarlo
                    // del mod). El framework registra cuál se salta.
                    if (!ReworkDevMode.Filter(hook.DeclaringType.Namespace ?? "?", hook.FullName))
                    {
                        Lg.Info($"Hook SALTO por ReworkDevMode (OnlyPatch/OnlyMod): {hook.FullName}");
                        continue;
                    }

                    Inject(hook, targetMethod, module, target.ctxArg);
                    Lg.Info($"Hook aplicado: {hook.DeclaringType.FullName}::{hook.Name} → {target.type}.{target.method}");
                    asmCSharp.Modified = true;
                }
                catch (Exception e)
                {
                    Lg.Error($"Hook {hook.FullName}: no se pudo inyectar: {e.Message}");
                }
            }
        }
    }

    /// <summary>Extrae el nombre del valor de atributo del enum (CustomAttributeArgument
    /// anidado o int si ya viene desempaquetado).</summary>
    private static string EnumNameOf(object v)
    {
        while (v is Mono.Cecil.CustomAttributeArgument caa)
            v = caa.Value;
        // El enum llega como int (su valor subyacente); lo convertimos al nombre.
        try
        {
            var names = System.Enum.GetNames(typeof(ReworkHookPoint));
            var vals = System.Enum.GetValues(typeof(ReworkHookPoint));
            var i = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
            if (i >= 0 && i < names.Length)
                return names[i];
            return v?.ToString();
        }
        catch
        {
            return v?.ToString();
        }
    }

    private static IEnumerable<MethodDefinition> FindHooks(IEnumerable<TypeDefinition> inTypes)
    {
        return
            from t in inTypes
            from m in t.Methods
            where m.CustomAttributes.Any(a => a.AttributeType.FullName == "Rework.ReworkHookAttribute")
            select m;
    }

    private static void Inject(MethodDefinition hook, MethodDefinition targetMethod, ModuleDefinition module, int ctxArg)
    {
        var hookRef = module.ImportReference(hook);

        // Idempotencia: si ya se inyectó (call del hook como primera instrucción real),
        // no duplicar.
        if (AlreadyInjected(targetMethod.Body.Instructions, hookRef))
            return;

        var first = targetMethod.Body.Instructions[0];
        var il = targetMethod.Body.GetILProcessor();

        // Si el hook toma contexto (1 parámetro), cargar el argumento correcto ANTES del
        // call: ctxArg == -1 → 'this' (ldarg.0); si no, ldarg.[parámetro].
        // OJO (lección, ERRORES.md §14): el opcode Ldarg espera un ParameterDefinition,
        // NO un int/byte (eso daba "ArgumentException: opcode" al validar el IL).
        if (hook.Parameters.Count == 1)
        {
            Instruction ctxLoad;
            if (ctxArg == -1)
            {
                ctxLoad = Instruction.Create(OpCodes.Ldarg_0);
            }
            else
            {
                var paramDef = targetMethod.Parameters[ctxArg - 1];
                ctxLoad = Instruction.Create(OpCodes.Ldarg, paramDef);
            }
            il.InsertBefore(first, ctxLoad);
        }
        il.InsertBefore(first, il.Create(OpCodes.Call, hookRef));
    }

    private static bool AlreadyInjected(Mono.Collections.Generic.Collection<Instruction> instrs, MethodReference hookRef)
    {
        // Tras la inyección, la cabecera es [ldarg.?]? call hook. Buscamos el call del
        // hook entre las primeras instrucciones (cubriendo ldarg.0 o ldarg.N de contexto).
        if (instrs.Count == 0) return false;
        var probe = instrs[0];
        if (probe.OpCode == OpCodes.Ldarg_0 || probe.OpCode == OpCodes.Ldarg
            || probe.OpCode == OpCodes.Ldarg_S)
        {
            if (instrs.Count < 2 || instrs[1].OpCode != OpCodes.Call) return false;
            probe = instrs[1];
        }
        return probe.OpCode == OpCodes.Call
            && probe.Operand is MethodReference pmr
            && pmr.FullName == hookRef.FullName;
    }
}