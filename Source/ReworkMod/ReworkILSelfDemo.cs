using System;
using System.Reflection;
using System.Threading;
using RimWorld;
using Rework.Core;
using Rework.Data;
using Verse;

namespace Rework;

/// <summary>
/// BLOQUE 24 — AUTO-DEMO Y VERIFICACIÓN INTERNA de la cirugía IL declarativa.
///
/// El framework CONSUME sus propios atributos (regla anti-fantasma §44: ninguna
/// feature declarativa sin consumidor real). Los cinco efectos están elegidos
/// para ser verificados en el log y en el Inspector sin ALTERAR NINGÚN
/// comportamiento del juego:
///
///   [ReworkRedirect] Verse.Log::Message(string) → contador de mensajes. El
///      call-through al original conserva el log idéntico (solo se cuenta).
///   [ReworkOverride] Verse.Building gana un override REAL de
///      ThingWithComps.GetInspectString; con InspectSuffix == null devuelve el
///      resultado del base TAL CUAL (despacho virtual real, salida idéntica).
///   [ReworkUnlock]   Lord.curJob (private → public) y CompActivity
///      (sealed → heredable): puro cambio de metadatos.
///   [ReworkInline]   Pawn.Name y MapDrawLayer.Map: getters calientes
///      (189/113 call-sites) inlineados a acceso directo de campo. Semántica
///      idéntica, una llamada menos por uso.
///   [ReworkConst]    JobDriver_AcceptRole.FacingUpdateInterval plegado a SU
///      MISMO valor (20): demo del plegado con impacto de juego exactamente 0.
///
/// La verificación de la pasada 2 corre en ReworkILVerifier (mismo archivo).
/// </summary>
[ReworkUnlock(Type = "Verse.AI.Group.Lord", Member = "curJob")]
[ReworkUnlock(Type = "RimWorld.CompActivity", Unseal = true, Public = false)]
[ReworkInline(Type = "Verse.Pawn", Method = "get_Name")]
[ReworkInline(Type = "Verse.MapDrawLayer", Method = "get_Map")]
[ReworkConst(Type = "RimWorld.JobDriver_AcceptRole", Field = "FacingUpdateInterval", Value = 20)]
public static class ReworkILSelfDemo
{
    /// <summary>Mensajes de Log.Message(string) que han pasado por el redirector
    /// (pasada 2). Visible en la verificación y en el Inspector.</summary>
    public static int InterceptedMessages;

    /// <summary>Sufijo opcional del override de inspección de edificios.
    /// null (default) → el override devuelve el resultado del base TAL CUAL
    /// (comportamiento idéntico). Ponle texto para VER el override en partida:
    /// p. ej. ReworkILSelfDemo.InspectSuffix = "(inspección Reforjed)";</summary>
    public static string? InspectSuffix;

    /// <summary>
    /// [ReworkRedirect] — TODOS los Log.Message(string) del juego y de los mods
    /// reescribibles entran por aquí (el IL final llama directo: coste cero).
    /// Regla anti-recursión: la llamada de abajo apunta al ORIGINAL y Rework NO
    /// la reescribe (estamos dentro del redirector). NUNCA usar Lg aquí: sus
    /// call-sites también están redirigidos y entraríamos en bucle.
    /// </summary>
    [ReworkRedirect(Type = "Verse.Log", Method = "Message")]
    public static void InterceptLogMessage(string text)
    {
        Interlocked.Increment(ref InterceptedMessages);
        Log.Message(text); // ← call-through al original (no se reescribe)
    }

    /// <summary>
    /// [ReworkOverride] — Verse.Building gana un override real del virtual
    /// ThingWithComps.GetInspectString (newslot/reuseslot: despacho virtual
    /// normal, sin trampolines). Con InspectSuffix == null la salida es la del
    /// base exacta: la maquinaria se ejercita en cada inspección de un edificio
    /// sin cambiar nada visible.
    /// </summary>
    [ReworkOverride(Type = "Verse.Building", CallBase = true)]
    public static string Rework_GetInspectString(Building b, string baseResult)
    {
        return InspectSuffix == null ? baseResult : baseResult + "\n" + InspectSuffix;
    }
}

