using System;
using System.Collections.Generic;
using Rework;
using RimWorld;
using Verse;
using Verse.AI;

namespace Rework.Core;

/// <summary>
/// Estado y lógica de slots ([ReworkSlotLoadable]).
///
/// El estado es un campo REAL inyectado en cada Thing (`Rework_SlottedLoaded`:
/// lista de "slotIndex:defName", con Serialize=true: viaja en el save sin
/// Scribe manual). Los items se consumen al cargar (split de 1) y se devuelven
/// al descargar (spawn al lado). Sin contenedores fantasma.
///
///  - `SlotsFor(weapon)`: lista de (slot, cargado?) para gizmos.
///  - `TryLoadSlot(weapon, slotIndex, item)`: valida, consume 1 y aplica
///    (color + hediff de stats al portador).
///  - `TryUnloadSlot(weapon, slotIndex)`: devuelve el item y retira efectos.
///  - `ExtraDamagesFor(weapon)`: daños extra de lo cargado (lo consume el
///    paso 18c al golpear).
///  - `OnPostHit(attacker, victim, weapon)`: curas vampírica/defensiva.
///  - `RefreshStatsHolder(weapon)`: (re)aplica el hediff de stats según lo
///    cargado; se llama al cargar/descargar/equipar.
/// </summary>
public static class ReworkSlotLoadableRuntime
{
    /// <summary>defName del HediffDef de stats (lo crea el registrador).</summary>
    public const string StatsHediffDefName = "Rework_SlottedStats";

    /// <summary>¿Hay armas con slots? (para early-out).</summary>
    public static bool HasActive => ReworkSlotLoadableRegistry.Count > 0;

    /// <summary>
    /// Campo real en cada Thing con lo cargado ("slotIndex:defName").
    /// El framework lo inyecta (como los fields de los mods) y lo serializa.
    /// Nace como lista nueva por inicializador (nunca null).
    /// </summary>
    [Rework.ReworkField(Initializer = nameof(NewLoadedList), Serialize = true)]
    public static extern ref List<string> Rework_SlottedLoaded(this Thing thing);

    /// <summary>Pública por exigencia del inyector (llamada cross-assembly).</summary>
    public static List<string> NewLoadedList() => new List<string>();

    private static List<string> LoadedFor(Thing thing)
    {
        try
        {
            var list = thing.Rework_SlottedLoaded();
            if (list == null)
            {
                list = new List<string>();
                thing.Rework_SlottedLoaded() = list;
            }
            return list;
        }
        catch
        {
            return new List<string>();
        }
    }

    public static string? LoadedInSlot(Thing weapon, int slotIndex)
    {
        var loaded = LoadedFor(weapon);
        string prefix = slotIndex + ":";
        for (int i = 0; i < loaded.Count; i++)
        {
            if (loaded[i].StartsWith(prefix, StringComparison.Ordinal))
                return loaded[i].Substring(prefix.Length);
        }
        return null;
    }

