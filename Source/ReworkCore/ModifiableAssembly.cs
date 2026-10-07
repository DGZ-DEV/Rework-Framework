using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;

namespace Rework.Core;

/// <summary>
/// Envoltorio de un ensamblado cargado + su vista Mono.Cecil.
/// (Diseño equivalente a Prepatcher Source/Implementation/Process/ModifiableAssembly.cs.)
/// </summary>
public class ModifiableAssembly
{
    public string OwnerName { get; }
    public string FriendlyName { get; }

    public Assembly? SourceAssembly { get; }
    public AssemblyDefinition AsmDefinition { get; }
    public ModuleDefinition ModuleDefinition => AsmDefinition.MainModule;

    /// <summary>true → este ensamblado aporta atributos (campos [ReworkField], etc.).</summary>
    public bool ProcessAttributes { get; set; }

    /// <summary>true → puede ser reescrito por parches libres (se usa en Fase 3).</summary>
    public bool AllowPatches { get; set; } = true;

    public bool Modified { get; set; }

    /// <summary>true si hay que recargarlo (modificado o porque algo que él usa se recarga).</summary>
    public bool NeedsReload => needsReload || Modified;

    public byte[]? Bytes { get; private set; }

    private readonly byte[]? rawBytes;
    private bool needsReload;

    /// <summary>Desde ruta de disco (ensamblados de sistema y ensamblados con Location).</summary>
    public ModifiableAssembly(string ownerName, string friendlyName, string path, IAssemblyResolver resolver)
    {
        OwnerName = ownerName;
        FriendlyName = friendlyName;
        rawBytes = File.ReadAllBytes(path);
        AsmDefinition = AssemblyDefinition.ReadAssembly(
            new MemoryStream(rawBytes),
            new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
    }

    /// <summary>Desde un ensamblado ya cargado (prefiere disco; fallback a la memoria de Mono).</summary>
    public ModifiableAssembly(string ownerName, string friendlyName, Assembly sourceAssembly, IAssemblyResolver resolver)
    {
        OwnerName = ownerName;
        FriendlyName = friendlyName;
        SourceAssembly = sourceAssembly;

        if (!string.IsNullOrEmpty(sourceAssembly.Location) && File.Exists(sourceAssembly.Location))
        {
            // Lección del fork jikulopo (Process/ModifiableAssembly.cs): en 1.6 el
            // ensamblado cargado puede no corresponder byte a byte con su archivo
            // (Assembly.LoadFrom deduplica por identidad); leer del disco es lo fiable.
            rawBytes = File.ReadAllBytes(sourceAssembly.Location);
        }
        else
        {
            // Fallback: bytes crudos desde la memoria del runtime Mono
            // (UnsafeAssembly.GetRawData → MonoImage.raw_data).
            rawBytes = UnsafeAssembly.GetRawData(sourceAssembly);
        }

        AsmDefinition = AssemblyDefinition.ReadAssembly(
            new MemoryStream(rawBytes),
            new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
    }

    /// <summary>
    /// Serializa a byte[] EN MEMORIA (nunca a disco, salvo debug explícito).
    /// Si no hubo modificaciones, reutiliza los bytes originales.
    /// </summary>
    public void SerializeToByteArray()
    {
        if (rawBytes != null && !Modified)
        {
            Bytes = rawBytes;
            return;
        }

        var stream = new MemoryStream();
        AsmDefinition.Write(stream);
        Bytes = stream.ToArray();
    }

    /// <summary>Marca el ensamblado original como reflection-only (para permitir el duplicado).</summary>
    public void SetSourceRefOnly()
    {
        if (SourceAssembly == null)
            throw new InvalidOperationException($"Sin SourceAssembly para {FriendlyName}");

        // GUARD del motor (bloque 1, 1.2/1.3 — "contexto seguro"): los ensamblados de
        // Unity (UnityEngine.dll, UnityEngine.CoreModule.dll, etc.) NUNCA se marcan
        // refonly ni se recargan: el runtime los gestiona y corromper su flag rompería
        // el motor entero. Si algo intenta, lo rechazamos con log (nunca tocar).
        if (IsUnityEngineAssembly(SourceAssembly))
        {
            Lg.Error($"Guard: {FriendlyName} es del motor (Unity). No se marcará refonly " +
                     "(contexto seguro = solo lectura de referencias, nunca recarga).");
            return;
        }

        UnsafeAssembly.SetReflectionOnly(SourceAssembly, true);
    }

    /// <summary>¿Es un ensamblado del motor de Unity o biblioteca de runtime protegida (nunca se reescribe/recarga)?</summary>
    internal static bool IsUnityEngineAssembly(Assembly asm)
    {
        var n = asm.GetName().Name;
        return n.StartsWith("UnityEngine", StringComparison.Ordinal)
            || n == "Unity" || n == "Mono.CSharp" || n == "mscorlib"
            || n == "System" || n.StartsWith("System.", StringComparison.Ordinal)
            || n.StartsWith("MonoBleedingEdge", StringComparison.Ordinal)
            || n.StartsWith("0Harmony", StringComparison.Ordinal)
            || n.StartsWith("HarmonySharedState", StringComparison.Ordinal);
    }

    public void SetNeedsReload() => needsReload = true;

    public override string ToString() => FriendlyName;
}
