using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Registro genérico de generadores de Defs acompañantes (§54-companion-defs).
///
/// Un "companion def" es un DefImplícito que vanilla crea durante la carga de XML
/// (vía PawnColumnDefGenerator, DefGenerator, etc.) pero que NO se crea cuando un
/// Def se añade a DefDatabase en runtime. Este registry permite a cada capa del
/// framework registrar un generador para un tipo de Def origen, de modo que al
/// crear un Def en runtime el sistema genere automáticamente sus companion defs.
///
/// Patrón de uso:
///   1. En startup: ReworkCompanionDefRegistry.Register(typeof(WorkTypeDef), GenerateWorkTabColumn)
///   2. Al crear un Def en runtime: ReworkCompanionDefRegistry.GenerateFor(workTypeDef)
///
/// El registry es genérico y Def-agnostic (usa object en la API pública) para que
/// viva en 0ReworkAPI sin referencias al juego. Los generadores concretos (que
/// necesitan tipos de RimWorld) viven en ReworkCore/ReworkMod y se registran
/// perezosamente vía ReworkCompanionDefGenerator.EnsureRegistered().
/// </summary>
public static class ReworkCompanionDefRegistry
{
    /// <summary>Delegado que genera los companion defs para un Def origen.</summary>
    public delegate void CompanionGeneratorDelegate(object sourceDef);

    private class Entry
    {
        public Type SourceDefType = null!;
        public CompanionGeneratorDelegate Generator = null!;
    }

    private static readonly List<Entry> entries = new();

    /// <summary>Registra un generador de defs acompañantes para un tipo de Def origen.</summary>
    public static void Register(Type sourceDefType, CompanionGeneratorDelegate generator)
    {
        if (sourceDefType == null || generator == null) return;
        entries.Add(new Entry { SourceDefType = sourceDefType, Generator = generator });
    }

    /// <summary>Número total de generadores registrados.</summary>
    public static int Count => entries.Count;

    /// <summary>
    /// Ejecuta todos los generadores cuyo tipo origen sea compatible con sourceDef.
    /// Se llama después de añadir un Def a DefDatabase en runtime para generar
    /// los Defs acompañantes que vanilla normalmente crea al cargar XML.
    /// </summary>
    public static void GenerateFor(object sourceDef)
    {
        if (sourceDef == null || entries.Count == 0) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.SourceDefType.IsInstanceOfType(sourceDef))
            {
                try { e.Generator(sourceDef); }
                catch { }
            }
        }
    }
}
