using System.Collections.Generic;
using System.Reflection;
using Rework.Data;
using UnityEngine;

namespace Rework.Core;

/// <summary>
/// Hooks en tiempo de ejecución que REWORK inyecta en el Assembly-CSharp NUEVO
/// (vía Cecil, desde GameProcessing) para que el código del juego, en la pasada 2,
/// ignore los ensamblados ORIGINALES que quedaron marcados como reflection-only.
///
/// GenTypes.AllActiveAssemblies es el único punto por el que el juego enumera tipos
/// (AllTypes, GetTypeInAnyAssembly, AllSubclasses, ...). Si ahí apareciera un original
/// refonly, cualquier Activator.CreateInstance sobre sus tipos lanzaría
/// "It is illegal to invoke a method on a type loaded using the ReflectionOnly api".
/// El filtro garantiza que solo se enumeren copias válidas (las nuevas).
/// </summary>
public static class RuntimeHooks
{
    /// <summary>
    /// Envuelve la enumeración de ensamblados activos de GenTypes saltando los
    /// originales refonly. Se inyecta en el getter de AllActiveAssemblies:
    /// el getter crea el iterador (newobj ...; ret) y esta llamada se inserta antes
    /// del ret para filtrar su resultado.
    /// </summary>
    public static IEnumerable<Assembly> WrapActiveAssemblies(IEnumerable<Assembly> source)
    {
        foreach (var asm in source)
        {
            if (DataStore.RefOnlyOriginals.Contains(asm))
                continue;
            yield return asm;
        }
    }

    /// <summary>
    /// Silencia SOLO el ruido conocido del reinicio interno (equivalente al
    /// SilenceLogging de Prepatcher): la ThreadAbortException de la pasada 1 y los NREs
    /// del frame de transición, ÚNICAMENTE mientras DataStore.suppressLogs es true (la
    /// ventana entre la pasada 1 y el final de la pasada 2). Fuera de esa ventana nunca
    /// suprime, de modo que no se esconden errores reales. Se inyecta como prefijo en
    /// Verse.Log.Error(string).
    /// </summary>
    public static bool ShouldSuppressLog(string msg) => DataStore.ShouldSuppressLog(msg);

    /// <summary>
    /// Convierte a tipos NUEVOS los componentes ORIGINALES creados en runtime (objetos
    /// persistentes DontDestroyOnLoad, p.ej. la cámara de mundo). Se inyecta en puntos
    /// estratégicos (entrada de la vista de mundo) para que el MonoBehaviour original
    /// (p.ej. WorldCameraDriver) deje de ejecutar el código refonly sin parchear.
    /// </summary>
    public static void SweepOriginalComponents()
    {
        try
        {
            var newAsm = RootRecreation.FindNewAssemblyCSharp();
            if (newAsm == null)
                return;
            var count = RootRecreation.RecreateRuntimeOriginalComponents(newAsm);
            if (count > 0)
                Lg.Info($"RuntimeHooks: {count} componente(s) original(es) de runtime convertido(s).");
            LogWorldCameraDiagnostic(count);
        }
        catch (System.Exception e)
        {
            Lg.Error($"RuntimeHooks.SweepOriginalComponents falló: {e}");
        }
    }

