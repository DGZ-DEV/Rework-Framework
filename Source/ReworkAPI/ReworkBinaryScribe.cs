using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Rework;

/// <summary>
/// Motor del Guardado Binario de Alta Velocidad (ReworkBinaryScribe).
/// Implementa el protocolo dual:
/// 1. Mantiene el XML intacto (.rws) para respaldo seguro.
/// 2. Genera un paquete binario compacto (.rwbin) con compresión Deflate de alta velocidad.
/// 3. Escribe SIEMPRE el marcador final `#REFORJED` como centinela atómico.
/// Si la lectura detecta cualquier interrupción o corrupción, descarta el binario y recurre al XML.
/// </summary>
public static class ReworkBinaryScribe
{
    public const string Sentinel = "#REFORJED";
    public static readonly byte[] SentinelBytes = Encoding.UTF8.GetBytes(Sentinel);
    public const string BinaryExtension = ".rwbin";

    /// <summary>
    /// Comprueba si existe un binario válido y completo (con el centinela #REFORJED al final).
    /// </summary>
    public static bool IsValidBinarySave(string binaryPath)
    {
        try
        {
            if (!File.Exists(binaryPath)) return false;

            using var fs = new FileStream(binaryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < SentinelBytes.Length + 4) return false;

            fs.Seek(-SentinelBytes.Length, SeekOrigin.End);
            byte[] footer = new byte[SentinelBytes.Length];
            int read = fs.Read(footer, 0, footer.Length);
            if (read != footer.Length) return false;

            for (int i = 0; i < SentinelBytes.Length; i++)
            {
                if (footer[i] != SentinelBytes[i]) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Empaqueta el archivo XML (.rws) en un binario de alta velocidad (.rwbin) con el centinela #REFORJED.
    /// </summary>
    public static bool WriteBinaryFromXml(string xmlPath, string binaryPath)
    {
        try
        {
            if (!File.Exists(xmlPath)) return false;

            byte[] xmlBytes = File.ReadAllBytes(xmlPath);

            using (var fs = new FileStream(binaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Cabecera: longitud original en 4 bytes
                byte[] lengthHeader = BitConverter.GetBytes(xmlBytes.Length);
                fs.Write(lengthHeader, 0, 4);

                // Payload: Compresión de alta velocidad
                using (var deflate = new DeflateStream(fs, CompressionMode.Compress, leaveOpen: true))
                {
                    deflate.Write(xmlBytes, 0, xmlBytes.Length);
                    deflate.Flush();
                }

                // Centinela atómico final: #REFORJED
                fs.Write(SentinelBytes, 0, SentinelBytes.Length);
                fs.Flush();
            }

            return true;
        }
        catch
        {
            // Si ocurre cualquier error, eliminamos el archivo parcial para no dejar basura
            if (File.Exists(binaryPath))
            {
                try { File.Delete(binaryPath); } catch { }
            }
            return false;
        }
    }

    /// <summary>
    /// Lee el XML comprimido desde el binario validado (.rwbin).
    /// Si el centinela no está presente, devuelve null para activar el fallback a XML.
    /// </summary>
    public static XmlDocument? LoadXmlFromBinary(string binaryPath)
    {
        if (!IsValidBinarySave(binaryPath)) return null;

        try
        {
            using var fs = new FileStream(binaryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] lengthHeader = new byte[4];
            fs.Read(lengthHeader, 0, 4);
            int originalLength = BitConverter.ToInt32(lengthHeader, 0);

            byte[] decompressed = new byte[originalLength];

            // Leemos el payload hasta antes del centinela
            long payloadLength = fs.Length - 4 - SentinelBytes.Length;
            using var subStream = new MemoryStream();
            
            using (var deflate = new DeflateStream(fs, CompressionMode.Decompress, leaveOpen: true))
            {
                int totalRead = 0;
                while (totalRead < originalLength)
                {
                    int r = deflate.Read(decompressed, totalRead, originalLength - totalRead);
                    if (r <= 0) break;
                    totalRead += r;
                }
            }

            var doc = new XmlDocument();
            using var ms = new MemoryStream(decompressed);
            using var reader = XmlReader.Create(ms);
            doc.Load(reader);
            return doc;
        }
        catch
        {
            return null;
        }
    }
}
