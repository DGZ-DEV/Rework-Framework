using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace Rework.Core;

/// <summary>
/// Generadores de Defs acompañantes para tipos de contenido que crean Defs en
/// DefDatabase en runtime (§54-companion-defs).
///
/// Reemplaza los métodos Ensure* hardcodeados por tipo (p. ej. EnsureWorkTabColumn
/// de ReworkWorkGiverRegistry) con un sistema genérico: cada tipo de contenido
/// llama a <see cref="ReworkCompanionDefRegistry.GenerateFor(def)"/> justo después
/// de añadir su Def a DefDatabase, y el framework ejecuta todos los generadores
/// registrados para ese tipo.
///
/// La inicialización es perezosa: <see cref="EnsureRegistered"/> registra todos los
/// generadores en el registry la primera vez que se llama (normalmente al inicio de
/// RegisterAll de cada registry de contenido).
/// </summary>
public static class ReworkCompanionDefGenerator
{
    private static bool registered = false;

    public static void EnsureRegistered()
    {
        if (registered) return;
        registered = true;

        // WorkTypeDef → PawnColumnDef (§41): la pestaña de Trabajo necesita una
        // columna por cada WorkTypeDef, que vanilla crea vía
        // PawnColumnDefGenerator.ImpliedPawnColumnDefs al cargar XML. Un
        // WorkTypeDef creado en runtime quedaría sin columna si no la generamos.
        ReworkCompanionDefRegistry.Register(
            typeof(WorkTypeDef),
            GenerateWorkTabColumn);

        // NeedDef: vanilla no crea Defs acompañantes para NeedDef. El pawn lo
        // descubre automáticamente vía DefDatabase<NeedDef>.AllDefs. No se registra
        // generador (documentado: no aplicable).
        //
        // GeneDef: idem — geneCategory es un string defName resuelto por referencia.
        //   No se registra generador.
        //
        // ResearchProjectDef: el árbol de investigación se reconstruye desde
        //   DefDatabase<ResearchProjectDef>.AllDefs. No se registra generador.

        Lg.Info("[ReworkCompanionDef] Generadores registrados: WorkTypeDef → PawnColumnDef.");
    }

    /// <summary>
    /// Genera la PawnColumnDef "WorkPriority_&lt;defName&gt;" para un WorkTypeDef
    /// recién creado en runtime, replicando PawnColumnDefGenerator.ImpliedPawnColumnDefs.
    /// </summary>
    private static void GenerateWorkTabColumn(object sourceDef)
    {
        var def = sourceDef as WorkTypeDef;
        if (def == null || !def.visible) return;

        try
        {
            var workTable = PawnTableDefOf.Work;
            if (workTable == null)
            {
                Lg.Error("[ReworkWorkGiver] PawnTableDefOf.Work no disponible; el tipo de trabajo no tendrá columna.");
                return;
            }

            string columnDefName = "WorkPriority_" + def.defName;

            var column = DefDatabase<PawnColumnDef>.GetNamedSilentFail(columnDefName);
            bool isNew = column == null;
            if (isNew)
            {
                column = new PawnColumnDef
                {
                    defName = columnDefName,
                    workType = def,
                    workerClass = typeof(PawnColumnWorker_WorkPriority),
                    sortable = true
                };
            }

            if (workTable.columns == null)
                workTable.columns = new List<PawnColumnDef>();

            // Vanilla alterna la etiqueta por columna de trabajo (estilo stagger).
            column.moveWorkTypeLabelDown = workTable.columns.Count(c => c != null && c.workType != null) % 2 == 1;

            if (isNew)
            {
                DefDatabase<PawnColumnDef>.Add(column);
                typeof(DefDatabase<PawnColumnDef>)
                    .GetMethod("SetIndices", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, null);
            }

            if (!workTable.columns.Contains(column))
            {
                // Vanilla inserta las columnas de trabajo justo antes de "copiar/pegar prioridades".
                int idx = workTable.columns.FindIndex(c =>
                    c != null && c.Worker is PawnColumnWorker_CopyPasteWorkPriorities);
                if (idx < 0) idx = workTable.columns.Count;
                workTable.columns.Insert(idx, column);
            }

            Lg.Info($"[ReworkWorkGiver] Columna '{columnDefName}' añadida a la pestaña de Trabajo " +
                    $"(PawnTableDefOf.Work.columns, índice {workTable.columns.IndexOf(column)}).");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkWorkGiver] No se pudo crear la columna de Trabajo para '{def.defName}': {e}");
        }
    }
}
