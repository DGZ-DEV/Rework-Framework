using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Rework.Core;
using Rework.Data;
using RimWorld;
using Verse;

namespace Rework;

/// <summary>
/// Punto de entrada del mod. El constructor corre durante
/// LoadedModManager.InitializeMods(): el juego ya cargó Assembly-CSharp pero
/// todavía no la usa, por lo que es el punto de enganche más temprano que tiene
/// un mod (igual que Prepatcher PrepatcherMod.cs).
///
/// MODELO DE DOS PASADAS EN MEMORIA (como Prepatcher; NO toca el DLL en disco):
///   Pasada 1 → Loader.Reload() (rewrite con Mono.Cecil + reload) y
///              Thread.CurrentThread.Abort(): el juego "reinicia en su sitio" con
///              los ensamblados ya parcheados. El abort "cuenta como crash", por
///              eso hay que desactivar el reseteo de ModsConfig.
///   Pasada 2 → DataStore.startedOnce == true → solo vaciar logs y verificar.
///
/// La cámara de mundo se resuelve con la subclase única ReworkWorldCameraDriver
/// (ver ReworkCore/WorldCameraDriverPatch.cs), igual que Prepatcher con su
/// WorldCameraDriver2.
/// </summary>
public class ReworkMod : Mod
{
    public static ReworkSettings Settings { get; private set; } = null!;

    public ReworkMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<ReworkSettings>();
        Settings.SyncToConfig();

        if (DataStore.startedOnce)
        {
            // Pasada 2: ensamblados ya parcheados; no hay nada que reescribir.
            // Reafirmamos resetModsConfigOnCrash=false porque en esta pasada Prefs es
            // un estático NUEVO (ensamblado nuevo) con el valor por defecto.
            Prefs.data.resetModsConfigOnCrash = false;
            Lg.FlushBuffered();
            VerifyPatch();
            // Init patches: código de cualquier mod activo que corre UNA vez,
            // con el juego ya operativo y campos/parches aplicados.
            InitRunner.Run();
            // 17.2/17.12 — resumen de salud: muestra al usuario los fallos que hubo en la
            // pasada 1 (recuperación parcial: el boot siguió, pero el usuario lo ve).
            if (ReworkHealth.HasFailures)
                Lg.Error(ReworkHealth.Summary());
            // Fin de la ventana de silencio: a partir de aquí los errores vuelven a verse.
            DataStore.suppressLogs = false;
            return;
        }

        // --- Pasada 1 ---
        Lg.Info("Pasada 1 iniciada");

        // §47 — GUARDIA ANTI-BUCLE (a prueba de identidad de ensamblados): cuenta
        // las ejecuciones de la pasada 1 en ESTE proceso con una variable de
        // entorno, que sobrevive a CUALQUIER recarga (incluso a una hipotética de
        // 0ReworkData, que resetearía todos los estáticos del framework, incluido
        // startedOnce). Si la pasada 1 intenta correr más de 2 veces, forzamos la
        // rama de pasada 2: un bucle de arranque infinito se convierte en boot
        // degradado + diagnóstico ruidoso en el log.
        int pass1Runs = 0;
        int.TryParse(Environment.GetEnvironmentVariable("REWORK_PASS1_RUNS"), out pass1Runs);
        pass1Runs++;
        Environment.SetEnvironmentVariable("REWORK_PASS1_RUNS", pass1Runs.ToString());
        if (pass1Runs > 2)
        {
            Lg.Error($"[Rework] GUARDIA ANTI-BUCLE (§47): la pasada 1 intentó ejecutarse {pass1Runs} veces " +
                     "en este proceso; se fuerza la rama de pasada 2 para no colgar el arranque. Causa típica: " +
                     "un ensamblado de barrera (0ReworkData) entró al swap y resetea startedOnce — ver la " +
                     "línea 'Reload: intercambiando' y ERRORES.md §47.");
            Prefs.data.resetModsConfigOnCrash = false;
            Lg.FlushBuffered();
            VerifyPatch();
            InitRunner.Run();
            if (ReworkHealth.HasFailures)
                Lg.Error(ReworkHealth.Summary());
            DataStore.suppressLogs = false;
            return;
        }

