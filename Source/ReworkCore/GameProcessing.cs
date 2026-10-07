using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Rework.Core;

/// <summary>
/// Pipeline de procesado de ensamblados.
/// (Equivalente a Prepatcher Source/Implementation/Process/GameProcessing.cs.)
///
/// MVP: solo adición de campos [ReworkField]. En Fase 3 se añaden aquí:
/// execution order fix (ExecutionOrderFixer), free patches ([ReworkPatch]),
/// parcheo de corrutinas y orden de prioridades configurables.
/// </summary>
internal static class GameProcessing
{
    internal static void Process(AssemblySet set)
    {
        var asmCSharp = set.FindAssembly(AssemblyCollector.AssemblyCSharp);
        if (asmCSharp == null)
            throw new InvalidOperationException("Assembly-CSharp no está en el set");

        // Assembly-CSharp siempre se recarga (los parches la modifican).
        // (Prepatcher GameProcessing.cs: "Other code assumes that these always get reloaded".)
        asmCSharp.SetNeedsReload();

        // 1) Adición de campos [ReworkField]
        new FieldAdder(set).ProcessAllAssemblies();

        // 1a) Métodos/propiedades añadidos [ReworkMethod] / [ReworkProperty] (bloque 3):
        //     forwarders en el tipo destino que delegan en el método del mod.
        MethodInjector.Process(set, asmCSharp);

        // 1a'') Atributos [ReworkAnnotate] (bloque 4.10/4.11/4.12): metadata en clases/
        //     métodos/campos existentes. (Las interfaces [ReworkInterface] quedaron
        //     DESCARTADAS: añadir una interfaz de un mod a una clase del juego rompe la
        //     vtable en runtime — TypeLoadException "VTable setup ... failed" — por el
        //     modelo de recarga con identidad duplicada. Ver ERRORES.md §18.)
        AnnotateInjector.Process(set, asmCSharp);

        // 1b) Ciclo de vida declarativo [ReworkHook] (Fase 4): engancha métodos del
        //     mod al inicio de los métodos del juego que representan eventos.
        LifecycleHooks.Process(set, asmCSharp);

        // 1c) Parches libres [ReworkPatch] (Fase 3): métodos estáticos marcados que
        //     reciben el ModuleDefinition de Assembly-CSharp y lo modifican con Cecil.
        FreePatcher.Process(set);

        // 1c') Paneles de UI declarativos [ReworkUIPanel]: inyecta la llamada al
        //      método del mod dentro de DoWindowContents(Rect) de la ventana destino
        //      (UI Inyectada — sin XML ni Window propia). Orden prio desc → carga → nombre.
        UiPanelInjector.Process(set, asmCSharp);

        // 2) Parche de carga de mods: redirigir ModAssemblyHandler.ReloadAll a
        //    ReworkLoader.LoadFile. Sin esto, la pasada 2 (re-arranque del boot)
        //    cargaría por Assembly.LoadFrom el ORIGINAL refonly de Rework.dll →
        //    mod inerte. (Equivalente a AssemblyLoadingFreePatch del fork jikulopo.)
        PatchModAssemblyHandlerReloadAll(set, asmCSharp);

        // 3) Parche de GenTypes.AllActiveAssemblies: el juego enumera tipos SOLO por
        //    ahí (AllTypes, GetTypeInAnyAssembly, AllSubclasses, ...). El filtro evita
        //    que en la pasada 2 se instancien tipos de los originales refonly
        //    ("It is illegal to invoke a method on a type loaded using the
        //    ReflectionOnly api"). Cinturón de seguridad de la mejora anterior.
        PatchGenTypesAllActiveAssemblies(asmCSharp);

        // 4) Parche de MusicManagerEntry.StartPlaying: la música de la pantalla de
        //    entrada solo debe arrancar con los defs cargados. Tras el swap del boot
        //    (pasada 1) puede haber un instante con la cola de eventos vacía en el que
        //    Root_Entry.Update no haga early-return y llame a StartPlaying ANTES de que
        //    carguen los defs → SongDefOf.EntrySong es null → NRE + un
        //    "MusicAudioSourceDummy" huérfano → bucle de NullReference por frame.
        //    Igual que en un boot normal, la música solo debe sonar con
        //    PlayDataLoader.Loaded == true.
        PatchMusicManagerEntryStartPlaying(asmCSharp);

        // 5) Parche de Verse.Root.Start: inyectar SceneRootHook.EnsureRegistered() al
        //    inicio, para que cualquier Root NUEVO registre el hook de transiciones de
        //    escena. Al cargarse una escena (cargar partida, nuevo mundo, quicktest...)
        //    Unity recrea el Root desde el ensamblado ORIGINAL (refonly); el hook lo
        //    sustituye por el tipo NUEVO → boot limpio. Sin esto, la transición a
        //    partida real resucita tipos refonly (cascada).
        PatchRootStartRegisterSceneHook(asmCSharp);

        // 6) Parche de guardia en WorldCameraDriver.ApplyPositionToGameObject: si
        //    Current.Game aún es null (al despertar la cámara de mundo durante la
        //    inicialización) retorna en vez de reventar. Así WorldCameraManager
        //    (static [StaticConstructorOnStartup]) termina su ctor y asigna
        //    worldCameraDriverInt → Find.WorldCameraDriver deja de ser null → se
        //    eliminan los NREs en cadena de la vista de mundo (ExpandableWorldObjects,
        //    WorldInterface).
        PatchWorldCameraDriverApplyPositionGuard(asmCSharp);

        // 7) SOLUCIÓN DEFINITIVA (estilo Prepatcher, WorldCameraFreePatch): el ctor de
        //    WorldCameraManager llama AddComponent<WorldCameraDriver>, pero Unity resuelve
        //    ese tipo POR NOMBRE y lo materializa desde el Assembly-CSharp ORIGINAL
        //    (refonly) → colisión en la vista de mundo. Redirigimos esa llamada a
        //    WorldCameraDriverPatch.AttachWorldCameraDriver (en ReworkCore) para que añada
        //    la subclase única ReworkWorldCameraDriver (nombre que NO colisiona y que
        //    hereda del driver NUEVO). Así el ctor hace GetComponent<WorldCameraDriver>()
        //    y lo encuentra → worldCameraDriverInt bien → Find.WorldCameraDriver deja de
        //    ser null → la vista de mundo/planeta carga sin NREs.
        PatchWorldCameraManagerCreateWorldCameraDriver(asmCSharp);

        // 8) Telegram (silenciar el ruido del reinicio, estilo Prepatcher SilenceLogging):
        //    prefijo en Verse.Log.Error(string) que consulta RuntimeHooks.ShouldSuppressLog y
        //    retorna sin loguear si estamos en la ventana del reinicio y el mensaje es ruido
        //    conocido (ThreadAbortException, NREs del frame de transición). Fuera de la
        //    ventana nunca suprime.
        PatchLogErrorSuppression(asmCSharp);

        // 9) ReworkBinaryScribe: Inyectar OnSafeSaveCompleted al final de Verse.SafeSaver.Save
        PatchSafeSaverBinarySave(asmCSharp);

        // 10) ReworkBinaryScribe: Inyectar TryLoadBinaryDocument al inicio de Verse.ScribeLoader.InitLoading
        PatchScribeLoaderBinaryLoad(asmCSharp);
    }

