using System;
using Rework.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rework.Core;

/// <summary>
/// Conecta la recreación de Roots a las transiciones de escena, SIN Harmony.
///
/// Cuando Unity carga una escena nueva (Entry→Play, nuevo juego, quicktest...), crea el
/// GameObject GameRoot con un Root (Root_Entry/Root_Play) del Assembly-CSharp ORIGINAL
/// (refonly) porque cachea el binding de los MonoBehaviour de escena al ensamblado con el
/// que se cargó la escena. Ese Root original arrancaría un boot de código original →
/// invocaciones ilegales. Este hook, en cada sceneLoaded, sustituye el Root original por
/// el del tipo NUEVO (RootRecreation.RecreateGameObjectComponents) → el Root nuevo arranca
/// un boot limpio con tipos nuevos.
///
/// Se registra una sola vez (EnsureRegistered) inyectada al inicio de Verse.Root.Start del
/// Assembly-CSharp NUEVO (GameProcessing.PatchRootStartRegisterSceneHook). sceneLoaded se
/// dispara tras el Awake de la escena pero antes del Start de sus objetos → alcanza con
/// destruir/añadir el Root antes de que el original corra su boot.
/// </summary>
public static class SceneRootHook
{
    private static bool registered;

    public static void EnsureRegistered()
    {
        if (registered)
            return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        registered = true;

        // La pasada 1 creó GameObjects persistente (DontDestroyOnLoad) con componentes
        // del Assembly-CSharp ORIGINAL (p.ej. la WorldCamera/WorldCameraDriver del
        // WorldCameraManager). No viven en la escena, así que el gancho de escena no los
        // recrea; al quedar duplicados con los NUEVOS rompen la vista de mundo.
        try
        {
            CleanupOriginalPersistentCameras();
        }
        catch (Exception e)
        {
            Lg.Error($"SceneRootHook: limpieza de cámaras persistentes falló: {e}");
        }
    }

    /// <summary>
    /// Destruye los GameObject de cámara de mundo cuyo componente es de un ensamblado
    /// refonly (ORIGINAL), sin importar el nombre del GO. El WorldCameraManager de la
    /// pasada 1 crea la WorldCamera/WorldSkyboxCamera (DontDestroyOnLoad) con un
    /// WorldCameraDriver ORIGINAL; si no se elimina, su Awake (sin parchear) revienta
    /// y derrumba el ctor estático → Find.WorldCameraDriver null → NREs de la vista de
    /// mundo. (EnsureRegistered corre antes de que la pasada 2 cree las suyas.)
    /// </summary>
    private static void CleanupOriginalPersistentCameras()
    {
        var targetTypes = new[]
        {
            "RimWorld.Planet.WorldCameraDriver",
            "RimWorld.Planet.WorldSkyboxCamera",
        };

        var gosToDestroy = new System.Collections.Generic.HashSet<GameObject>();
        foreach (var obj in UnityEngine.Object.FindObjectsOfTypeAll(typeof(Component)))
        {
            var c = (Component)obj;
            if (c is Transform)
                continue;
            var t = c.GetType();
            var isTarget = t.FullName != null
                && (t.FullName == targetTypes[0] || t.FullName == targetTypes[1]);
            if (!isTarget && c.gameObject.name != "WorldCamera" && c.gameObject.name != "WorldSkyboxCamera")
                continue;

            // Añadimos el GO solo si realmente lleva un componente de juego ORIGINAL (refonly).
            var hasOriginal = false;
            foreach (var comp in c.gameObject.GetComponents<Component>())
            {
                var a = comp.GetType().Assembly;
                if (a.GetName().Name != AssemblyCollector.AssemblyCSharp)
                    continue;
                if (DataStore.RefOnlyOriginals.Contains(a))
                {
                    hasOriginal = true;
                    break;
                }
            }
            if (hasOriginal)
                gosToDestroy.Add(c.gameObject);
        }

        foreach (var go in gosToDestroy)
        {
            UnityEngine.Object.Destroy(go);
            Lg.Info($"SceneRootHook: cámara persistente ORIGINAL '{go.name}' destruida.");
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            var newAsm = RootRecreation.FindNewAssemblyCSharp();
            if (newAsm == null)
            {
                Lg.Error("SceneRootHook: no se encontró el Assembly-CSharp nuevo.");
                return;
            }

            var count = RootRecreation.RecreateScene(scene, newAsm);
            if (count > 0)
                Lg.Info($"SceneRootHook: escena '{scene.name}' cargada; {count} componente(s) re-creado(s) con tipos nuevos.");
        }
        catch (Exception e)
        {
            Lg.Error($"SceneRootHook falló: {e}");
        }
    }
}