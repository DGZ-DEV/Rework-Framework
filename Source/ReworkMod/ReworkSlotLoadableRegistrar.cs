using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Rework.Core;
using Verse;
using Verse.AI;

namespace Rework;

/// <summary>
/// Registrador de armas con slots ([ReworkSlotLoadable]).
///
/// Corre cuando el juego ya cargó los Defs ([StaticConstructorOnStartup]):
/// valida cada arma (def existente), parsea slots/bonus/curas/stats y crea el
/// JobDef `Rework_GatherSlotItem` (una vez) + gizmos genéricos (una vez):
/// en un arma con slots, un botón por slot (cargar lo disponible / descargar);
/// al elegir carga, el portador va a buscar el item con el job.
///
/// Formatos:
///  - Slots: "Etiqueta:Def1,Def2[:color][:stats]" por slot, separados por ';'.
///    `color` = el item tiñe el arma; `stats` = sus bonus aplican al portador.
///  - Bonuses: "Item:Damage:Cant[:Pen]" separados por ';'.
///  - Heal: "Item:defensiva|vampirica:chance:cantidad" separados por ';'.
///  - Stats: "Item:StatDef:offset" separados por ';'.
/// </summary>
[StaticConstructorOnStartup]
public static class ReworkSlotLoadableRegistrar
{
    private static bool gizmoRegistered;

    static ReworkSlotLoadableRegistrar()
    {
        EnsureJobDef();
        RegisterAll();
        RegisterGizmos();
    }

