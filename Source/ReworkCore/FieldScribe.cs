using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Verse;

namespace Rework.Core;

/// <summary>
/// Serialización AUTOMÁTICA de campos inyectados ([ReworkField(Serialize = true)]).
///
/// Cuando un accessor pide Serialize, el campo se engancha al ExposeData del tipo
/// destino (p.ej. Verse.Pawn.ExposeData) con la llamada Scribe correspondiente:
///   - primitivas (int, float, double, long, bool...), string y enums
///     → Scribe_Values.Look(ref campo, "Nombre", default, forceSave=true).
///   - List&lt;primitiva/string/enum&gt; → Scribe_Collections.Look(LookMode.Value).
///
/// Así un mod consigue un campo que viaja en la partida guardada SIN escribir el
/// parche Scribe a mano (con lo que antes había que hacer un transpiler de ExposeData).
/// Solo el mod que CREA el campo decide la serialización (los que se enlazan a un
/// campo compartido no la reafirman).
///
/// Se inyecta al INICIO de ExposeData (antes de cualquier región try/catch), igual que
/// los inicializadores de campo: escribir antes de las regiones EH es IL válido.
/// </summary>
internal static class FieldScribe
{
    /// <summary>Petición de serialización para un campo inyectado.</summary>
    internal sealed class Req
    {
        internal readonly TypeDefinition TargetType;
        internal readonly FieldReference Field;
        internal readonly string Label;
        internal readonly object? DefaultValue;

        internal Req(TypeDefinition targetType, FieldReference field, string label, object? defaultValue)
        {
            TargetType = targetType;
            Field = field;
            Label = label;
            DefaultValue = defaultValue;
        }
    }

    private static readonly Dictionary<TypeDefinition, List<Req>> pending = new();

    internal static void Request(Req r)
    {
        if (!pending.TryGetValue(r.TargetType, out var list))
            pending[r.TargetType] = list = new List<Req>();
        list.Add(r);
    }

    /// <summary>Inyecta todas las peticiones pendientes al final del procesamiento.</summary>
    internal static void FlushAll()
    {
        if (!ReworkConfig.AutoScribeEnabled)
        {
            Lg.Info("FieldScribe: serialización automática desactivada por configuración.");
            pending.Clear();
            return;
        }

        foreach (var kvp in pending)
            Inject(kvp.Key, kvp.Value);
        pending.Clear();
    }

    private static void Inject(TypeDefinition targetType, List<Req> reqs)
    {
        var expose = targetType.Methods.FirstOrDefault(
            m => m.Name == "ExposeData" && m.Parameters.Count == 0 && m.HasBody);
        if (expose == null)
        {
            foreach (var r in reqs)
                Lg.Error($"Serialización automática: no se pudo enganchar {r.TargetType.FullName}.{r.Field.Name} " +
                         "— el tipo destino no tiene ExposeData() para inyectar la llamada Scribe.");
            return;
        }

        var il = expose.Body.GetILProcessor();
        var first = expose.Body.Instructions[0];
        int ok = 0;
        foreach (var r in reqs)
        {
            try
            {
                if (TryEmit(il, first, r, targetType.Module))
                    ok++;
            }
            catch (Exception e)
            {
                Lg.Error($"Serialización automática: error al enganchar {r.TargetType.FullName}.{r.Field.Name}: {e.Message}");
            }
        }

        if (ok > 0)
            Lg.Info($"Serialización automática: {ok} campo(s) de {targetType.FullName} enganchados a ExposeData.");
    }

