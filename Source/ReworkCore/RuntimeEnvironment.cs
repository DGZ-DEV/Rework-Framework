using System;
using System.Reflection;

namespace Rework.Core;

/// <summary>
/// Detección del entorno de runtime (bloque 1 de la lista: 1.7 / 1.8 / 1.10).
///
/// Rework reescribe Assembly-CSharp con Mono.Cecil y usa acceso nativo al runtime
/// Mono (UnsafeAssembly, offsets 0x74/0x60/0x10/0x18). Antes de tocar nada hay que
/// saber:
///   1.7 → ¿es Mono? ¿qué versión? (Mono.Runtime.GetDisplayName())
///   1.8 → ¿el layout nativo que asumimos sigue siendo válido? (verificación de
///         offsets ANTES de usarlos; si cambió la build, degradamos a modo seguro)
///   1.10→ si algo no cuadra, NO reescribimos: el juego arranca vanilla con Rework
///         inerte pero advirtiendo. (Modo seguro PROACTIVO: decide antes de romper,
///         no después de un crash.)
/// </summary>
internal static class RuntimeEnvironment
{
    internal enum RuntimeKind
    {
        Unknown,
        Mono,
        NetFramework,
        NetCore,
    }

    internal static RuntimeKind Kind { get; private set; } = RuntimeKind.Unknown;

    /// <summary>Versión de Mono reportada por Mono.Runtime.GetDisplayName() (p.ej.
    /// "Mono 6.12.0 (tarball) 5e0cb2419cc0"). null si no se pudo obtener.</summary>
    internal static string? MonoVersion { get; private set; }

    /// <summary>¿La verificación del layout nativo pasó? (null/false → no es seguro).</summary>
    internal static bool LayoutVerified { get; private set; }

    /// <summary>Detecta el tipo de runtime y, si es Mono, su versión.</summary>
    internal static void Detect()
    {
        try
        {
            var mono = Type.GetType("Mono.Runtime");
            if (mono != null)
            {
                Kind = RuntimeKind.Mono;
                var gdn = mono.GetMethod("GetDisplayName",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (gdn != null)
                    MonoVersion = gdn.Invoke(null, null)?.ToString();
            }
            else
            {
                Kind = Environment.Version.Major >= 5 ? RuntimeKind.NetCore : RuntimeKind.NetFramework;
            }
        }
        catch
        {
            Kind = RuntimeKind.Unknown;
        }
    }

    /// <summary>
    /// Verifica que el layout nativo Mono (los offsets que usa UnsafeAssembly:
    /// _mono_assembly +0x60 → MonoImage; +0x10 → raw_data; +0x18 → length) es
    /// plausible en ESTA build antes de confiar en él. Si la build de Mono cambió
    /// los offsets, la lectura devuelve punteros nulos o longitudes absurdas y
    /// esto lo detecta → modo seguro.
    /// </summary>
    internal static bool VerifyMonoLayout()
    {
        LayoutVerified = false;
        try
        {
            if (Kind != RuntimeKind.Mono)
                return false;

            var field = typeof(Assembly).Assembly
                .GetType("System.Reflection.RuntimeAssembly")?
                .GetField("_mono_assembly", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                return false;

            unsafe
            {
                var asmPtr = (IntPtr)field.GetValue(typeof(Verse.Game).Assembly);
                if (asmPtr == IntPtr.Zero)
                    return false;

                var image = *(long*)((IntPtr)asmPtr + 0x60);
                if (image == 0)
                    return false;

                var rawData = *(long*)((IntPtr)image + 0x10);
                var rawLength = *(uint*)((IntPtr)image + 0x18);

                // Sanity: puntero no nulo y longitud plausible de ensamblado .NET.
                LayoutVerified = rawData != 0 && rawLength > 0u && rawLength < 1_000_000_000u;
                return LayoutVerified;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// POLÍTICA de la pasada 1: ¿es seguro reescribir en este runtime?
    /// false → modo seguro: NO se reescribe nada (el juego arranca vanilla; Rework
    /// solo loguea la incompatibilidad).
    /// </summary>
    internal static bool IsSafeToRewrite()
    {
        Detect();
        if (Kind != RuntimeKind.Mono)
            return false;
        return VerifyMonoLayout();
    }

    /// <summary>Resumen para el log de diagnóstico (pasada 2).</summary>
    internal static string Summary()
        => $"Runtime={Kind}, Mono={MonoVersion ?? "?"}, layoutOK={LayoutVerified}";
}