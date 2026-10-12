using System;

namespace Rework.Core;

/// <summary>
/// Marcador genérico por defecto para [ReworkAnnotate] cuando no se indica un atributo
/// propio. Sirve para "etiquetar" clases/métodos/campos del juego sin definir un
/// atributo específico: se encuentra con GetCustomAttribute(typeof(ReworkMarkAttribute)).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ReworkMarkAttribute : Attribute
{
    public string? Nota { get; set; }

    public ReworkMarkAttribute()
    {
    }

    public ReworkMarkAttribute(string nota)
    {
        Nota = nota;
    }
}