        // 0) Entorno (bloque 1): modo seguro PROACTIVO (configurable con 18.6).
        //    Solo reescribimos si el runtime es Mono y el layout nativo (offsets de
        //    UnsafeAssembly) verifica en esta build (o si el usuario desactivó el modo seguro).
        var envSafe = RuntimeEnvironment.IsSafeToRewrite();
        var safe = !Settings.enableSafeMode || envSafe;
        Lg.Info($"Entorno: {RuntimeEnvironment.Summary()} (modo seguro={(Settings.enableSafeMode ? "activo" : "desactivado")}) → {(safe ? "seguro para reescribir" : "MODO SEGURO: no se reescribirá")}");
        if (!safe)
        {
            Lg.Error("Rework Reforjed: runtime/detecación no soportado para el rewrite. " +
                     "El juego arrancará SIN los parches de Rework (mod inerte, sin romper nada).");
            DataStore.startedOnce = true; // evita re-entrar
            return;
        }

        // Instalar el filtro del ruido de reinicio ANTES del reload (los métodos del
        // filtro deben quedar JIT antes de marcar ReworkCore refonly). Silencia el
        // ThreadAbortException y los NREs del frame de transición durante la ventana.
        LogSilence.Install();

        // 1) Preparar el reinicio ANTES del swap: las referencias al juego
        //    (Verse.LongEventHandler, Verse.Root, Verse.Prefs) resueltas aquí apuntan al
        //    Assembly-CSharp ORIGINAL (el que aún ejecuta). La recreación del Root se
        //    encola y se ejecuta en el hilo principal al morir el hilo de fondo.
        BootControl.CancelBootAndScheduleRootSwap();
        Prefs.data.resetModsConfigOnCrash = false;

        // 2) Swap: rewrite (Cecil) + reload (refonly originales + Assembly.Load(bytes)).
        // 17.8/17.9 — Modo desarrollador: un archivo ReworkDev.txt en la carpeta del mod
        // limita el framework a aplicar solo los parches/inits/hooks de ese mod o de un
        // parche concreto (para aislar un fallo o revertir uno sin quitarlo del mod).
        // Formato: OnlyPatch=Tipo.Metodo  o  OnlyMod=NamespaceDelMod
        LoadDevMode();
        Loader.Reload();

        // startedOnce DEBE ir después de Reload() para que los logs de la pasada 1
        // se buffericen.
        DataStore.startedOnce = true;

        // Abrir la ventana de silencio del ruido del reinicio (ThreadAbortException +
        // NREs del frame de transición) hasta que la pasada 2 termine de verificar.
        DataStore.suppressLogs = true;

