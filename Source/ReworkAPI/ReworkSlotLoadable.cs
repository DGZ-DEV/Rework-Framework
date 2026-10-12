using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Un slot del arma: nombre, qué acepta y qué cambia al cargar.
/// </summary>
public class ReworkSlotSpec
{
    public string Label = "";
    public List<string> SlottableDefNames = new();
    public bool ChangesColor;
    public bool ChangesStats;
}

/// <summary>
/// Bonus de lo cargado en un slot: daño extra al golpear, stats al portador
/// (vía hediff oculto), color del arma y curas.
/// </summary>
public class ReworkSlotBonusSpec
{
    public string ItemDefName = "";
    public string? DamageDefName;
    public float DamageAmount;
    public float ArmorPenetration;
    public List<(string statName, float offset)> StatOffsets = new();
    public float DefensiveHealChance;
    public float DefensiveHealAmount = 1f;
    public float VampiricHealChance;
    public float VampiricHealAmount = 1f;
    public object? DamageDef;
}

/// <summary>
/// Declara slots en el arma indicada: el jugador carga items (munición,
/// cristales...) y el arma cambia (color, stats del portador, daño extra al
/// golpear, curas). Sin XML ni comps.
///
/// Equivale al CompSlotLoadable de JecsTools, sin el contenedor físico: el
/// estado es una lista persistida en el arma (qué hay cargado) y los items se
/// consumen al cargar / se devuelven al descargar.
///
/// <code>
/// [ReworkSlotLoadable("MiMod_FusilPeso",
///     Slots = "Munición:BulletLight,BulletAP,BulletHE",
///     Bonuses = "BulletAP:Cut:2:0.3;BulletHE:Burn:4:0")]
/// public static class FusilPeso { }
/// </code>
/// Formato Slots: "Etiqueta:Def1,Def2[:cambiaColor][:cambiaStats]" por slot,
/// separados por ';'. Formato Bonuses: "ItemDef:DamageDef:Cantidad[:Pen]"
/// separados por ';'. Cura: `Heal = "ItemDef:defensiva:chance:cantidad"` o
/// `"ItemDef:vampirica:chance:cantidad"`.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ReworkSlotLoadableAttribute : Attribute
{
    /// <summary>defName del ThingDef del arma con slots (debe existir).</summary>
    public string WeaponDefName { get; }

    /// <summary>Slots: "Etiqueta:Def1,Def2[:color][:stats]" separados por ';'.</summary>
    public string Slots { get; set; } = "";

    /// <summary>Bonus de daño: "Item:Damage:Cant[:Pen]" separados por ';'.</summary>
    public string? Bonuses { get; set; }

    /// <summary>Curas: "Item:defensiva|vampirica:chance:cantidad" separados por ';'.</summary>
    public string? Heal { get; set; }

    /// <summary>Stats: "Item:StatDef:offset" separados por ';' (al portador).</summary>
    public string? Stats { get; set; }

    public ReworkSlotLoadableAttribute(string weaponDefName)
    {
        WeaponDefName = weaponDefName;
    }
}

/// <summary>
/// Registro de armas con slots ([ReworkSlotLoadable]).
/// </summary>
public static class ReworkSlotLoadableRegistry
{
    public class SlottedWeapon
    {
        public string WeaponDefName = "";
        public List<ReworkSlotSpec> Slots = new();
        public Dictionary<string, ReworkSlotBonusSpec> Bonuses =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, SlottedWeapon> weapons =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra un arma con slots (lo llama el registrador del framework).
    /// </summary>
    public static void Register(SlottedWeapon w)
    {
        if (w == null || string.IsNullOrEmpty(w.WeaponDefName)) return;
        weapons[w.WeaponDefName] = w;
    }

    /// <summary>Datos por defName del arma, o null si no tiene slots.</summary>
    public static SlottedWeapon? Get(string weaponDefName)
    {
        weapons.TryGetValue(weaponDefName, out var w);
        return w;
    }

    /// <summary>Número de armas con slots (para early-out).</summary>
    public static int Count => weapons.Count;
}