    /// <summary>Escribe la secuencia IL que llama al Scribe para un campo (primitiva, List,
    /// Dictionary, HashSet). v1(=bloque 6 avanza): primitivas/string/enums y las colecciones
    /// de esos. Arrays y colecciones de OBJETOS (refs/deep) se loguean como no soportados.</summary>
    private static bool TryEmit(ILProcessor il, Instruction first, Req r, ModuleDefinition module)
    {
        var fieldType = r.Field.FieldType;

        // Dictionary<K,V> de primitivas/string/enums → Scribe_Collections.Look(ref Dict, label, Value, Value)
        if (fieldType is GenericInstanceType gd
            && gd.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
            && gd.GenericArguments.Count == 2)
        {
            var k = module.ImportReference(gd.GenericArguments[0]);
            var v = module.ImportReference(gd.GenericArguments[1]);
            if (!IsScribeValueType(k) || !IsScribeValueType(v))
            {
                Lg.Error($"No se puede serializar automáticamente {r.Field.FullName}: " +
                         $"el Dictionary requiere claves Y valores de primitivas/string/enums (v1), " +
                         $"no '{k.FullName}' / '{v.FullName}'.");
                return false;
            }

            var look = module.ImportReference(GetDictionaryLook());
            var gi = new GenericInstanceMethod(look);
            gi.GenericArguments.Add(k);
            gi.GenericArguments.Add(v);

            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));       // this
            il.InsertBefore(first, il.Create(OpCodes.Ldflda, r.Field)); // ref Dictionary<K,V>
            il.InsertBefore(first, il.Create(OpCodes.Ldstr, r.Label));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, (int)LookMode.Value)); // key
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, (int)LookMode.Value)); // value
            il.InsertBefore(first, il.Create(OpCodes.Call, gi));
            return true;
        }

        // HashSet<T> de primitivas/string/enums → Scribe_Collections.Look(ref HashSet, label, Value)
        if (fieldType is GenericInstanceType gh
            && gh.ElementType.FullName == "System.Collections.Generic.HashSet`1"
            && gh.GenericArguments.Count == 1)
        {
            var elem = module.ImportReference(gh.GenericArguments[0]);
            if (!IsScribeValueType(elem))
            {
                Lg.Error($"No se puede serializar automáticamente {r.Field.FullName}: " +
                         $"el HashSet requiere primitivas/string/enums (v1), no '{elem.FullName}'.");
                return false;
            }

            var look = module.ImportReference(GetHashSetLook());
            var gi = new GenericInstanceMethod(look);
            gi.GenericArguments.Add(elem);

            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));       // this
            il.InsertBefore(first, il.Create(OpCodes.Ldflda, r.Field)); // ref HashSet<T>
            il.InsertBefore(first, il.Create(OpCodes.Ldstr, r.Label));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, (int)LookMode.Value));
            il.InsertBefore(first, il.Create(OpCodes.Call, gi));
            return true;
        }

        // List<T> de primitivas/string/enums → Scribe_Collections.Look(ref List<T>, label, Value, null)
        if (fieldType is GenericInstanceType git
            && git.ElementType.FullName == "System.Collections.Generic.List`1"
            && git.GenericArguments.Count == 1)
        {
            var elem = module.ImportReference(git.GenericArguments[0]);
            if (!IsScribeValueType(elem))
            {
                Lg.Error($"No se puede serializar automáticamente {r.Field.FullName}: tipo de lista no soportado " +
                         $"'{git.GenericArguments[0].FullName}' (v1: primitivas, string, enums).");
                return false;
            }

            var look = module.ImportReference(GetListLook(4));
            var gi = new GenericInstanceMethod(look);
            gi.GenericArguments.Add(elem);

            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));      // this
            il.InsertBefore(first, il.Create(OpCodes.Ldflda, r.Field)); // ref List<T>
            il.InsertBefore(first, il.Create(OpCodes.Ldstr, r.Label));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, (int)LookMode.Value));
            il.InsertBefore(first, il.Create(OpCodes.Ldnull));       // ctorArgs
            il.InsertBefore(first, il.Create(OpCodes.Call, gi));
            return true;
        }

        // Primitiva / string / enum → Scribe_Values.Look(ref T, label, default, forceSave=true)
        if (IsScribeValueType(fieldType))
        {
            var look = module.ImportReference(GetValuesLook(4));
            var gi = new GenericInstanceMethod(look);
            gi.GenericArguments.Add(module.ImportReference(fieldType));

            var pushDefault = MakePush(il, fieldType, r.DefaultValue);
            if (pushDefault == null)
            {
                Lg.Error($"No se puede serializar automáticamente {r.Field.FullName}: tipo '{fieldType.FullName}' sin default CLR simple.");
                return false;
            }

            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Ldflda, r.Field));
            il.InsertBefore(first, il.Create(OpCodes.Ldstr, r.Label));
            il.InsertBefore(first, pushDefault);
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));     // forceSave
            il.InsertBefore(first, il.Create(OpCodes.Call, gi));
            return true;
        }

        Lg.Error($"No se puede serializar automáticamente {r.Field.FullName}: tipo no soportado " +
                 $"'{fieldType.FullName}' (v1: primitivas, string, enums y List de esos).");
        return false;
    }

    /// <summary>¿El tipo es serializable con Scribe_Values (primitiva, string, enum, bool)?</summary>
    private static bool IsScribeValueType(TypeReference t)
    {
        switch (t.MetadataType)
        {
            case MetadataType.Boolean:
            case MetadataType.SByte:
            case MetadataType.Byte:
            case MetadataType.Int16:
            case MetadataType.UInt16:
            case MetadataType.Int32:
            case MetadataType.UInt32:
            case MetadataType.Int64:
            case MetadataType.UInt64:
            case MetadataType.Single:
            case MetadataType.Double:
            case MetadataType.String:
                return true;
        }
        // Enums se serializan por su valor entero (Scribe_Values los maneja como T).
        try
        {
            return t.Resolve()?.IsEnum == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Empuja el default (si el attr lo dio) o el cero/nulo CLR del tipo.</summary>
    private static Instruction? MakePush(ILProcessor il, TypeReference fieldType, object? defaultValue)
    {
        var meta = fieldType.MetadataType;
        if (defaultValue != null)
        {
            // Si el mod dio Default, usarlo (por coherencia con el inicializador).
            switch (meta)
            {
                case MetadataType.Boolean:
                    return il.Create(OpCodes.Ldc_I4, Convert.ToInt32(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture) & 1);
                case MetadataType.SByte:
                case MetadataType.Byte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                    return il.Create(OpCodes.Ldc_I4, Convert.ToInt32(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture));
                case MetadataType.Int64:
                case MetadataType.UInt64:
                    return il.Create(OpCodes.Ldc_I8, Convert.ToInt64(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture));
                case MetadataType.Single:
                    return il.Create(OpCodes.Ldc_R4, Convert.ToSingle(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture));
                case MetadataType.Double:
                    return il.Create(OpCodes.Ldc_R8, Convert.ToDouble(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture));
                case MetadataType.String:
                    return il.Create(OpCodes.Ldstr, Convert.ToString(defaultValue,
                        System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Default CLR
        switch (meta)
        {
            case MetadataType.Boolean:
            case MetadataType.SByte:
            case MetadataType.Byte:
            case MetadataType.Int16:
            case MetadataType.UInt16:
            case MetadataType.Int32:
            case MetadataType.UInt32:
                return il.Create(OpCodes.Ldc_I4_0);
            case MetadataType.Int64:
            case MetadataType.UInt64:
                return il.Create(OpCodes.Ldc_I8, 0L);
            case MetadataType.Single:
                return il.Create(OpCodes.Ldc_R4, 0f);
            case MetadataType.Double:
                return il.Create(OpCodes.Ldc_R8, 0d);
            case MetadataType.String:
                return il.Create(OpCodes.Ldnull);
        }
        return null;
    }

    private static MethodInfo GetValuesLook(int paramCount)
        => typeof(Scribe_Values).GetMethods()
            .First(m => m.Name == "Look"
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == paramCount
                        && m.GetParameters()[0].ParameterType.IsByRef
                        && m.GetParameters()[0].ParameterType.GetElementType() == m.GetGenericArguments()[0]);

    private static MethodInfo GetListLook(int paramCount)
        => typeof(Scribe_Collections).GetMethods()
            .First(m => m.Name == "Look"
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == paramCount
                        && m.GetParameters()[0].ParameterType.IsByRef
                        && m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericType == true
                        && m.GetParameters()[0].ParameterType.GetElementType()?.GetGenericTypeDefinition() == typeof(List<>));

    /// <summary>Overload de Scribe_Collections.Look específico de Dictionary: 2 genéricos,
    /// 4 params (ref Dictionary, label, keyLookMode, valueLookMode).</summary>
    private static MethodInfo GetDictionaryLook()
        => typeof(Scribe_Collections).GetMethods()
            .First(m => m.Name == "Look"
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 2
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType.IsByRef
                        && m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericType == true
                        && m.GetParameters()[0].ParameterType.GetElementType()?.GetGenericTypeDefinition() == typeof(Dictionary<,>));

    /// <summary>Overload de Scribe_Collections.Look específico de HashSet: 1 genérico,
    /// 3 params (ref HashSet, label, lookMode).</summary>
    private static MethodInfo GetHashSetLook()
        => typeof(Scribe_Collections).GetMethods()
            .First(m => m.Name == "Look"
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == 3
                        && m.GetParameters()[0].ParameterType.IsByRef
                        && m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericType == true
                        && m.GetParameters()[0].ParameterType.GetElementType()?.GetGenericTypeDefinition() == typeof(HashSet<>));
}