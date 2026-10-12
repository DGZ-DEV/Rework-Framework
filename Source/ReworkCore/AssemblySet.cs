using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using UnityEngine;

namespace Rework.Core;

/// <summary>
/// Conjunto de ensamblados a procesar, con resolución de referencias entre ellos.
/// (Equivalente a Prepatcher Source/Implementation/Process/AssemblySet.cs.)
///
/// Asunción (heredada de Prepatcher): lo usual es un ensamblado por NOMBRE simple.
/// Caso B (Fase 3): si dos mods traen un ensamblado con el MISMO nombre, el PRIMERO
/// registrado es el principal para el resolver y el duplicado se conserva en
/// AllAssemblies (se procesa y se recarga por su propia ruta). Dedup de
/// Assembly.LoadFrom (1.6) gestionado vía DataStore.AssembliesByPath (por Location).
/// </summary>
public class AssemblySet
{
    internal List<ModifiableAssembly> AllAssemblies { get; } = new();
    private readonly Dictionary<string, ModifiableAssembly> nameToAsm = new();

    public IAssemblyResolver Resolver { get; }

    public AssemblySet()
    {
        Resolver = new SetResolver(this);
    }

    public ModifiableAssembly AddAssembly(string ownerName, string friendlyName, string? path, Assembly? asm)
    {
        var masm = path != null
            ? new ModifiableAssembly(ownerName, friendlyName, path, Resolver)
            : new ModifiableAssembly(ownerName, friendlyName, asm!, Resolver);

        // Caso B (múltiples ensamblados con el mismo nombre simple): se admiten.
        // El PRIMERO registrado queda como "principal" para el resolver (p.ej.
        // Assembly-CSharp entró primero y debe seguir siendo la que se resuelve).
        // Cualquier duplicado se conserva en AllAssemblies → se procesa y se recarga
        // por su propia ruta (DataStore.AssembliesByPath clavea por Location).
        if (!nameToAsm.ContainsKey(masm.AsmDefinition.Name.Name))
            nameToAsm[masm.AsmDefinition.Name.Name] = masm;

        AllAssemblies.Add(masm);
        return masm;
    }

    public bool HasAssembly(string name) => nameToAsm.ContainsKey(name);

    public ModifiableAssembly? FindAssembly(string name) =>
        nameToAsm.TryGetValue(name, out var a) ? a : null;

    /// <summary>
    /// Mapa de dependientes (quién referencia a quién) para propagar NeedsReload:
    /// si un ensamblado se recarga, todos los que dependen de él también.
    /// </summary>
    public Dictionary<ModifiableAssembly, HashSet<ModifiableAssembly>> AllAssembliesToDependants()
    {
        var dependants = new Dictionary<ModifiableAssembly, HashSet<ModifiableAssembly>>();

        foreach (var asm in nameToAsm.Values)
        {
            foreach (var reference in asm.ModuleDefinition.AssemblyReferences)
            {
                var refAsm = FindAssembly(reference.Name);
                if (refAsm == null) continue;

                if (!dependants.TryGetValue(refAsm, out var set))
                    dependants[refAsm] = set = new HashSet<ModifiableAssembly>();
                set.Add(asm);
            }
        }

        return dependants;
    }

    /// <summary>
    /// Resuelve referencias dentro del set; si no están, busca en la carpeta Managed
    /// del juego (cubre UnityEngine* y el resto). Mismo patrón que el resolver de
    /// Prepatcher, con fallback a disco.
    /// </summary>
    private class SetResolver : IAssemblyResolver
    {
        private readonly AssemblySet owner;
        private readonly DefaultAssemblyResolver fallback = new();

        public SetResolver(AssemblySet owner)
        {
            this.owner = owner;
            var managed = Path.Combine(Application.dataPath, Util.ManagedFolderOS());
            if (Directory.Exists(managed))
                fallback.AddSearchDirectory(managed);
        }

        public AssemblyDefinition? Resolve(AssemblyNameReference name) =>
            owner.FindAssembly(name.Name)?.AsmDefinition ?? fallback.Resolve(name);

        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) =>
            Resolve(name);

        public void Dispose() => fallback.Dispose();
    }
}
