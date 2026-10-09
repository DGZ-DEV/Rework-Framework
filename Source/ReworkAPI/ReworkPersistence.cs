using System;
using System.Collections.Generic;
using System.IO;

namespace Rework;

/// <summary>
/// Orquestador de persistencia de los almacenes de servicio de Rework
/// (ReworkColonySkill, ReworkPawnTimeline, ReworkWorldStore,
/// ReworkStateSnapshot y ReworkRelation).
///
/// ANTES de esta integración, cada almacén implementaba SerializeToBinary /
/// DeserializeFromBinary pero NADIE los conectaba a la ruta de guardado:
/// eran código huérfano ("fantasmas de persistencia"). Este archivo los engancha
/// de verdad: se escribe un compañero binario <guardado>.rwdat junto al save
/// (en RuntimeHooks.OnSafeSaveCompleted / TryLoadBinaryDocument) que empaqueta
/// todos los almacenes como un manifiesto con id + bytes.
///
/// Formato: magic "RWRK1" + número de almacenes + por almacén:
///   [int lenId][string id][int lenBytes][bytes]
/// </summary>
public static class ReworkPersistence
{
    private const string Magic = "RWRK1";
    private const string Extension = ".rwdat";

    private static readonly (string id, Func<byte[]> ser, Action<byte[]> des)[] Stores =
    {
        ("ColonySkill",  ReworkColonySkill.SerializeToBinary,   ReworkColonySkill.DeserializeFromBinary),
        ("PawnTimeline", ReworkPawnTimeline.SerializeToBinary,  ReworkPawnTimeline.DeserializeFromBinary),
        ("WorldStore",   ReworkWorldStore.SerializeToBinary,    ReworkWorldStore.DeserializeFromBinary),
        ("StateSnapshot",ReworkStateSnapshot.SerializeToBinary, ReworkStateSnapshot.DeserializeFromBinary),
        ("Relation",     ReworkRelation.SerializeToBinary,      ReworkRelation.DeserializeFromBinary),
    };

    /// <summary>Devuelve la ruta del compañero binario para un path de save dado.</summary>
    public static string CompanionPath(string savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return savePath;
        return savePath + Extension;
    }

    /// <summary>
    /// Serializa todos los almacenes de Rework al archivo compañero (.rwdat).
    /// Llamado por RuntimeHooks.OnSafeSaveCompleted tras un guardado correcto.
    /// </summary>
    public static void SaveAll(string savePath)
    {
        try
        {
            if (string.IsNullOrEmpty(savePath)) return;

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Magic);
                bw.Write(Stores.Length);
                foreach (var (id, ser, _) in Stores)
                {
                    byte[] payload;
                    try { payload = ser(); }
                    catch { payload = Array.Empty<byte>(); }

                    bw.Write(id);
                    bw.Write(payload.Length);
                    bw.Write(payload);
                }
            }

            string target = CompanionPath(savePath);
            File.WriteAllBytes(target, ms.ToArray());
        }
        catch
        {
            // Un fallo al escribir los datos auxiliares no debe romper el guardado.
        }
    }

    /// <summary>
    /// Restaura los almacenes desde el archivo compañero (.rwdat), si existe.
    /// Llamado por RuntimeHooks.TryLoadBinaryDocument (y su fallback XML).
    /// </summary>
    public static void LoadAll(string savePath)
    {
        try
        {
            string target = CompanionPath(savePath);
            if (!File.Exists(target)) return;

            byte[] bytes = File.ReadAllBytes(target);
            if (bytes == null || bytes.Length == 0) return;

            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            if (br.ReadString() != Magic) return;

            int count = br.ReadInt32();
            var payloadById = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string id = br.ReadString();
                int len = br.ReadInt32();
                payloadById[id] = br.ReadBytes(len);
            }

            foreach (var (id, _, des) in Stores)
            {
                if (payloadById.TryGetValue(id, out var payload))
                {
                    try { des(payload); }
                    catch { }
                }
            }
        }
        catch
        {
            // Datos auxiliares corruptos: los almacenes quedan vacíos (no bloquea la carga).
        }
    }

    /// <summary>Limpia todos los almacenes (fin de partida / nueva partida).</summary>
    public static void ClearAll()
    {
        ReworkColonySkill.DeserializeFromBinary(Array.Empty<byte>());
        ReworkPawnTimeline.DeserializeFromBinary(Array.Empty<byte>());
        ReworkWorldStore.Clear();
        ReworkStateSnapshot.Clear();
        ReworkRelation.DeserializeFromBinary(Array.Empty<byte>());
    }
}