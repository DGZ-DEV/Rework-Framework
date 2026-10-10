using System;
using System.IO;
using Rework.Core;
using RimWorld;
using Verse;

namespace Rework;

/// <summary>
/// GameComponent que actualiza periódicamente el ÚNICO archivo XML (.rws) de la partida
/// como Respaldo de Tiempo seguro cada X minutos (configurable por el jugador).
///
/// Principio:
/// - En cada guardado normal (autosave, quicksave, manual), el juego usa el binario (.rwbin).
/// - Este GameComponent asegura que el XML (.rws) principal se refresque silenciosamente
///   cada X minutos (ej: 15 min), sirviendo de foto de respaldo estable si el binario
///   más reciente sufriera cualquier problema.
/// - NO crea archivos duplicados: solo existe MiPartida.rwbin y MiPartida.rws.
/// </summary>
public class GameComponent_ReworkXmlBackup : GameComponent
{
    private int ticksSinceLastBackup = 0;

    // 60 ticks/s × 60 s/min × N min = ticks por intervalo
    private static int BackupIntervalTicks => Math.Max(1, ReworkMod.Settings.xmlBackupIntervalMinutes) * 60 * 60;

    public GameComponent_ReworkXmlBackup(Game game) { }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref ticksSinceLastBackup, "rework_ticksSinceLastBackup", 0);
    }

    public override void GameComponentTick()
    {
        if (!ReworkMod.Settings.enableXmlBackup) return;
        if (ReworkMod.Settings.xmlBackupIntervalMinutes <= 0) return;

        ticksSinceLastBackup++;

        if (ticksSinceLastBackup >= BackupIntervalTicks)
        {
            ticksSinceLastBackup = 0;
            TriggerBackup();
        }
    }

    /// <summary>
    /// Obtiene la ruta del archivo XML principal (.rws) para la colonia activa.
    /// </summary>
    public static string? GetPrimaryXmlPath()
    {
        try
        {
            string colonyName = Find.ActiveLanguageWorker?.ToTitleCase(
                Find.World?.info?.name ?? Current.Game?.World?.info?.name ?? "Colonia")
                ?? "Colonia";

            foreach (var c in Path.GetInvalidFileNameChars())
                colonyName = colonyName.Replace(c, '_');

            string saveDir = GenFilePaths.SavedGamesFolderPath;
            return Path.Combine(saveDir, colonyName + ".rws");
        }
        catch { return null; }
    }

    private static void TriggerBackup()
    {
        try
        {
            string? xmlPath = GetPrimaryXmlPath();
            if (xmlPath == null) return;

            // Guardado silencioso del XML principal como foto de respaldo de tiempo
            SafeSaver.Save(xmlPath, "savegame", () =>
            {
                ScribeMetaHeaderUtility.WriteMetaHeader();
                Game game = Current.Game;
                Scribe_Deep.Look(ref game, "game");
            }, false);

            Lg.Info($"[ReworkXmlBackup] XML de Respaldo de Tiempo sincronizado: '{Path.GetFileName(xmlPath)}'.");
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkXmlBackup] Error sincronizando XML de respaldo: {e.Message}");
        }
    }
}