    /// <summary>
    /// Inyecta en Verse.Log.Error(string) el prefijo:
    /// <c>if (RuntimeHooks.ShouldSuppressLog(msg)) return;</c>
    /// para silenciar únicamente el ruido del reinicio interno durante su ventana
    /// (DataStore.suppressLogs == true). El prefijo es: ldarg.0; call
    /// ShouldSuppressLog(string); brfalse.s primera-instr; ret.
    /// </summary>
    private static void PatchLogErrorSuppression(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var logError = module.Types
            .FirstOrDefault(t => t.FullName == "Verse.Log")
            ?.Methods.FirstOrDefault(m => m.Name == "Error" && m.Parameters.Count == 1);
        if (logError == null || !logError.HasBody)
        {
            Lg.Error("No se encontró Verse.Log.Error(string) para el silencio de logs.");
            return;
        }

        var suppressRef = module.ImportReference(
            typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.ShouldSuppressLog), new[] { typeof(string) }));
        if (suppressRef == null)
            return;

        var first = logError.Body.Instructions[0];
        // Evita re-parchear en montaje repetido en memoria.
        if (first.OpCode == OpCodes.Ldarg_0 && first.Next?.OpCode == OpCodes.Call
            && first.Next.Operand is MethodReference smr
            && smr.FullName == suppressRef.FullName)
            return;

        var il = logError.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, suppressRef));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        Lg.Info("Parche aplicado: Verse.Log.Error → ShouldSuppressLog (silencio del ruido de reinicio)");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Reescribe la llamada <c>Assembly.LoadFrom(string)</c> dentro de
    /// Verse.ModAssemblyHandler.ReloadAll para que pase por ReworkLoader.LoadFile.
    /// </summary>
    private static void PatchModAssemblyHandlerReloadAll(AssemblySet set, ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;
        var reloadAll = module.Types
            .FirstOrDefault(t => t.FullName == "Verse.ModAssemblyHandler")
            ?.Methods.FirstOrDefault(m => m.Name == "ReloadAll");

        if (reloadAll == null)
        {
            Lg.Error("No se encontró Verse.ModAssemblyHandler.ReloadAll para parchear.");
            return;
        }

        var loadFileRef = module.ImportReference(typeof(ReworkLoader).GetMethod(nameof(ReworkLoader.LoadFile)));

        var changed = false;
        foreach (var instr in reloadAll.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Call)
                continue;
            if (instr.Operand is not MethodReference mr)
                continue;
            if (mr.Name != "LoadFrom")
                continue;
            if (mr.DeclaringType?.Namespace != "System.Reflection" || mr.DeclaringType?.Name != "Assembly")
                continue;

            instr.Operand = loadFileRef;
            changed = true;
            break;
        }

        if (changed)
        {
            Lg.Info("Parche aplicado: ModAssemblyHandler.ReloadAll → ReworkLoader.LoadFile");
            asmCSharp.Modified = true;
        }
        else
        {
            Lg.Error("No se encontró la llamada Assembly.LoadFrom dentro de ModAssemblyHandler.ReloadAll.");
        }
    }

    /// <summary>
    /// Inyecta RuntimeHooks.WrapActiveAssemblies en el getter de
    /// GenTypes.AllActiveAssemblies: el getter de un iterador solo construye el
    /// objeto estado (newobj &lt;get_AllActiveAssemblies&gt;d__XX; ret); se inserta la
    /// llamada al hook justo antes del ret para filtrar los originales refonly.
    /// </summary>
    private static void PatchGenTypesAllActiveAssemblies(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;
        var genTypes = module.Types.FirstOrDefault(t => t.FullName == "Verse.GenTypes");
        var getter = genTypes?.Properties.FirstOrDefault(p => p.Name == "AllActiveAssemblies")?.GetMethod;

        if (getter == null)
        {
            Lg.Error("No se encontró Verse.GenTypes.AllActiveAssemblies para parchear.");
            return;
        }

        var instructions = getter.Body.Instructions;
        var ret = instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret);
        if (ret == null)
        {
            Lg.Error("GenTypes.AllActiveAssemblies no termina en ret; no se parcheó.");
            return;
        }

        // Al llegar al ret, la pila tiene el IEnumerable del iterador: lo envolvemos.
        var wrapRef = module.ImportReference(typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.WrapActiveAssemblies)));
        var il = getter.Body.GetILProcessor();
        il.InsertBefore(ret, il.Create(OpCodes.Call, wrapRef));

        Lg.Info("Parche aplicado: GenTypes.AllActiveAssemblies → RuntimeHooks.WrapActiveAssemblies");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Inyecta en RimWorld.MusicManagerEntry.StartPlaying():
    ///   1) Prefijo: <c>if (!PlayDataLoader.Loaded) ret;</c>
    ///      La música de entrada no debe arrancar hasta que los defs estén cargados
    ///      (evita el NRE por SongDefOf null en un hueco transitorio de la cola tras
    ///      el swap del boot y evita crear un MusicAudioSourceDummy huérfano).
    ///   2) Reapunta el <c>ret</c> de la rama "ya existe un music source" a la rama
    ///      que crea uno nuevo. La rama de error hace Log.Error + ret y deja
    ///      audioSource null → bucle de NullReference por frame (MusicManagerEntryUpdate
    ///      accede a audioSource.volume). Con el salto, si queda un dummy huérfano
    ///      (p.ej. creado por el Root_Entry original en el frame del swap), se crea un
    ///      source nuevo válido y la música suena; sin NRE ni log de error.
    /// </summary>
    private static void PatchMusicManagerEntryStartPlaying(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var startPlaying = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.MusicManagerEntry")
            ?.Methods.FirstOrDefault(m => m.Name == "StartPlaying");
        if (startPlaying == null)
        {
            Lg.Error("No se encontró RimWorld.MusicManagerEntry.StartPlaying para parchear.");
            return;
        }

        var madeChanges = false;
        var il = startPlaying.Body.GetILProcessor();

        // --- 1) Prefijo: if (!PlayDataLoader.Loaded) ret; ---
        var loadedGetter = module.Types
            .FirstOrDefault(t => t.FullName == "Verse.PlayDataLoader")
            ?.Properties.FirstOrDefault(p => p.Name == "Loaded")
            ?.GetMethod;
        if (loadedGetter == null)
        {
            Lg.Error("No se encontró Verse.PlayDataLoader.Loaded para parchear StartPlaying.");
        }
        else
        {
            var first = startPlaying.Body.Instructions[0];
            var alreadyGuarded = first.OpCode == OpCodes.Call
                && first.Operand is MethodReference gmr
                && gmr.FullName == loadedGetter.FullName;
            if (!alreadyGuarded)
            {
                il.InsertBefore(first, il.Create(OpCodes.Call, loadedGetter));
                il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
                il.InsertBefore(first, il.Create(OpCodes.Ret));
                madeChanges = true;
            }
        }

        // --- 2) Reapuntar el ret de la rama "ya existe un music source" ---
        var instrs = startPlaying.Body.Instructions;
        for (var i = 0; i < instrs.Count; i++)
        {
            var c = instrs[i];
            if (c.OpCode != OpCodes.Call || c.Operand is not MethodReference logError)
                continue;
            if (logError.DeclaringType?.Name != "Log" || logError.Name != "Error")
                continue;
            if (i + 1 >= instrs.Count || instrs[i + 1].OpCode != OpCodes.Ret)
                continue;

            // Ya no es un error: se recupera el dummy. Degradamos el Log.Error a
            // Log.Message para que no aparezca una línea roja en el log.
            var logMessage = module.Types
                .FirstOrDefault(t => t.FullName == "Verse.Log")
                ?.Methods.FirstOrDefault(m => m.Name == "Message"
                    && m.Parameters.Count == 1
                    && m.Parameters[0].ParameterType.FullName == "System.String");
            if (logMessage != null)
                logError = logMessage;
            c.Operand = logError;

            // Ejecución en orden de IL → la siguiente instrucción tras el brfalse de
            // "no existe" es la rama de creación. Buscamos el brfalse previo.
            var branchTarget = default(Instruction);
            for (var j = i - 1; j >= 0; j--)
            {
                if (instrs[j].OpCode == OpCodes.Brfalse || instrs[j].OpCode == OpCodes.Brfalse_S)
                {
                    branchTarget = (Instruction)instrs[j].Operand;
                    break;
                }
            }

            var errRet = instrs[i + 1];
            var target = branchTarget ?? instrs[i + 2];
            il.Replace(errRet, il.Create(OpCodes.Br, target));
            madeChanges = true;
            break;
        }

        if (madeChanges)
        {
            Lg.Info("Parche aplicado: MusicManagerEntry.StartPlaying → guardia de defs + recuperación de dummy existente");
            asmCSharp.Modified = true;
        }
        else
        {
            Lg.Error("No se pudo parchear MusicManagerEntry.StartPlaying (patrón no encontrado).");
        }
    }

    /// <summary>
    /// Inyecta al inicio de Verse.Root.Start(): <c>call SceneRootHook::EnsureRegistered()</c>,
    /// para registrar el hook de transiciones de escena en cualquier Root NUEVO.
    /// </summary>
    private static void PatchRootStartRegisterSceneHook(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var rootStart = module.Types
            .FirstOrDefault(t => t.FullName == "Verse.Root")
            ?.Methods.FirstOrDefault(m => m.Name == "Start");
        if (rootStart == null)
        {
            Lg.Error("No se encontró Verse.Root.Start para parchear SceneRootHook.");
            return;
        }

        var ensureRegistered = module.ImportReference(
            typeof(SceneRootHook).GetMethod(nameof(SceneRootHook.EnsureRegistered)));
        var first = rootStart.Body.Instructions[0];

        // Evita re-parchear si ya está (montaje repetido en memoria).
        if (first.OpCode == OpCodes.Call
            && first.Operand is MethodReference pmr
            && pmr.FullName == ensureRegistered.FullName)
            return;

        var il = rootStart.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Call, ensureRegistered));

        Lg.Info("Parche aplicado: Verse.Root.Start → SceneRootHook.EnsureRegistered (transiciones de escena)");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Inyecta al inicio de RimWorld.Planet.WorldCameraDriver.ApplyPositionToGameObject():
    ///   if (Current.Game == null) ret;
    /// (prefijo <c>call Current::get_Game(); brtrue primera-instr; ret</c>).
    /// </summary>
    private static void PatchWorldCameraDriverApplyPositionGuard(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var method = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.Planet.WorldCameraDriver")
            ?.Methods.FirstOrDefault(m => m.Name == "ApplyPositionToGameObject");
        if (method == null)
        {
            Lg.Error("No se encontró WorldCameraDriver.ApplyPositionToGameObject para parchear.");
            return;
        }

        var currentGameGetter = module.Types
            .FirstOrDefault(t => t.FullName == "Verse.Current")
            ?.Properties.FirstOrDefault(p => p.Name == "Game")
            ?.GetMethod;
        if (currentGameGetter == null)
        {
            Lg.Error("No se encontró Verse.Current.Game para el parche del mundo.");
            return;
        }

        var first = method.Body.Instructions[0];
        if (first.OpCode == OpCodes.Call
            && first.Operand is MethodReference gmr
            && gmr.FullName == currentGameGetter.FullName)
            return;

        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Call, currentGameGetter));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        Lg.Info("Parche aplicado: WorldCameraDriver.ApplyPositionToGameObject → if (Current.Game == null) ret");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Inyecta una llamada a RuntimeHooks.SweepOriginalComponents() al inicio de
    /// RimWorld.WorldInterface.Reset (entrada de la vista de mundo/planeta). Convierte
    /// a tipos NUEVOS los componentes ORIGINALES creados en runtime (cámara de mundo
    /// DontDestroyOnLoad) justo antes de que la vista se active.
    /// </summary>
    private static void PatchWorldInterfaceResetRuntimeSweep(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var method = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.WorldInterface")
            ?.Methods.FirstOrDefault(m => m.Name == "Reset");
        if (method == null)
        {
            Lg.Error("No se encontró RimWorld.WorldInterface.Reset para parchear el sweep de runtime.");
            return;
        }

        var sweep = module.ImportReference(
            typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.SweepOriginalComponents)));

        var first = method.Body.Instructions[0];
        if (first.OpCode == OpCodes.Call
            && first.Operand is MethodReference pmr
            && pmr.FullName == sweep.FullName)
            return;

        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Call, sweep));

        Lg.Info("Parche aplicado: WorldInterface.Reset → RuntimeHooks.SweepOriginalComponents (conversión de runtime)");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Inyecta una llamada a RuntimeHooks.SweepOriginalComponents() justo antes del
    /// ret de RimWorld.Planet.WorldCameraManager.CreateWorldCamera. Convierte a tipo
    /// NUEVO el WorldCameraDriver que AddComponent pudo ligar a ORIGINAL (refonly),
    /// antes de que el ctor estático haga GetComponent<WorldCameraDriver> y asigne
    /// worldCameraDriverInt (que si no, queda null → Find.WorldCameraDriver null).
    /// </summary>
    private static void PatchWorldCameraManagerCreateWorldCameraSweep(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var method = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.Planet.WorldCameraManager")
            ?.Methods.FirstOrDefault(m => m.Name == "CreateWorldCamera");
        if (method == null)
        {
            Lg.Error("No se encontró WorldCameraManager.CreateWorldCamera para parchear el sweep.");
            return;
        }

        var sweep = module.ImportReference(
            typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.SweepOriginalComponents)));

        var ret = method.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret);
        if (ret == null)
            return;

        var prev = ret.Previous;
        if (prev != null && prev.OpCode == OpCodes.Call
            && prev.Operand is MethodReference pmr
            && pmr.FullName == sweep.FullName)
            return;

        var il = method.Body.GetILProcessor();
        il.InsertBefore(ret, il.Create(OpCodes.Call, sweep));

        Lg.Info("Parche aplicado: WorldCameraManager.CreateWorldCamera → SweepOriginalComponents (antes del ret)");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Prefix en RimWorld.Planet.WorldCameraManager.get_WorldCameraDriver:
    ///   WorldCameraDriver get() {
    ///     var v = worldCameraDriverInt;
    ///     if (v != null) return v;          // path normal
    ///     return RuntimeHooks.EnsureWorldCameraDriver();  // auto-reparación
    ///   }
    /// Resolver es válido: el getter original es `ldsfld worldCameraDriverInt; ret`.
    /// </summary>
    private static void PatchWorldCameraManagerGetDriverSelfHeal(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var method = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.Planet.WorldCameraManager")
            ?.Properties.FirstOrDefault(p => p.Name == "WorldCameraDriver")
            ?.GetMethod;
        if (method == null)
        {
            Lg.Error("No se encontró WorldCameraManager.get_WorldCameraDriver para parchear.");
            return;
        }

        var ensureRef = module.ImportReference(
            typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.EnsureWorldCameraDriver)));

        var first = method.Body.Instructions[0];
        // La instrucción original es ldsfld worldCameraDriverInt; ret. Detectamos si ya
        // está parcheado: el first sería una Dup (inyectada) o un Ldsfld (replicado).
        if (first.OpCode == OpCodes.Dup)
            return;

        var origLdFld = method.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldsfld);
        var origRet = method.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret);
        if (origLdFld == null || origRet == null)
            return;

        // Empalme entre el ldsfld original y su ret:
        //   ldsfld worldCameraDriverInt       (original)
        //   dup                                -> v
        //   brtrue.s ret                       -> si v != null devuelve v
        //   pop                                -> descarta el null
        //   call EnsureWorldCameraDriver()     -> devuelve el driver reparado
        //   ret                                (original)
        var il = method.Body.GetILProcessor();
        il.InsertBefore(origRet, il.Create(OpCodes.Dup));
        il.InsertBefore(origRet, il.Create(OpCodes.Brtrue_S, origRet));
        il.InsertBefore(origRet, il.Create(OpCodes.Pop));
        il.InsertBefore(origRet, il.Create(OpCodes.Call, ensureRef));

        Lg.Info("Parche aplicado: WorldCameraManager.get_WorldCameraDriver → auto-reparación si worldCameraDriverInt null");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// SOLUCIÓN DEFINITIVA de la cámara de mundo (tomada de Prepatcher
    /// WorldCameraFreePatch.cs): en WorldCameraManager.CreateWorldCamera, reemplaza la
    /// llamada AddComponent&lt;WorldCameraDriver&gt; (que Unity resuelve POR NOMBRE y
    /// materializa desde el Assembly-CSharp ORIGINAL refonly) por una llamada a
    /// WorldCameraDriverPatch.AttachWorldCameraDriver, que añade la subclase única
    /// ReworkWorldCameraDriver (nombre que no colisiona y que hereda del driver NUEVO).
    /// </summary>
    private static void PatchWorldCameraManagerCreateWorldCameraDriver(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;

        var method = module.Types
            .FirstOrDefault(t => t.FullName == "RimWorld.Planet.WorldCameraManager")
            ?.Methods.FirstOrDefault(m => m.Name == "CreateWorldCamera");
        if (method == null)
        {
            Lg.Error("No se encontró WorldCameraManager.CreateWorldCamera para el parche del driver.");
            return;
        }

        var attachRef = module.ImportReference(
            typeof(WorldCameraDriverPatch).GetMethod(nameof(WorldCameraDriverPatch.AttachWorldCameraDriver)));
        if (attachRef == null)
        {
            Lg.Error("No se pudo importar WorldCameraDriverPatch.AttachWorldCameraDriver.");
            return;
        }

        bool changed = false;
        foreach (var inst in method.Body.Instructions)
        {
            // AddComponent<T>() genérico compila a call instance ...::AddComponent.
            if (inst.Operand is MethodReference mr && mr.Name == "AddComponent")
            {
                inst.Operand = attachRef;
                changed = true;
            }
        }

        if (!changed)
        {
            Lg.Error("No se halló AddComponent en CreateWorldCamera; el parche del driver no se aplicó.");
            return;
        }

        Lg.Info("Parche aplicado: CreateWorldCamera → AddComponent<ReworkWorldCameraDriver> (subclase única)");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Inyecta al final del método Verse.SafeSaver.Save la llamada a RuntimeHooks.OnSafeSaveCompleted(string path)
    /// para crear de forma atómica el archivo binario comprimido (.rwbin) con el centinela #REFORJED.
    /// </summary>
    private static void PatchSafeSaverBinarySave(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;
        var safeSaverType = module.Types.FirstOrDefault(t => t.FullName == "Verse.SafeSaver");
        var saveMethod = safeSaverType?.Methods.FirstOrDefault(m => m.Name == "Save" && m.Parameters.Count == 4);

        if (saveMethod == null)
        {
            Lg.Error("[ReworkBinaryScribe] No se encontró Verse.SafeSaver.Save para inyectar el guardado binario.");
            return;
        }

        var hookMethod = typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.OnSafeSaveCompleted));
        if (hookMethod == null)
        {
            Lg.Error("[ReworkBinaryScribe] No se encontró RuntimeHooks.OnSafeSaveCompleted.");
            return;
        }

        var hookRef = module.ImportReference(hookMethod);
        var il = saveMethod.Body.GetILProcessor();

        // Buscamos todas las instrucciones ret limpias al final del método
        var retInstructions = saveMethod.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var ret in retInstructions)
        {
            // Justo antes del ret, llamamos a OnSafeSaveCompleted(path) pasando el arg 0 (path)
            il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(ret, Instruction.Create(OpCodes.Call, hookRef));
        }

        Lg.Info("[ReworkBinaryScribe] Parche aplicado: Verse.SafeSaver.Save enganchado a ReworkBinaryScribe.");
        Data.DataStore.AppliedPatches.Add("Verse.SafeSaver.Save → ReworkBinaryScribe");
        asmCSharp.Modified = true;
    }

    /// <summary>
    /// Intercepta Verse.ScribeLoader.InitLoading(string filePath) para cargar directamente el XmlDocument
    /// desde el binario (.rwbin) con centinela #REFORJED cuando existe y es íntegro, evitando el parseo de XML.
    /// Si retorna null (no existe o corrupto), continúa normalmente cargando el XML vanilla (.rws).
    /// </summary>
    private static void PatchScribeLoaderBinaryLoad(ModifiableAssembly asmCSharp)
    {
        var module = asmCSharp.ModuleDefinition;
        var loaderType = module.Types.FirstOrDefault(t => t.FullName == "Verse.ScribeLoader");
        var initMethod = loaderType?.Methods.FirstOrDefault(m => m.Name == "InitLoading" && m.Parameters.Count == 1);

        if (initMethod == null)
        {
            Lg.Error("[ReworkBinaryScribe] No se encontró Verse.ScribeLoader.InitLoading.");
            return;
        }

        var hookMethod = typeof(RuntimeHooks).GetMethod(nameof(RuntimeHooks.TryLoadBinaryDocument));
        var curXmlParentField = loaderType?.Fields.FirstOrDefault(f => f.Name == "curXmlParent");

        if (hookMethod == null || curXmlParentField == null)
        {
            Lg.Error("[ReworkBinaryScribe] No se encontró RuntimeHooks.TryLoadBinaryDocument o curXmlParent.");
            return;
        }

        var hookRef = module.ImportReference(hookMethod);
        var fieldRef = module.ImportReference(curXmlParentField);
        var modeField = module.Types.FirstOrDefault(t => t.FullName == "Verse.Scribe")?.Fields.FirstOrDefault(f => f.Name == "mode");

        var il = initMethod.Body.GetILProcessor();
        var first = initMethod.Body.Instructions.First();

        // Creamos una variable local para el XmlElement retornado
        var xmlElemType = module.ImportReference(typeof(System.Xml.XmlElement));
        var elemVar = new VariableDefinition(xmlElemType);
        initMethod.Body.Variables.Add(elemVar);

        // if (TryLoadBinaryDocument(filePath) is XmlElement elem)
        // {
        //     this.curXmlParent = elem;
        //     Scribe.mode = LoadSaveMode.LoadingVars;
        //     return;
        // }
        var contInst = first; // Si es null, saltamos al flujo vanilla existente

        var ldarg1 = Instruction.Create(OpCodes.Ldarg_1); // filePath
        var callHook = Instruction.Create(OpCodes.Call, hookRef);
        var stloc = Instruction.Create(OpCodes.Stloc, elemVar);
        var ldloc = Instruction.Create(OpCodes.Ldloc, elemVar);
        var brfalse = Instruction.Create(OpCodes.Brfalse_S, contInst);

        var ldarg0 = Instruction.Create(OpCodes.Ldarg_0); // this
        var ldloc2 = Instruction.Create(OpCodes.Ldloc, elemVar);
        var stfld = Instruction.Create(OpCodes.Stfld, fieldRef);

        Instruction setMode;
        if (modeField != null)
        {
            var modeFieldRef = module.ImportReference(modeField);
            setMode = Instruction.Create(OpCodes.Stsfld, modeFieldRef);
        }
        else
        {
            setMode = Instruction.Create(OpCodes.Nop);
        }

        var ldc2 = Instruction.Create(OpCodes.Ldc_I4_2); // LoadSaveMode.LoadingVars = 2
        var ret = Instruction.Create(OpCodes.Ret);

        il.InsertBefore(first, ldarg1);
        il.InsertBefore(first, callHook);
        il.InsertBefore(first, stloc);
        il.InsertBefore(first, ldloc);
        il.InsertBefore(first, brfalse);
        il.InsertBefore(first, ldarg0);
        il.InsertBefore(first, ldloc2);
        il.InsertBefore(first, stfld);
        il.InsertBefore(first, ldc2);
        if (modeField != null)
        {
            il.InsertBefore(first, setMode);
        }
        il.InsertBefore(first, ret);

        Lg.Info("[ReworkBinaryScribe] Parche aplicado: Verse.ScribeLoader.InitLoading desvia a descompresión binaria de alta velocidad.");
        Data.DataStore.AppliedPatches.Add("Verse.ScribeLoader.InitLoading → ReworkBinaryScribe");
        asmCSharp.Modified = true;
    }
}
