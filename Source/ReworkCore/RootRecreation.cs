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

    // §48: el antiguo RecreateRuntimeOriginalComponents fue ELIMINADO junto con su
    // único consumidor (RuntimeHooks.SweepOriginalComponents, despachador fantasma:
    // sus inyectadores quedaron huérfanos al superarlos la subclase única
    // ReworkWorldCameraDriver). RecreateGameObjectComponents sigue vivo (SwapRoot).
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