    public static void RegisterAll()
    {
        int count = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (ReworkConfig.ExcludedMods.Contains(mod.PackageIdPlayerFacing)
                || ReworkConfig.ExcludedMods.Contains(mod.Name))
                continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass) continue;
                    foreach (var attr in type.GetCustomAttributes<ReworkSlotLoadableAttribute>())
                    {
                        if (Register(attr, type)) count++;
                    }
                }
            }
        }

        Lg.Info($"ReworkSlotLoadableRegistrar: {count} arma(s) [ReworkSlotLoadable] registrada(s).");
    }

    private static bool Register(ReworkSlotLoadableAttribute attr, Type carrierType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(attr.WeaponDefName))
            {
                Lg.Error($"[ReworkSlots] '{carrierType.FullName}': WeaponDefName vacío; se ignora.");
                return false;
            }

            var weapon = DefDatabase<ThingDef>.GetNamedSilentFail(attr.WeaponDefName.Trim());
            if (weapon == null)
            {
                Lg.Error($"[ReworkSlots] '{carrierType.FullName}': arma '{attr.WeaponDefName}' no existe; NO funcionará hasta corregirlo.");
                return false;
            }

            var slots = ParseSlots(attr);
            if (slots.Count == 0)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': sin slots válidos; se ignora.");
                return false;
            }

            var w = new ReworkSlotLoadableRegistry.SlottedWeapon { WeaponDefName = weapon.defName };
            w.Slots.AddRange(slots);
            ParseBonuses(attr, w);
            ParseHeal(attr, w);
            ParseStats(attr, w);

            ReworkSlotLoadableRegistry.Register(w);
            Lg.Info($"[ReworkSlots] Arma '{weapon.defName}' registrada ({slots.Count} slot(s), {w.Bonuses.Count} bonus).");
            return true;
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSlots] Error registrando '{attr.WeaponDefName}': {e.Message}");
            return false;
        }
    }

    private static List<ReworkSlotSpec> ParseSlots(ReworkSlotLoadableAttribute attr)
    {
        var out_ = new List<ReworkSlotSpec>();
        if (string.IsNullOrWhiteSpace(attr.Slots)) return out_;
        foreach (var part in attr.Slots.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': slot '{part}' mal formado (Etiqueta:Def1,Def2...); se ignora.");
                continue;
            }
            string label = part.Substring(0, colon).Trim();
            var rest = part.Substring(colon + 1).Split(',');
            var defs = new List<string>();
            bool color = false, stats = false;
            foreach (var r in rest)
            {
                var t = r.Trim();
                if (t.Equals("color", StringComparison.OrdinalIgnoreCase)) { color = true; continue; }
                if (t.Equals("stats", StringComparison.OrdinalIgnoreCase)) { stats = true; continue; }
                if (t.Length == 0) continue;
                if (DefDatabase<ThingDef>.GetNamedSilentFail(t) == null)
                {
                    Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': sloteable '{t}' no existe; se ignora.");
                    continue;
                }
                defs.Add(t);
            }
            if (defs.Count == 0)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': slot '{label}' sin items válidos; se ignora.");
                continue;
            }
            out_.Add(new ReworkSlotSpec { Label = label, SlottableDefNames = defs, ChangesColor = color, ChangesStats = stats });
        }
        return out_;
    }

    private static void ParseBonuses(ReworkSlotLoadableAttribute attr, ReworkSlotLoadableRegistry.SlottedWeapon w)
    {
        if (string.IsNullOrWhiteSpace(attr.Bonuses)) return;
        foreach (var part in attr.Bonuses.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length < 3)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': bonus '{part}' mal formado (Item:Damage:Cant[:Pen]); se ignora.");
                continue;
            }
            string item = f[0].Trim(), dmg = f[1].Trim();
            if (DefDatabase<ThingDef>.GetNamedSilentFail(item) == null)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': bonus para item inexistente '{item}'; se ignora.");
                continue;
            }
            var dd = DefDatabase<DamageDef>.GetNamedSilentFail(dmg);
            if (dd == null)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': daño '{dmg}' no existe; se ignora.");
                continue;
            }
            if (!float.TryParse(f[2].Trim(), out float amt) || amt <= 0f)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': cantidad '{f[2]}' inválida; se ignora.");
                continue;
            }
            float pen = 0f;
            if (f.Length > 3 && !float.TryParse(f[3].Trim(), out pen)) pen = 0f;

            if (!w.Bonuses.TryGetValue(item, out var bonus))
            {
                bonus = new ReworkSlotBonusSpec { ItemDefName = item };
                w.Bonuses[item] = bonus;
            }
            bonus.DamageDefName = dmg;
            bonus.DamageAmount = amt;
            bonus.ArmorPenetration = pen;
            bonus.DamageDef = dd;
        }
    }

    private static void ParseHeal(ReworkSlotLoadableAttribute attr, ReworkSlotLoadableRegistry.SlottedWeapon w)
    {
        if (string.IsNullOrWhiteSpace(attr.Heal)) return;
        foreach (var part in attr.Heal.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length < 4)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': cura '{part}' mal formada (Item:defensiva|vampirica:chance:cantidad); se ignora.");
                continue;
            }
            string item = f[0].Trim(), kind = f[1].Trim().ToLowerInvariant();
            if (!w.Bonuses.TryGetValue(item, out var bonus))
            {
                bonus = new ReworkSlotBonusSpec { ItemDefName = item };
                w.Bonuses[item] = bonus;
            }
            if (!float.TryParse(f[2].Trim(), out float chance) || chance < 0f || chance > 1f
                || !float.TryParse(f[3].Trim(), out float amount) || amount <= 0f)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': cura '{part}' inválida; se ignora.");
                continue;
            }
            if (kind.StartsWith("vamp"))
            {
                bonus.VampiricHealChance = chance;
                bonus.VampiricHealAmount = amount;
            }
            else
            {
                bonus.DefensiveHealChance = chance;
                bonus.DefensiveHealAmount = amount;
            }
        }
    }

    private static void ParseStats(ReworkSlotLoadableAttribute attr, ReworkSlotLoadableRegistry.SlottedWeapon w)
    {
        if (string.IsNullOrWhiteSpace(attr.Stats)) return;
        foreach (var part in attr.Stats.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length < 3)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': stat '{part}' mal formado (Item:Stat:offset); se ignora.");
                continue;
            }
            string item = f[0].Trim(), stat = f[1].Trim();
            if (DefDatabase<StatDef>.GetNamedSilentFail(stat) == null)
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': stat '{stat}' no existe; se ignora.");
                continue;
            }
            if (!float.TryParse(f[2].Trim(), out float off))
            {
                Lg.Error($"[ReworkSlots] '{attr.WeaponDefName}': offset '{f[2]}' inválido; se ignora.");
                continue;
            }
            if (!w.Bonuses.TryGetValue(item, out var bonus))
            {
                bonus = new ReworkSlotBonusSpec { ItemDefName = item };
                w.Bonuses[item] = bonus;
            }
            bonus.StatOffsets.Add((stat, off));
        }
    }

    private static void EnsureJobDef()
    {
        try
        {
            const string defName = "Rework_GatherSlotItem";
            if (DefDatabase<JobDef>.GetNamedSilentFail(defName) != null) return;
            var def = new JobDef
            {
                defName = defName,
                driverClass = typeof(Rework.Core.ReworkJobDriver_GatherSlotItem),
                reportString = "Recogiendo para el slot.",
                playerInterruptible = true,
                casualInterruptible = true,
                suspendable = true,
            };
            DefDatabase<JobDef>.Add(def);
        }
        catch (Exception e)
        {
            Lg.Error($"[ReworkSlots] No se pudo crear el JobDef de recogida: {e.Message}");
        }
    }

    private static void RegisterGizmos()
    {
        if (gizmoRegistered) return;
        gizmoRegistered = true;
        var method = typeof(ReworkSlotLoadableRegistrar).GetMethod(nameof(OnGizmo),
            BindingFlags.Public | BindingFlags.Static)!;
        Func<object, bool> visible = target =>
        {
            if (!Rework.Core.ReworkSlotLoadableRuntime.HasActive) return false;
            try
            {
                Thing? thing = target as Thing;
                if (target is Pawn pawn && pawn.IsColonistPlayerControlled)
                    thing = pawn.equipment?.Primary;
                if (thing?.def == null) return false;
                return ReworkSlotLoadableRegistry.Get(thing.def.defName) != null;
            }
            catch { return false; }
        };
        ReworkGizmoRegistry.RegisterConditional("Thing", "Slots", "Gestiona los slots del arma.", method, visible);
        ReworkGizmoRegistry.RegisterConditional("Pawn", "Slots", "Gestiona los slots del arma equipada.", method, visible);
    }

    public static void OnGizmo(object target)
    {
        try
        {
            Thing? thing = target as Thing;
            if (target is Pawn pawn)
                thing = pawn.equipment?.Primary;
            if (thing?.def == null) return;
            var data = ReworkSlotLoadableRegistry.Get(thing.def.defName);
            if (data == null) return;

            var options = new List<FloatMenuOption>();
            var captured = thing;
            for (int i = 0; i < data.Slots.Count; i++)
            {
                int idx = i;
                var slot = data.Slots[i];
                string? loaded = Rework.Core.ReworkSlotLoadableRuntime.LoadedInSlot(captured, idx);
                if (loaded == null)
                {
                    // Cargar: buscar items disponibles en el mapa.
                    var avail = FindAvailable(captured, slot);
                    foreach (var item in avail)
                    {
                        var capturedItem = item;
                        options.Add(new FloatMenuOption(
                            $"Cargar {capturedItem.LabelShort} en {slot.Label}",
                            () => GiveGatherJob(captured, capturedItem, idx)));
                    }
                    if (avail.Count == 0)
                        options.Add(new FloatMenuOption($"{slot.Label}: sin items disponibles", () => { }));
                }
                else
                {
                    options.Add(new FloatMenuOption(
                        $"Descargar {loaded} de {slot.Label}",
                        () => Rework.Core.ReworkSlotLoadableRuntime.TryUnloadSlot(captured, idx)));
                }
            }
            if (options.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(options));
        }
        catch (Exception e)
        {
            Rework.Core.Lg.Error($"[ReworkSlots] Gizmo falló: {e.Message}");
        }
    }

    private static List<Thing> FindAvailable(Thing weapon, ReworkSlotSpec slot)
    {
        var out_ = new List<Thing>();
        try
        {
            Map? map = weapon.MapHeld;
            Pawn? holder = Rework.Core.ReworkSlotLoadableRuntime.HolderOf(weapon);
            if (holder != null) map = holder.MapHeld ?? map;
            if (map?.listerThings == null) return out_;
            foreach (var defName in slot.SlottableDefNames)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null) continue;
                foreach (var th in map.listerThings.ThingsOfDef(def))
                {
                    if (th == null || th.Destroyed || !th.Spawned) continue;
                    try
                    {
                        if (holder != null && ForbidUtility.IsForbidden(th, holder)) continue;
                    }
                    catch { }
                    out_.Add(th);
                    if (out_.Count >= 5) return out_;
                }
            }
        }
        catch { }
        return out_;
    }

    private static void GiveGatherJob(Thing weapon, Thing item, int slotIndex)
    {
        try
        {
            Pawn? holder = Rework.Core.ReworkSlotLoadableRuntime.HolderOf(weapon);
            if (holder == null)
            {
                Messages.Message("El arma debe estar equipada para cargar slots.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (holder.Drafted)
            {
                Messages.Message($"{holder.LabelShort} está en combate.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Rework_GatherSlotItem");
            if (jobDef == null) return;
            var job = JobMaker.MakeJob(jobDef, item);
            job.count = 1;
            // El slot se guarda en el job vía targetC celda codificada? No: el
            // driver busca el arma en el equipo al llegar. El slot viaja en `count`.
            job.count = slotIndex + 1;
            holder.jobs?.TryTakeOrderedJob(job);
        }
        catch (Exception e)
        {
            Rework.Core.Lg.Error($"[ReworkSlots] No se pudo ordenar recoger: {e.Message}");
        }
    }
}
