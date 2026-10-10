using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Rework.Core;
using Verse;

namespace Rework;

/// <summary>
/// Auto-verificación de usabilidad de contenido declarativo (§55-self-check).
///
/// El "anti-fantasma" original (ReworkDemoContentVerify) sólo comprobaba que cada
/// Def estaba en DefDatabase ("registrado = 1"). Este módulo eleva el chequeo al
/// siguiente escalón: "registrado y aparece/funciona". Comprueba que los Defs no
/// solo existen, sino que tienen sus companion defs y propiedades críticas
/// configuradas — lo que habría detectado el §41 (WorkTypeDef visible sin columna
/// en la pestaña de Trabajo) antes de que el usuario lo notara jugando.
///
/// Las comprobaciones se registran de forma perezosa y se ejecutan DESPUÉS de que
/// todos los [StaticConstructorOnStartup] hayan terminado (via
/// LongEventHandler.ExecuteWhenFinished), para garantizar que todos los registries
/// hayan completado su registro.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkContentUsability
{
    static ReworkContentUsability()
    {
        LongEventHandler.ExecuteWhenFinished(RunChecks);
    }

    private static void RunChecks()
    {
        try
        {
            // Registrar comprobaciones de usabilidad de framework
            ReworkContentSelfCheck.Register(CheckWorkTypeDefColumns);
            ReworkContentSelfCheck.Register(CheckNeedDefUsability);
            ReworkContentSelfCheck.Register(CheckGeneDefUsability);
            ReworkContentSelfCheck.Register(CheckResearchDefUsability);
            ReworkContentSelfCheck.Register(CheckWorkGiverClassAssignable);

            // Ejecutar todas las comprobaciones
            var results = ReworkContentSelfCheck.RunAll();

            int registeredOk = 0, usableOk = 0, failures = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[ReworkContent Self-Check] Resultados de verificación:");

            foreach (var r in results)
            {
                if (r.Registered) registeredOk++;
                if (r.Usable) usableOk++;
                else failures++;

                string status = r.Registered && r.Usable ? "✓" : (!r.Registered ? "✗(no registrado)" : "✗(no usable)");
                sb.AppendLine($"  [{r.Category}] {r.Name}: {status}" +
                    (r.Detail != null ? $" — {r.Detail}" : ""));
            }

            sb.AppendLine($"Total: {results.Length} checks, {registeredOk} registrados, {usableOk} usables, {failures} fallos.");

            if (failures > 0)
            {
                Lg.Error($"[ReworkContent Self-Check] {failures} contenido(s) no usable(s) detectado(s). " +
                         "El juego continuará pero algunas features pueden no funcionar. Revise los detalles arriba.");
                Lg.Error(sb.ToString());
            }
            else
            {
                Lg.Info(sb.ToString());
            }
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkContent Self-Check] Error durante la verificación: {e}");
        }
    }

    /// <summary>
    /// §41: verifica que cada WorkTypeDef visible creado por Rework tiene su
    /// PawnColumnDef acompañante en PawnTableDefOf.Work.columns.
    /// </summary>
    private static IEnumerable<ReworkContentSelfCheck.CheckResult> CheckWorkTypeDefColumns()
    {
        var workTable = PawnTableDefOf.Work;
        if (workTable?.columns == null)
        {
            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = "PawnTableDefOf.Work.columns",
                Category = "WorkTable",
                Registered = workTable != null,
                Usable = workTable?.columns != null,
                Detail = "Tabla de trabajo de la UI"
            };
            yield break;
        }

        foreach (var kvp in ReworkWorkGiverRegistry.RegisteredWorkTypes)
        {
            var wt = kvp.Value;
            if (wt == null || !wt.visible) continue;

            string expectedColumn = "WorkPriority_" + wt.defName;
            var column = DefDatabase<PawnColumnDef>.GetNamedSilentFail(expectedColumn);

            // Usable = el PawnColumnDef existe AND está en la lista de columnas de la tabla
            bool columnInTable = column != null && workTable.columns.Contains(column);

            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = $"WorkType '{wt.defName}'",
                Category = "WorkTypeDef",
                Registered = true,
                Usable = columnInTable,
                Detail = columnInTable
                    ? "columna 'WorkPriority_" + wt.defName + "' presente ✓"
                    : "FALTA PawnColumnDef 'WorkPriority_" + wt.defName + "' en la pestaña de Trabajo (§41)"
            };
        }
    }

    /// <summary>
    /// Verifica que los NeedDefs registrados tienen needClass asignado (usable).
    /// </summary>
    private static IEnumerable<ReworkContentSelfCheck.CheckResult> CheckNeedDefUsability()
    {
        foreach (var def in DefDatabase<NeedDef>.AllDefs)
        {
            // Solo chequear defs que parecen ser de Rework (prefijo típico)
            if (!def.defName.StartsWith("Rework", StringComparison.OrdinalIgnoreCase)) continue;

            bool usable = def.needClass != null;
            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = def.defName,
                Category = "NeedDef",
                Registered = true,
                Usable = usable,
                Detail = usable ? null : "needClass es null — el pawn no podrá crear la necesidad"
            };
        }
    }

    /// <summary>
    /// Verifica que los GeneDefs registrados tienen una categoría válida.
    /// </summary>
    private static IEnumerable<ReworkContentSelfCheck.CheckResult> CheckGeneDefUsability()
    {
        foreach (var def in DefDatabase<GeneDef>.AllDefs)
        {
            if (!def.defName.StartsWith("Rework", StringComparison.OrdinalIgnoreCase)) continue;

            bool usable = def.displayCategory != null || !string.IsNullOrEmpty(def.displayCategory?.defName);
            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = def.defName,
                Category = "GeneDef",
                Registered = true,
                Usable = usable,
                Detail = usable ? null : "displayCategory es null — el gen no aparecerá en el editor de xenotipos"
            };
        }
    }

    /// <summary>
    /// Verifica que los ResearchProjectDefs registrados tienen un tab válido.
    /// </summary>
    private static IEnumerable<ReworkContentSelfCheck.CheckResult> CheckResearchDefUsability()
    {
        foreach (var def in DefDatabase<ResearchProjectDef>.AllDefs)
        {
            if (!def.defName.StartsWith("Rework", StringComparison.OrdinalIgnoreCase)) continue;

            bool usable = def.tab != null;
            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = def.defName,
                Category = "ResearchProjectDef",
                Registered = true,
                Usable = usable,
                Detail = usable ? null : "tab es null — la investigación no aparecerá en el árbol"
            };
        }
    }

    /// <summary>
    /// Verifica que los WorkGiverDefs registrados tienen giverClass assignable a WorkGiver.
    /// </summary>
    private static IEnumerable<ReworkContentSelfCheck.CheckResult> CheckWorkGiverClassAssignable()
    {
        foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
        {
            if (!def.defName.StartsWith("Rework", StringComparison.OrdinalIgnoreCase)) continue;

            bool usable = def.giverClass != null && typeof(WorkGiver).IsAssignableFrom(def.giverClass);
            yield return new ReworkContentSelfCheck.CheckResult
            {
                Name = def.defName,
                Category = "WorkGiverDef",
                Registered = true,
                Usable = usable,
                Detail = usable ? null : "giverClass no hereda de WorkGiver — el colono no podrá realizar el trabajo"
            };
        }
    }
}
