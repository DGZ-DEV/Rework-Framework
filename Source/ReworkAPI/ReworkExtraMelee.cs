using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Un daño extra de la lista de [ReworkExtraMelee]: tipo, cantidad,
/// penetración y probabilidad por golpe.
/// </summary>
public class ReworkExtraDamageEntry
{
    public string DamageDefName = "";
    public float Amount;
    public float ArmorPenetration;
    public float Chance = 1f;
    public object? DamageDef;
}

/// <summary>
/// Declara daños extra de melee en la clase Hediff que lleva el atributo:
/// cada golpe cuerpo a cuerpo del portador añade estos daños al impacto,
/// igual que los `extraMeleeDamages` del arma (`Tool`), pero viniendo de un
/// hediff (garras venenosas, puños en llamas, colmillos vampíricos).
///
/// Equivale al HediffComp_ExtraMeleeDamages de JecsTools, sin XML ni comps.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ReworkExtraMeleeAttribute : Attribute
{
    /// <summary>Nombre único del HediffDef (defName) que añade daños al golpear.</summary>
    public string DefName { get; }

    /// <summary>
    /// Daños extra como CSV "DefName:Cantidad[:Penetración][:Chance]".
    /// Ej: "Toxic:3:0.2:1,Burn:2" (tóxico 3 con 0.2 pen + quemadura 2).
    /// </summary>
    public string ExtraDamages { get; set; } = "";

    /// <summary>Nombre visible en la pestaña de salud del colono.</summary>
    public string? Label { get; set; }

    /// <summary>Descripción de los daños extra.</summary>
    public string? Description { get; set; }

    public ReworkExtraMeleeAttribute(string defName)
    {
        DefName = defName;
    }
}

/// <summary>
/// Registro de daños extra ([ReworkExtraMelee]).
/// El registrador parsea el CSV y resuelve los DamageDefs una vez.
/// </summary>
public static class ReworkExtraMeleeRegistry
{
    public class ExtraMeleeData
    {
        public string DefName = "";
        public Type HandlerType = null!;
        public List<ReworkExtraDamageEntry> Damages = new();
        public string Label = "";
        public string Description = "";
    }

    private static readonly Dictionary<string, ExtraMeleeData> entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registra los datos de un extra-melee (lo llama el registrador del framework).
    /// </summary>
    public static void Register(ExtraMeleeData data)
    {
        if (data == null || string.IsNullOrEmpty(data.DefName)) return;
        entries[data.DefName] = data;
    }

    /// <summary>Datos por defName del Hediff, o null si no añade daños.</summary>
    public static ExtraMeleeData? Get(string defName)
    {
        entries.TryGetValue(defName, out var data);
        return data;
    }

    /// <summary>Número de extra-melees registrados (para early-out en el golpe).</summary>
    public static int Count => entries.Count;
}