/// <summary>
/// Verificación de la pasada 2 para el bloque 24: comprueba por REFLEXIÓN (sobre
/// el Assembly-CSharp REESCRITO) que los cinco efectos de ReworkILSelfDemo están
/// presentes, y vuelca al log la telemetría de la pasada 1 (que cruzó la barrera
/// del reload en DataStore.AppliedPatches — el Inspector también la muestra).
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkILVerifier
{
    static ReworkILVerifier()
    {
        try
        {
            Verify();
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkIL] verificación falló con excepción: {e}");
        }
    }

    private static void Verify()
    {
        int ok = 0, fail = 0;

        void Report(string nombre, bool pasado, string detalle)
        {
            if (pasado)
            {
                ok++;
                Lg.Info($"[ReworkIL] {nombre}: ✓ — {detalle}");
            }
            else
            {
                fail++;
                Lg.Error($"[ReworkIL] {nombre}: ✗ FALLO — {detalle}");
            }
        }

        // ---------- 1) ReworkRedirect: el delta del contador al llamar a Log.Message.
        // Esta llamada pasa por el redirector (su call-site fue reescrito en la
        // pasada 1) → el contador debe subir exactamente en 1.
        int antes = ReworkILSelfDemo.InterceptedMessages;
        Log.Message("[ReworkIL] verificando el redirect de Log.Message...");
        int despues = ReworkILSelfDemo.InterceptedMessages;
        Report("ReworkRedirect Verse.Log::Message(string)",
               despues == antes + 1,
               $"delta={despues - antes} (esperado 1); total interceptado={despues}");

        // ---------- 2) ReworkOverride: Building ahora DECLARA el override.
        // DeclaredOnly: sin él, GetMethod devolvería el método HEREDADO de
        // ThingWithComps y la comprobación daría un falso positivo.
        var mis = typeof(Building).GetMethod("GetInspectString",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Report("ReworkOverride Verse.Building::GetInspectString",
               mis != null && mis.IsVirtual && mis.ReturnType == typeof(string),
               mis == null
                   ? "Building no declara GetInspectString (el override no se inyectó)"
                   : $"virtual={mis.IsVirtual}, retorno={mis.ReturnType.Name}");

        // ---------- 3) ReworkUnlock: metadatos reales del ensamblado reescrito.
        var curJob = typeof(Verse.AI.Group.Lord).GetField("curJob",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Report("ReworkUnlock Verse.AI.Group.Lord::curJob",
               curJob != null && curJob.IsPublic,
               curJob == null ? "campo no hallado" : $"IsPublic={curJob.IsPublic}");

        Report("ReworkUnlock RimWorld.CompActivity heredable",
               !typeof(CompActivity).IsSealed,
               $"IsSealed={typeof(CompActivity).IsSealed}");

        // ---------- 4/5) ReworkInline y ReworkConst: la reescritura de call-sites
        // no es visible por reflexión (el getter SIGUE existiendo); la evidencia
        // es la telemetría de la pasada 1, que cruzó la barrera del reload.
        foreach (var linea in DataStore.AppliedPatches)
        {
            if (linea.StartsWith("[ReworkInline]", StringComparison.Ordinal)
                || linea.StartsWith("[ReworkConst]", StringComparison.Ordinal)
                || linea.StartsWith("[ReworkRedirect]", StringComparison.Ordinal)
                || linea.StartsWith("[ReworkOverride]", StringComparison.Ordinal)
                || linea.StartsWith("[ReworkUnlock]", StringComparison.Ordinal))
                Lg.Info($"[ReworkIL] pasada 1: {linea}");
        }

        bool inlineOk = DataStore.AppliedPatches.Exists(l =>
            l.StartsWith("[ReworkInline]", StringComparison.Ordinal) && l.Contains("call-site"));
        bool constOk = DataStore.AppliedPatches.Exists(l =>
            l.StartsWith("[ReworkConst]", StringComparison.Ordinal) && l.Contains("lectura"));
        Report("ReworkInline (telemetría pasada 1)", inlineOk,
               inlineOk ? "getters inlineados con call-sites reescritos"
                        : "sin registro de inline en DataStore.AppliedPatches");
        Report("ReworkConst (telemetría pasada 1)", constOk,
               constOk ? "lecturas ldsfld plegadas a literales"
                       : "sin registro de plegado en DataStore.AppliedPatches");

        if (fail == 0)
            Lg.Info($"[ReworkIL] verificación: {ok} OK / {fail} fallos — cirugía IL (bloque 24) VERIFICADA ✓");
        else
            Lg.Error($"[ReworkIL] verificación: {ok} OK / {fail} FALLOS — repasar Rework pasada1.log y ERRORES.md.");
    }
}
