using System;
using System.Collections.Generic;

namespace Rework;

/// <summary>
/// Representa una habilidad o especialidad a nivel colonia (Combate, Ciencia, Construcción, etc.).
/// </summary>
public class ColonySkill
{
    public string Name { get; set; }
    public int Level { get; set; } = 1;
    public float Xp { get; set; } = 0f;

    public ColonySkill(string name, int level = 1, float xp = 0f)
    {
        Name = name;
        Level = level;
        Xp = xp;
    }

    /// <summary>
    /// XP requerida para alcanzar el siguiente nivel.
    /// </summary>
    public float XpForNextLevel => Level * 1000f;

    /// <summary>
    /// Añade experiencia y procesa subidas de nivel si corresponde.
    /// </summary>
    public bool AddXp(float amount)
    {
        Xp += amount;
        bool leveledUp = false;
        while (Xp >= XpForNextLevel)
        {
            Xp -= XpForNextLevel;
            Level++;
            leveledUp = true;
        }
        return leveledUp;
    }
}

/// <summary>
/// Sistema de Progresión de Colonia de Rework.
/// Permite acumular XP a nivel global de asentamiento, desbloquear niveles y guardar el estado
/// de forma transparente tanto en el formato binario (.rwbin) como en XML.
/// </summary>
public static class ReworkColonySkill
{
    private static readonly Dictionary<string, ColonySkill> skills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Evento disparado cuando una habilidad de colonia sube de nivel.
    /// Parámetros: nombreHabilidad, nuevoNivel.
    /// </summary>
    public static event Action<string, int>? OnLevelUp;

    static ReworkColonySkill()
    {
        // Habilidades base por defecto
        RegisterSkill("Combate");
        RegisterSkill("Ciencia");
        RegisterSkill("Construccion");
        RegisterSkill("Supervivencia");
    }

    public static void RegisterSkill(string name)
    {
        if (!skills.ContainsKey(name))
        {
            skills[name] = new ColonySkill(name);
        }
    }

    public static ColonySkill? GetSkill(string name)
    {
        skills.TryGetValue(name, out var skill);
        return skill;
    }

    public static IReadOnlyDictionary<string, ColonySkill> AllSkills => skills;

    /// <summary>
    /// Añade XP a una habilidad global de colonia.
    /// </summary>
    public static void AddXp(string name, float amount)
    {
        if (skills.TryGetValue(name, out var skill))
        {
            if (skill.AddXp(amount))
            {
                OnLevelUp?.Invoke(skill.Name, skill.Level);
            }
        }
    }

    /// <summary>
    /// Serializa los datos de las habilidades de la colonia a bytes binarios.
    /// </summary>
    public static byte[] SerializeToBinary()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(skills.Count);
        foreach (var kvp in skills)
        {
            bw.Write(kvp.Key);
            bw.Write(kvp.Value.Level);
            bw.Write(kvp.Value.Xp);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializa las habilidades de la colonia desde bytes binarios.
    /// </summary>
    public static void DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string name = br.ReadString();
                int level = br.ReadInt32();
                float xp = br.ReadSingle();
                skills[name] = new ColonySkill(name, level, xp);
            }
        }
        catch
        {
            // Fallback seguro si los datos binarios estuvieran dañados
        }
    }
}
