using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rework.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using Verse;

namespace Rework.Core;

/// <summary>
/// Recreación de componentes de Root en el hilo principal.
///
/// POR QUÉ ES NECESARIO: la pasada 1 deja "sobras" del arranque ORIGINAL y Unity
/// cachea el binding de los MonoBehaviour de escena (GUID → clase) al ensamblado con
/// el que se cargó. Cuando una escena se carga/recrea (transición Entry→Play, quicktest,
/// nuevo juego...), Unity re-instancia el Root desde el Assembly-CSharp ORIGINAL (refonly)
/// → boot original → invocaciones ilegales. Prepatcher lo soluciona con RecreateComponents
/// (destruir componentes viejos + añadir los del tipo NUEVO, mismo nombre).
///
/// SwapRoot (evento de la pasada 1, encolado ANTES del swap para ligar a la cola ORIGINAL)
/// y SceneRootHook.OnSceneLoaded (cualquier escena posterior) comparten estos helpers.
/// </summary>
public static class RootRecreation
{
    /// <summary>Ensamblado Assembly-CSharp NUEVO (no refonly).</summary>
    public static Assembly FindNewAssemblyCSharp()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a =>
                a.GetName().Name == AssemblyCollector.AssemblyCSharp &&
                !DataStore.RefOnlyOriginals.Contains(a));
    }

    /// <summary>
    /// En un GameObject, destruye los componentes cuyo tipo vive en un ensamblado
    /// refonly (original) y re-añade los del tipo NUEVO con el mismo nombre completo
    /// (mismo patrón que Prepatcher RecreateComponents.cs). Devuelve cuántos recreó.
    /// </summary>
    public static int RecreateGameObjectComponents(GameObject go, Assembly newAsm)
    {
        var oldComps = go.GetComponents<Component>();
        var distinctOldTypes = oldComps
            .Where(c => DataStore.RefOnlyOriginals.Contains(c.GetType().Assembly))
            .Select(c => c.GetType().FullName!)
            .Distinct()
            .ToList();

        foreach (var c in oldComps)
        {
            if (DataStore.RefOnlyOriginals.Contains(c.GetType().Assembly))
                UnityEngine.Object.Destroy(c);
        }

        foreach (var fullName in distinctOldTypes)
        {
            var newType = newAsm.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (newType != null && typeof(MonoBehaviour).IsAssignableFrom(newType))
                go.AddComponent(newType);
        }

        // Música antigua: la pasada 2 no debe encontrar un dummy huérfano (si no,
        // MusicManagerEntry.StartPlaying podría prender de un source vacío).
        var dummy = GameObject.Find("MusicAudioSourceDummy");
        if (dummy != null)
            UnityEngine.Object.Destroy(dummy);

        return distinctOldTypes.Count;
    }

    /// <summary>
    /// Recreación completa de la escena: recorre TODOS los GameObject (raíces de la
    /// escena + descendientes, recursivo) y convierte a tipos NUEVOS los componentes
    /// cuyo tipo vive en un ensamblado refonly (original). Equivalente al
    /// RecreateComponents.cs de Prepatcher conectado a cada cambio de escena.
    /// Devuelve el número total de componentes recreados.
    /// </summary>
    public static int RecreateScene(Scene scene, Assembly newAsm)
    {
        var count = 0;
        var stack = new Stack<Transform>();
        foreach (var rootGo in scene.GetRootGameObjects())
            stack.Push(rootGo.transform);

        var processed = new HashSet<GameObject>();
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            var go = t.gameObject;
            if (!processed.Add(go))
                continue;
            count += RecreateGameObjectComponents(go, newAsm);
            foreach (Transform child in t)
                stack.Push(child);
        }

        return count;
    }

    /// <summary>
    /// Conversión de componentes ORIGINALES creados en RUNTIME (no en el escenario).
    /// La vista de mundo crea GameObjects persistente (DontDestroyOnLoad) — WorldCamera,
    /// WorldSkyboxCamera — desde código que a veces liga al tipo ORIGINAL (refonly),
    /// por lo que su MonoBehaviour (p.ej. WorldCameraDriver) despierta sin el parche de
    /// la pasada 2 y revienta (→ ctor estático de WorldCameraManager aborta →
    /// Find.WorldCameraDriver null → NREs en cadena del globo).
    ///
    /// Barrer con FindObjectsOfTypeAll también devuelve ACTIVOS/prefabs; se filtran con
    /// go.scene.isLoaded (solo objetos de escenas cargadas o DontDestroyOnLoad), para no
    /// corromper recursos. Devuelve cuántos componente recreó con tipos nuevos.
    /// </summary>
    public static int RecreateRuntimeOriginalComponents(Assembly newAsm)
    {
        var gos = new HashSet<GameObject>();
        foreach (var obj in UnityEngine.Object.FindObjectsOfTypeAll(typeof(Component)))
        {
            var c = (Component)obj;
            if (c is Transform || c is RectTransform)
                continue;
            var a = c.GetType().Assembly;
            if (a.GetName().Name != AssemblyCollector.AssemblyCSharp)
                continue;
            if (!DataStore.RefOnlyOriginals.Contains(a))
                continue;
            // igual que el RecreateComponents de Prepatcher: convertimos el componente
            // ORIGINAL (refonly) esté donde esté, incluidos los DontDestroyOnLoad que
            // crea la vista de mundo (p.ej. WorldCameraDriver). No filtramos por
            // scene.isLoaded: la cámara de mundo exit no la cumple y se saltaría.
            gos.Add(c.gameObject);
        }

        var count = 0;
        foreach (var go in gos)
            count += RecreateGameObjectComponents(go, newAsm);
        return count;
    }
    /// LongEventHandler). oldRootType se captura COMO VALOR en la pasada 1 (Verse.Root
    /// original); el ensamblado nuevo se resuelve en tiempo de ejecución (ver bb.).
    /// </summary>
    public static void SwapRoot(Type oldRootType)
    {
        try
        {
            var oldRoot = UnityEngine.Object.FindObjectOfType(oldRootType);
            if (oldRoot == null)
            {
                Lg.Error("RootRecreation: no se encontró el Root original para recrear.");
                return;
            }

            var newAsm = FindNewAssemblyCSharp();
            if (newAsm == null)
            {
                Lg.Error("RootRecreation: no se encontró el Assembly-CSharp nuevo.");
                return;
            }

            RecreateGameObjectComponents(((Component)oldRoot).gameObject, newAsm);
            Lg.Info("Root recreado: componentes del GameRoot re-creados con tipos nuevos. Boot reiniciado.");
        }
        catch (Exception e)
        {
            Lg.Error($"RootRecreation falló: {e}");
        }
    }
}