    public static bool TryLoadSlot(Thing weapon, int slotIndex, Thing item)
    {
        try
        {
            if (weapon?.def == null || item?.def == null) return false;
            var data = ReworkSlotLoadableRegistry.Get(weapon.def.defName);
            if (data == null || slotIndex < 0 || slotIndex >= data.Slots.Count) return false;
            var slot = data.Slots[slotIndex];

            bool allowed = false;
            foreach (var s in slot.SlottableDefNames)
            {
                if (string.Equals(s, item.def.defName, StringComparison.OrdinalIgnoreCase))
                { allowed = true; break; }
            }
            if (!allowed)
            {
                Messages.Message($"{item.LabelShort} no cabe en {slot.Label}.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Vaciar el slot primero (devuelve lo anterior).
            TryUnloadSlot(weapon, slotIndex);

            // Consumir 1 del stack.
            Thing taken;
            try
            {
                if (item.stackCount > 1)
                    taken = item.SplitOff(1);
                else
                {
                    taken = item;
                    try { taken.DeSpawn(); } catch { }
                }
            }
            catch (Exception e)
            {
                Lg.Error($"[ReworkSlots] No se pudo tomar {item.LabelShort}: {e.Message}");
                return false;
            }

            LoadedFor(weapon).Add(slotIndex + ":" + taken.def.defName);
            ApplySlotEffects(weapon, data, slot, taken.def.defName);
            Lg.Info($"[ReworkSlots] {taken.LabelShort} cargado en {slot.Label} ({weapon.LabelShort}).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSlots] Carga falló: {e.Message}");
            return false;
        }
    }

    public static bool TryUnloadSlot(Thing weapon, int slotIndex)
    {
        try
        {
            if (weapon?.def == null) return false;
            var data = ReworkSlotLoadableRegistry.Get(weapon.def.defName);
            if (data == null) return false;

            string? loadedDef = LoadedInSlot(weapon, slotIndex);
            if (loadedDef == null) return false;

            var loaded = LoadedFor(weapon);
            loaded.Remove(slotIndex + ":" + loadedDef);

            // Devolver el item al suelo.
            try
            {
                Map? map = weapon.MapHeld;
                IntVec3 at = weapon.PositionHeld;
                if (map != null && at.IsValid)
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(loadedDef);
                    if (def != null)
                    {
                        var thing = ThingMaker.MakeThing(def);
                        GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near);
                    }
                }
            }
            catch { }

            RefreshHolder(weapon, data);
            Lg.Info($"[ReworkSlots] Slot {slotIndex} de {weapon.LabelShort} vaciado.");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSlots] Descarga falló: {e.Message}");
            return false;
        }
    }

    private static void ApplySlotEffects(Thing weapon, ReworkSlotLoadableRegistry.SlottedWeapon data, ReworkSlotSpec slot, string itemDefName)
    {
        // Color.
        if (slot.ChangesColor)
        {
            try
            {
                var itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(itemDefName);
                if (itemDef?.graphic?.Color != null)
                {
                    // El color del item tiñe el arma (igual que JecsTools).
                    weapon.Graphic.color = itemDef.graphic.Color;
                    try { weapon.Notify_ColorChanged(); } catch { }
                }
            }
            catch { }
        }
        RefreshHolder(weapon, data);
    }

    /// <summary>(Re)aplica el hediff de stats al portador según lo cargado.</summary>
    public static void RefreshHolder(Thing weapon, ReworkSlotLoadableRegistry.SlottedWeapon data)
    {
        try
        {
            Pawn? holder = HolderOf(weapon);
            if (holder?.health == null) return;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(StatsHediffDefName);
            if (hediffDef == null) return;

            // Quitar el anterior y recalcular (simple y sin estados colgados).
            var existing = holder.health.hediffSet?.GetFirstHediffOfDef(hediffDef, false);
            if (existing != null)
            {
                try { holder.health.RemoveHediff(existing); } catch { }
            }

            var offsets = CollectStatOffsets(weapon, data);
            if (offsets.Count == 0) return;

            ApplyBonusHediffs(holder, offsets);
        }
        catch { }
    }

    private static Dictionary<string, float> CollectStatOffsets(Thing weapon, ReworkSlotLoadableRegistry.SlottedWeapon data)
    {
        var out_ = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var loaded = LoadedFor(weapon);
        foreach (var entry in loaded)
        {
            int sep = entry.IndexOf(':');
            if (sep < 0) continue;
            string itemDef = entry.Substring(sep + 1);
            if (data.Bonuses.TryGetValue(itemDef, out var bonus))
            {
                foreach (var (stat, off) in bonus.StatOffsets)
                {
                    out_.TryGetValue(stat, out float cur);
                    out_[stat] = cur + off;
                }
            }
        }
        return out_;
    }

    private static void ApplyBonusHediffs(Pawn holder, Dictionary<string, float> offsets)
    {
        // Por cada stat con offset: HediffDef generado "Rework_Slotted_<Stat>"
        // con un stage con statOffsets. Se cachea por stat (uno por stat).
        foreach (var kvp in offsets)
        {
            try
            {
                var hediffDef = ReworkSlotBonusHediffs.Ensure(kvp.Key, kvp.Value);
                if (hediffDef == null) continue;
                var hediff = HediffMaker.MakeHediff(hediffDef, holder);
                holder.health.AddHediff(hediff);
            }
            catch { }
        }
    }

    public static Pawn? HolderOf(Thing weapon)
    {
        try
        {
            if (weapon?.ParentHolder is Pawn_EquipmentTracker eq)
                return eq.pawn;
            if (weapon?.ParentHolder is Pawn_ApparelTracker ap)
                return ap.pawn;
        }
        catch { }
        return null;
    }

    /// <summary>Daños extra de lo cargado (para el paso 18c al golpear).</summary>
    public static List<DamageInfo> ExtraDamagesFor(Thing weapon, Pawn attacker)
    {
        var out_ = new List<DamageInfo>();
        try
        {
            if (weapon?.def == null) return out_;
            var data = ReworkSlotLoadableRegistry.Get(weapon.def.defName);
            if (data == null) return out_;
            var loaded = LoadedFor(weapon);
            foreach (var entry in loaded)
            {
                int sep = entry.IndexOf(':');
                if (sep < 0) continue;
                string itemDef = entry.Substring(sep + 1);
                if (!data.Bonuses.TryGetValue(itemDef, out var bonus)) continue;
                if (!(bonus.DamageDef is DamageDef dd) || bonus.DamageAmount <= 0f) continue;
                out_.Add(new DamageInfo(dd, bonus.DamageAmount, bonus.ArmorPenetration, -1f, attacker));
            }
        }
        catch { }
        return out_;
    }

    /// <summary>Curas al golpear (para la cola AfterHit): vampírica al atacante, defensiva al azar.</summary>
    public static void OnPostHit(Pawn attacker, Thing weapon)
    {
        try
        {
            if (attacker?.health == null || weapon?.def == null) return;
            var data = ReworkSlotLoadableRegistry.Get(weapon.def.defName);
            if (data == null) return;
            var loaded = LoadedFor(weapon);
            foreach (var entry in loaded)
            {
                int sep = entry.IndexOf(':');
                if (sep < 0) continue;
                string itemDef = entry.Substring(sep + 1);
                if (!data.Bonuses.TryGetValue(itemDef, out var bonus)) continue;

                if (bonus.VampiricHealChance > 0f && bonus.VampiricHealAmount > 0f)
                {
                    try
                    {
                        if (Rand.Chance(bonus.VampiricHealChance))
                            HealRandomWound(attacker, bonus.VampiricHealAmount);
                    }
                    catch { }
                }
                if (bonus.DefensiveHealChance > 0f && bonus.DefensiveHealAmount > 0f)
                {
                    try
                    {
                        if (Rand.Chance(bonus.DefensiveHealChance))
                            HealRandomWound(attacker, bonus.DefensiveHealAmount);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void HealRandomWound(Pawn pawn, float amount)
    {
        try
        {
            var injuries = new List<Hediff_Injury>();
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury inj && inj.CanHealNaturally() && !inj.IsPermanent())
                    injuries.Add(inj);
            }
            if (injuries.Count == 0) return;
            var pick = injuries[Rand.Range(0, injuries.Count)];
            pick.Heal(amount);
        }
        catch { }
    }
}

/// <summary>
/// Hediffs de bonus por stat: un HediffDef por stat ("Rework_Slotted_<Stat>")
/// con stage de statOffsets. Se generan bajo demanda y se cachean.
/// </summary>
public static class ReworkSlotBonusHediffs
{
    private static readonly Dictionary<string, HediffDef> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static HediffDef? Ensure(string statName, float offset)
    {
        try
        {
            string key = statName + ":" + offset;
            if (cache.TryGetValue(key, out var existing))
                return existing;

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(statName);
            if (stat == null) return null;

            var def = new HediffDef
            {
                defName = "Rework_Slotted_" + statName + "_" + offset.ToString("0.##"),
                label = "bono de slot",
                description = "Bonus de lo cargado en el arma.",
                initialSeverity = 1f,
                maxSeverity = 1f,
            };
            def.stages = new List<HediffStage>
            {
                new HediffStage
                {
                    minSeverity = 0f,
                    statOffsets = new List<StatModifier> { new StatModifier { stat = stat, value = offset } },
                },
            };
            DefDatabase<HediffDef>.Add(def);
            cache[key] = def;
            return def;
        }
        catch { return null; }
    }
}