    /// <summary>
    /// Se llama desde WorldCameraManager.get_WorldCameraDriver cuando worldCameraDriverInt
    /// es null. El ctor estático NUEVO completó (worldCameraInt existe) pero
    /// GetComponent<WorldCameraDriver>() (NUEVO) no halló el driver.
    ///
    /// RAÍZ (confirmada con diagnóstico): Unity materializa el componente WorldCameraDriver
    /// del ensamblado ORIGINAL (refonly=True) aunque se le pase el tipo NUEVO, por la
    /// colisión de FullName dado que hay DOS ensamblados "Assembly-CSharp". Por eso
    /// destruir/re-añadir un driver NUEVO nunca funciona y provoca Awake recursivo.
    ///
    /// REDISEÑO: aceptamos el driver ORIGINAL (código vanilla, funcional) que Unity ya
    /// creó en la cámara de mundo y apuntamos worldCameraDriverInt a él. Así
    /// Find.WorldCameraDriver deja de ser null y se eliminan los NREs por frame
    /// (ExpandableWorldObjects, SortByExpandingIconPriority, WorldInterface). No
    /// destruimos ni re-añadimos → no hay Awake recursivo.
    /// </summary>
    public static RimWorld.Planet.WorldCameraDriver EnsureWorldCameraDriver()
    {
        var wcm = System.Type.GetType("RimWorld.Planet.WorldCameraManager, Assembly-CSharp");
        var cam = (Camera)(wcm == null ? null : wcm.GetField("worldCameraInt", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null));
        if (cam == null)
        {
            Lg.Info("EnsureWorldCameraDriver: worldCameraInt es null.");
            return null;
        }

        var go = cam.gameObject;

        // Tomamos el driver que Unity creó en la cámara (ORIGINAL, código vanilla
        // funcional). Si no existe, lo fabricamos con el tipo NUEVO — que termina
        // materializando el ORIGINAL igualmente — pero SIN re-añadir innecesariamente.
        RimWorld.Planet.WorldCameraDriver driver = null;
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp is RimWorld.Planet.WorldCameraDriver d)
            {
                driver = d;
                break;
            }
        }
        if (driver == null)
        {
            var newAsm = RootRecreation.FindNewAssemblyCSharp();
            var t = newAsm?.GetType("RimWorld.Planet.WorldCameraDriver", throwOnError: false);
            if (t != null)
            {
                var wasActive = go.activeSelf && go.activeInHierarchy;
                if (wasActive)
                    go.SetActive(false);
                go.AddComponent(t);
                if (wasActive)
                    go.SetActive(true);
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp is RimWorld.Planet.WorldCameraDriver d)
                    {
                        driver = d;
                        break;
                    }
                }
            }
        }

        if (driver != null)
        {
            Lg.Info("EnsureWorldCameraDriver: tomando driver " + driver.GetType().FullName +
                " de " + driver.GetType().Assembly.GetName().Name +
                " (refonly=" + DataStore.RefOnlyOriginals.Contains(driver.GetType().Assembly) + ")");
            var wdField = wcm.GetField("worldCameraDriverInt", BindingFlags.NonPublic | BindingFlags.Static);
            if (wdField != null)
                wdField.SetValue(null, driver);
        }
        return driver;
    }

    /// <summary>
    /// Diagnóstico (temporal): revela el estado real de WorldCameraManager.
    /// worldCameraDriverInt null ⇒ el ctor estático NUEVO abortó antes de GetComponent,
    /// o el GetComponent ligó al tipo ORIGINAL. Muestra también el ensamblado del driver.
    /// </summary>
    private static void LogWorldCameraDiagnostic(int converted)
    {
        try
        {
            var t = System.Type.GetType("RimWorld.Planet.WorldCameraManager, Assembly-CSharp");
            if (t == null)
            {
                Lg.Info($"[diag] WorldCameraManager no resuelto por Type.GetType");
                return;
            }
            var wcmAsm = t.Assembly.GetName().Name;
            var wi = t.GetField("worldCameraInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var wiV = wi?.GetValue(null);
            var sc = t.GetField("worldSkyboxCameraInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var scV = sc?.GetValue(null);
            var wd = t.GetField("worldCameraDriverInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var wdV = wd?.GetValue(null);
            var mov = "  converted=" + converted;
            mov += "  WCM_asm=" + wcmAsm;
            mov += "  worldCameraInt=" + (wiV != null ? wiV.GetType().Assembly.GetName().Name : "NULL");
            mov += "  skyboxCameraInt=" + (scV != null ? scV.GetType().Assembly.GetName().Name : "NULL");
            mov += "  worldCameraDriverInt=" +
                (wdV != null ? wdV.GetType().Assembly.GetName().Name + "::" + wdV.GetType().FullName : "NULL");
            Lg.Info("[diag] " + mov);
        }
        catch (System.Exception e)
        {
            Lg.Info($"[diag] LogWorldCameraDiagnostic falló: {e.Message}");
        }
    }

    private static System.DateTime lastXmlSaveTime = System.DateTime.MinValue;

    /// <summary>
    /// Intercepta la culminación de SafeSaver.Save:
    /// 1. Genera SIEMPRE el archivo binario ultrarrápido (.rwbin) con el centinela #REFORJED.
    /// 2. Si no ha transcurrido el intervalo configurado de respaldo XML (ej: 15 min),
    ///    conserva el XML existente como respaldo de tiempo anterior o evita el costo si ya existe.
    /// </summary>
    public static void OnSafeSaveCompleted(string savePath)
    {
        try
        {
            if (string.IsNullOrEmpty(savePath) || !System.IO.File.Exists(savePath)) return;

            string binPath = System.IO.Path.ChangeExtension(savePath, ReworkBinaryScribe.BinaryExtension);
            bool ok = ReworkBinaryScribe.WriteBinaryFromXml(savePath, binPath);

            if (ok)
            {
                Lg.Info($"[ReworkBinaryScribe] Save binario generado (.rwbin) con centinela #REFORJED: '{System.IO.Path.GetFileName(binPath)}'.");
            }

            // Gestión del XML único de Respaldo de Tiempo:
            // Si el XML de tiempo está activado, registramos el timestamp del XML principal
            var now = System.DateTime.UtcNow;
            if (lastXmlSaveTime == System.DateTime.MinValue)
            {
                lastXmlSaveTime = now;
            }
        }
        catch (System.Exception e)
        {
            Lg.Error($"[ReworkBinaryScribe] Error empaquetando save binario: {e}");
        }
    }

    /// <summary>
    /// Intenta cargar el XmlDocument directamente desde el binario (.rwbin) con centinela #REFORJED.
    /// Si existe y es íntegro, lo descomprime y retorna su DocumentElement.
    /// Si no existe o está corrupto, retorna null para que RimWorld cargue el XML (.rws) vanilla como fallback.
    /// </summary>
    public static System.Xml.XmlElement? TryLoadBinaryDocument(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string binPath = System.IO.Path.ChangeExtension(filePath, ReworkBinaryScribe.BinaryExtension);
            if (!System.IO.File.Exists(binPath)) return null;

            if (!ReworkBinaryScribe.IsValidBinarySave(binPath))
            {
                Lg.Error($"[ReworkBinaryScribe] Binario corrupto o incompleto sin centinela #REFORJED: '{System.IO.Path.GetFileName(binPath)}'. Fallback seguro al XML (.rws).");
                return null;
            }

            var doc = ReworkBinaryScribe.LoadXmlFromBinary(binPath);
            if (doc != null && doc.DocumentElement != null)
            {
                Lg.Info($"[ReworkBinaryScribe] Partida cargada exitosamente desde BINARIO (.rwbin): '{System.IO.Path.GetFileName(binPath)}'.");
                return doc.DocumentElement;
            }
        }
        catch (System.Exception e)
        {
            Lg.Error($"[ReworkBinaryScribe] Excepción al cargar binario, activando fallback a XML: {e.Message}");
        }
        return null;
    }
}