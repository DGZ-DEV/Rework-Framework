using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Rework.Data;

namespace Rework.Core;

/// <summary>
/// Acceso a las estructuras internas del runtime Mono de Unity.
/// Este es el equivalente a Prepatcher
/// Source/Implementation/UnsafeAssembly.cs (mismo diseño, implementación propia).
///
/// MECANISMO DE DUPLICACIÓN (el corazón del reloader):
/// Mono de Unity no permite cargar dos ensamblados con el mismo nombre/versión.
/// La flag "reflection-only" del struct nativo MonoAssembly hace que el buscador
/// interno de Mono SALTE ese ensamblado al resolver por nombre; así, un segundo
/// ensamblado con el mismo nombre cargado con Assembly.Load(bytes) se convierte
/// en el "canónico". Prepatcher escribe esa flag a pelo en memoria.
///
/// AVISO (mejora #4 de Rework — multi-versión): los offsets 0x74 / 0x60 / 0x10 / 0x18
/// dependen del layout de MonoAssembly y MonoImage de la build de Mono de Unity.
/// Prepatcher (1.4/1.5) y el fork jikulopo (1.6) usan los MISMOS offsets, lo que
/// sugiere que el layout no cambió entre esas versiones, pero deben verificarse
/// en la build objetivo (laboratorio Fase 1). Si no coinciden, GetRawData/SetReflectionOnly
/// fallarán o corromperán memoria: Rework debe detectarlo y degradar con modo seguro.
/// </summary>
internal static class UnsafeAssembly
{
    private static readonly FieldInfo? MonoAssemblyField =
        typeof(Assembly).Assembly.GetType("System.Reflection.RuntimeAssembly")?
            .GetField("_mono_assembly", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly List<Assembly> refOnly = new();

    /// <summary>
    /// Escribe la flag ref_only del struct MonoAssembly del ensamblado indicado.
    /// value=true → el buscador interno de Mono lo salta al resolver por nombre.
    /// </summary>
    internal static unsafe void SetReflectionOnly(Assembly asm, bool value)
    {
        // Runtime no-Mono (p.ej. .NET Framework en desarrollo): no-op silencioso.
        if (MonoAssemblyField == null)
            return;

        if (asm == null)
            throw new ArgumentNullException(nameof(asm), "SetReflectionOnly sobre ensamblado null");

        *(int*)((IntPtr)MonoAssemblyField.GetValue(asm) + 0x74) = value ? 1 : 0;
        if (value)
        {
            refOnly.Add(asm);
            // Registro en DataStore (0ReworkData, nunca se recarga): la pasada 2 corre
            // con el ReworkCore NUEVO (estáticos frescos) y necesita saber qué originales
            // quedaron refonly para saltarlos (LoadFile / GenTypes).
            DataStore.RefOnlyOriginals.Add(asm);
        }
        else
        {
            refOnly.Remove(asm);
            DataStore.RefOnlyOriginals.Remove(asm);
        }
    }

    /// <summary>
    /// Lee los bytes crudos de un ensamblado YA CARGADO desde la memoria del
    /// runtime Mono (MonoImage.raw_data). Fallback cuando no hay Location en disco.
    /// </summary>
    internal static unsafe byte[] GetRawData(Assembly asm)
    {
        if (MonoAssemblyField == null)
            throw new NotSupportedException("GetRawData solo está disponible en el runtime Mono");

        var image = *(long*)((IntPtr)MonoAssemblyField.GetValue(asm) + 0x60);
        var rawData = *(long*)((IntPtr)image + 0x10);
        var rawDataLength = *(uint*)((IntPtr)image + 0x18);

        var arr = new byte[rawDataLength];
        Marshal.Copy((IntPtr)rawData, arr, 0, (int)rawDataLength);
        return arr;
    }

    /// <summary>
    /// MODO SEGURO (mejora #5 de Rework): revierte todas las flags ref_only para
    /// que los ensamblados originales vuelvan a ser resolubles y el juego pueda
    /// continuar con ellos si el reload falló.
    /// </summary>
    internal static void UnsetRefonlys()
    {
        foreach (var asm in refOnly)
            SetReflectionOnly(asm, false);
        refOnly.Clear();
    }
}
