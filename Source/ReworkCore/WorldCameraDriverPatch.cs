using UnityEngine;

namespace Rework.Core;

/// <summary>
/// Subclase ÚNICA del driver de cámara de mundo (tomada de Prepatcher
/// WorldCameraDriver2.cs). Prepatcher descubrió que Unity resuelve el tipo
/// WorldCameraDriver POR NOMBRE y lo materializa desde el Assembly-CSharp ORIGINAL
/// (refonly), provocando la colisión de la vista de mundo cuando existen dos
/// ensamblados "Assembly-CSharp".
///
/// Esta subclase se define en el ensamblado ReworkCore (un nombre que NO colisiona),
/// y hacemos que WorldCameraManager.CreateWorldCamera la añada en vez del driver
/// por-nombre. Al heredar del WorldCameraDriver NUEVO, GetComponent&lt;WorldCameraDriver&gt;()
/// del ctor la encuentra → worldCameraDriverInt se asigna bien → Find.WorldCameraDriver
/// deja de ser null → la vista de mundo/planeta carga sin NREs.
/// </summary>
public sealed class ReworkWorldCameraDriver : RimWorld.Planet.WorldCameraDriver
{
}

/// <summary>
/// Helper estático que sustituye la llamada AddComponent&lt;WorldCameraDriver&gt; dentro de
/// WorldCameraManager.CreateWorldCamera (que el parche Cecil reemplaza en el cuerpo del
/// método). Devuelve Component (igual que Prepatcher) para no romper el uso/descartes del
/// valor de retorno en el método original.
/// </summary>
public static class WorldCameraDriverPatch
{
    public static Component AttachWorldCameraDriver(GameObject gameObject)
    {
        return gameObject.AddComponent<ReworkWorldCameraDriver>();
    }
}