        Thread.CurrentThread.Abort();
    }

    /// <summary>
    /// Verificación de la pasada 2: comprueba que el rewrite + swap funcionó.
    /// 1) La Assembly-CSharp que corre es la NUEVA (no aparece en RefOnlyOriginals).
    /// 2) La subclase única del driver de mundo sigue correctamente enganchada.
    /// 3) El juego es la versión soportada (1.6.x rev646); si no, avisa.
    /// No depende de campos demo: es la invariante del propio framework.
    /// </summary>
    private static void VerifyPatch()
    {
        try
        {
            var runningAsm = typeof(Game).Assembly;
            var runningNew = !DataStore.RefOnlyOriginals.Contains(runningAsm);
            var driverHooked =
                typeof(Rework.Core.ReworkWorldCameraDriver).BaseType == typeof(RimWorld.Planet.WorldCameraDriver);
            Lg.Info(runningNew && driverHooked
                ? "VERIFICACIÓN OK: Assembly-CSharp activa es la NUEVA (rewrite en efecto) y ReworkWorldCameraDriver enganchado."
                : $"VERIFICACIÓN FALLIDA: runningNew={runningNew}, driverHooked={driverHooked}");
        }
        catch (Exception e)
        {
            Lg.Error($"La verificación lanzó una excepción: {e}");
        }

        CheckGameVersion();
    }

    /// <summary>
    /// Chequeo de versión en runtime: Rework reescribe la Assembly-CSharp en memoria
    /// con Mono.Cecil, por lo que SOLO es seguro para la versión contra la que se
    /// compiló (1.6.4850 rev646). Si el juego ejecutivo no coincide, avisamos claro
    /// en el log en vez de dejar una partida rota silenciosa.
    /// </summary>
    private static void CheckGameVersion()
    {
        try
        {
            var v = RimWorld.VersionControl.CurrentVersionString; // p.ej. "1.6.4850 rev646"
            var ok = v == "1.6.4850 rev646"
                || v.StartsWith("1.6.4850", StringComparison.Ordinal);
            if (ok)
            {
                Lg.Info($"Versión del juego soportada: {v}");
            }
            else
            {
                Lg.Error($"¡Versión del juego NO soportada! Actual: {v}. " +
                         "Rework Reforjed fue compilado para RimWorld 1.6.4850 rev646. " +
                         "Si el juego cambió, el rewrite puede no ser seguro.");
            }
        }
        catch (Exception e)
        {
            Lg.Error($"No se pudo comprobar la versión del juego: {e}");
        }
    }

    /// <summary>
    /// 17.8/17.9 — Lee un archivo `ReworkDev.txt` en la carpeta del mod (ContentPack.RootDir)
    /// para activar el modo desarrollador: aislar un parche (OnlyPatch) o un mod (OnlyMod)
    /// sin quitarlo del mod. Formato, una línea por clave:
    ///   OnlyPatch=ReworkDemo.ReworkDemoPatch.ParcheMultiplicador
    ///   OnlyMod=ReworkDemo
    /// </summary>
    private static void LoadDevMode()
    {
        try
        {
            var root = typeof(ReworkMod).Assembly.Location;
            if (string.IsNullOrEmpty(root)) return;
            var dir = Path.GetDirectoryName(root);
            var path = Path.Combine(dir, "ReworkDev.txt");
            if (!File.Exists(path)) return;

            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var key = t.Substring(0, eq).Trim();
                var val = t.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(val)) continue;

                switch (key)
                {
                    case "OnlyPatch":
                        ReworkDevMode.OnlyPatch = val;
                        Lg.Info($"Modo desarrollador: OnlyPatch={val} (aplica solo ese parche).");
                        break;
                    case "OnlyMod":
                        ReworkDevMode.OnlyMod = val;
                        Lg.Info($"Modo desarrollador: OnlyMod={val} (aplica solo los parches/hooks/inits de ese mod).");
                        break;
                }
            }
        }
        catch
        {
            // Un archivo de modo desarrollador mal formado no debe romper el boot.
        }
    }

    /// <summary>18.1 — Categoría del menú de opciones de RimWorld.</summary>
    public override string SettingsCategory() => "Rework Reforjed";

    /// <summary>18.1 — Ventana interactiva de configuración con controles para 18.2–18.10.</summary>
    public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        Text.Font = GameFont.Medium;
        listing.Label("Rework Reforjed — Configuración");
        Text.Font = GameFont.Small;
        listing.Gap();

        // 18.2 Perfil de rendimiento
        listing.Label($"Perfil de rendimiento actual: {Settings.perfProfile}");
        if (listing.ButtonText("Cambiar perfil de rendimiento"))
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>
            {
                new FloatMenuOption("Equilibrado", () => Settings.ApplyProfile(PerfProfile.Equilibrado)),
                new FloatMenuOption("Máximo Rendimiento", () => Settings.ApplyProfile(PerfProfile.MaximoRendimiento)),
                new FloatMenuOption("Ahorro de Memoria", () => Settings.ApplyProfile(PerfProfile.AhorroMemoria))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        listing.Gap(6f);

        // 18.3 Perfil de compatibilidad
        listing.Label($"Perfil de compatibilidad actual: {Settings.compatProfile}");
        if (listing.ButtonText("Cambiar perfil de compatibilidad"))
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>
            {
                new FloatMenuOption("Estándar", () => Settings.ApplyProfile(CompatProfile.Estandar)),
                new FloatMenuOption("Estricto", () => Settings.ApplyProfile(CompatProfile.Estricto)),
                new FloatMenuOption("Permisivo", () => Settings.ApplyProfile(CompatProfile.Permisivo))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        listing.Gap(12f);
        listing.GapLine();
        listing.Gap(12f);

        // 18.4 Multihilo
        bool prevMulti = Settings.enableMultithreading;
        listing.CheckboxLabeled("Activar multihilo (Rework.Threading)", ref Settings.enableMultithreading, "Habilita el pool de workers para tareas asíncronas de mods.");
        if (prevMulti != Settings.enableMultithreading)
        {
            Settings.SyncToConfig();
            Rework.Threading.ReworkJobs.ResetPool();
        }

        // 18.5 Backend de serialización
        bool prevScribe = Settings.enableAutoScribe;
        listing.CheckboxLabeled("Activar serialización automática (FieldScribe)", ref Settings.enableAutoScribe, "Inyecta llamadas Scribe automáticamente en campos con [ReworkField(Serialize=true)].");
        if (prevScribe != Settings.enableAutoScribe)
            Settings.SyncToConfig();

        // 18.6 Modo seguro
        bool prevSafe = Settings.enableSafeMode;
        listing.CheckboxLabeled("Activar modo seguro proactivo", ref Settings.enableSafeMode, "Verifica el entorno Mono y layout nativo antes de realizar el rewrite.");
        if (prevSafe != Settings.enableSafeMode)
            Settings.SyncToConfig();

        // 18.7 Logs detallados
        bool prevVerbose = Settings.verboseLogs;
        listing.CheckboxLabeled("Activar logs detallados (Verbose)", ref Settings.verboseLogs, "Muestra información de diagnóstico extendida en Rework.log.");
        if (prevVerbose != Settings.verboseLogs)
            Settings.SyncToConfig();

        listing.Gap(12f);

        // 18.8 Configuración por mod
        listing.Label($"Mods excluidos del parcheo: {(Settings.excludedMods.Count > 0 ? string.Join(", ", Settings.excludedMods) : "Ninguno")}");
        if (listing.ButtonText("Gestionar exclusión de mods..."))
        {
            var options = new System.Collections.Generic.List<FloatMenuOption>();
            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                var modId = mod.PackageIdPlayerFacing;
                var isExcluded = Settings.excludedMods.Contains(modId);
                var label = (isExcluded ? "[EXCLUIDO] " : "[ACTIVO] ") + mod.Name;
                options.Add(new FloatMenuOption(label, () =>
                {
                    Settings.excludedMods.Toggle(modId);
                    Settings.SyncToConfig();
                }));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        listing.Gap(12f);
        listing.GapLine();
        listing.Gap(6f);

        // Sección ReworkLive (Hot-Reload)
        Text.Font = GameFont.Medium;
        listing.Label("ReworkLive — Hot-Reload en Tiempo Real");
        Text.Font = GameFont.Small;
        listing.Label($"Monitoreando {ReworkLiveEngine.WatchedCount} DLLs externas.");

        if (listing.ButtonText("⚡ Recargar DLLs modificadas ahora (Sin reiniciar)"))
        {
            bool any = ReworkLiveEngine.CheckForChangesAndReload();
            Messages.Message(any ? "ReworkLive: Mods recargados en caliente con éxito." : "ReworkLive: No se detectaron archivos DLL modificados.", MessageTypeDefOf.PositiveEvent, false);
        }

        listing.Gap(12f);
        listing.GapLine();
        listing.Gap(6f);

        // Sección ReworkBinaryScribe — XML de Respaldo de Tiempo
        Text.Font = GameFont.Medium;
        listing.Label("ReworkBinaryScribe — Respaldo de Tiempo XML");
        Text.Font = GameFont.Small;

        bool prevXmlBackup = Settings.enableXmlBackup;
        listing.CheckboxLabeled(
            "Activar sincronización periódica de XML (.rws)",
            ref Settings.enableXmlBackup,
            "Mantiene un único XML (.rws) por partida que se sincroniza silenciosamente cada X minutos. " +
            "Los guardados comunes usan el binario (.rwbin) al instante. Si el binario se corrompe, este XML sirve de salvavidas.");
        if (prevXmlBackup != Settings.enableXmlBackup) Settings.SyncToConfig();

        if (Settings.enableXmlBackup)
        {
            listing.Label($"Intervalo de actualización XML: cada {Settings.xmlBackupIntervalMinutes} minutos.");
            Settings.xmlBackupIntervalMinutes = (int)listing.Slider(Settings.xmlBackupIntervalMinutes, 5f, 120f);

            string? xmlPath = GameComponent_ReworkXmlBackup.GetPrimaryXmlPath();
            bool xmlExists = xmlPath != null && System.IO.File.Exists(xmlPath);
            if (xmlExists)
            {
                var lastWrite = System.IO.File.GetLastWriteTime(xmlPath!);
                listing.Label($"Último XML sincronizado: {lastWrite:HH:mm:ss dd/MM/yyyy}");
            }
            else
            {
                listing.Label("Último XML: aún no se generó para la partida actual.");
            }
        }

        listing.Gap(16f);

        // Bloque 19: Inspector de Runtime y DevTools
        if (listing.ButtonText("🔍 Abrir Rework DevTools & Inspector de Runtime (Bloque 19)"))
        {
            Find.WindowStack.Add(new Dialog_ReworkInspector());
        }

        listing.Gap(12f);

        // 18.10 Resetear configuración
        if (listing.ButtonText("Restablecer valores por defecto"))
        {
            Settings.Reset();
        }

        listing.End();
    }
}