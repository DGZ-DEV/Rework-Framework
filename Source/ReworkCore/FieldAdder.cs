using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Rework.Core;

/// <summary>
/// Añade campos REALES a tipos del juego a partir de accessors marcados con
/// [ReworkField]. Diseño inspirado en Prepatcher
/// Source/Implementation/Process/FieldAdder.cs, con la MEJORA #1 de Rework:
/// el nombre del campo es el nombre EXACTO del accessor (definido por el modder),
/// no un nombre generado ("asmShortName + accessorName + MetadataToken.RID"
/// como en Prepatcher).
///
/// Patrón de accessor (igual que Prepatcher):
///   [ReworkField] public static extern ref int Rework_Test(this Pawn pawn);
/// </summary>
internal class FieldAdder
{
    private readonly AssemblySet set;

    public FieldAdder(AssemblySet set)
    {
        this.set = set;
    }

    internal void ProcessAllAssemblies()
    {
        foreach (var asm in set.AllAssemblies.Where(a => a.ProcessAttributes))
        {
            foreach (var accessor in FindAccessors(asm.ModuleDefinition.Types))
                ProcessAccessor(asm, accessor);
        }

        // Serialización automática: enganchar los campos con [ReworkField(Serialize=true)]
        // al ExposeData de su tipo destino (después de que TODOS los campos ya existan).
        FieldScribe.FlushAll();
    }

    /// <summary>
    /// Accessors = métodos estáticos (en clase estática) con [ReworkField].
    /// (Prepatcher FieldAdder.GetAllPrepatcherFieldAccessors: t.IsSealed && t.IsAbstract.)
    /// </summary>
    private static IEnumerable<MethodDefinition> FindAccessors(IEnumerable<TypeDefinition> inTypes)
    {
        return
            from t in inTypes
            where t.IsSealed && t.IsAbstract
            from m in t.Methods
            where m.CustomAttributes.Any(a => a.AttributeType.FullName == "Rework.ReworkFieldAttribute")
            select m;
    }

    private void ProcessAccessor(ModifiableAssembly accessorAsm, MethodDefinition accessor)
    {
        // --- 0. Leer el atributo (inicializador por método o Default, si los hay) ---
        object? defaultVal = null;
        TypeReference? defaultValType = null;
        string? initializerName = null;
        bool serialize = false;
        var attr = accessor.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "Rework.ReworkFieldAttribute");
        if (attr != null)
        {
            // Forma de propiedad con nombre: [ReworkField(Default = 42)] / [Initializer = "M"]
            foreach (var prop in attr.Properties)
            {
                switch (prop.Name)
                {
                    case "Default":
                        defaultVal = UnwrapArgument(prop.Argument.Value);
                        defaultValType = prop.Argument.Type;
                        break;
                    case "Initializer":
                        initializerName = UnwrapArgument(prop.Argument.Value) as string;
                        break;
                    case "Serialize":
                        serialize = UnwrapArgument(prop.Argument.Value) is bool b && b;
                        break;
                }
            }
            // Forma de constructor: [ReworkField(42)]
            if (defaultVal == null && attr.ConstructorArguments.Count > 0)
            {
                defaultVal = UnwrapArgument(attr.ConstructorArguments[0].Value);
                defaultValType = attr.ConstructorArguments[0].Type;
            }
        }

        // --- 1. Tipo destino = primer parámetro (el "this") ---
        var targetType = accessor.Parameters[0].ParameterType.Resolve();
        if (targetType == null)
        {
            Lg.Error($"{accessorAsm.FriendlyName}: no se pudo resolver el tipo destino de {accessor.Name}");
            return;
        }

        // --- 2. Nombre y tipo del campo ---
        var fieldName = FieldName(accessor); // MEJORA #1: nombre exacto del modder
        var fieldType = accessor.ReturnType.IsByReference
            ? ((ByReferenceType)accessor.ReturnType).ElementType
            : accessor.ReturnType;

        // Fase 3: tipos ARBITRARIOS (primitivas, clases, genéricos, tipos del juego).
        // Se importa el tipo al módulo destino con el patrón DummyMethodReference de
        // Prepatcher (FieldAdder.ImportFieldTypeIntoTargetModule): el contexto genérico
        // del tipo destino garantiza que referencias a sus parámetros genéricos (y el
        // scope correcto: mscorlib/Assembly-CSharp) se importen bien.
        var fieldTypeInTarget = ImportFieldTypeIntoTargetModule(accessor, targetType);

        // --- 3. Orden entre mods (cross-mod): el PRIMER campo con ese nombre gana. ---
        //    Dos accessors (del mismo mod o de mods distintos) que pidan el MISMO nombre de
        //    campo en el MISMO tipo no pueden crear dos campos (metadata inválida: nombres
        //    duplicados en un tipo). Se resuelve por orden de carga del mod:
        //    - El primer mod registrado crea el campo (y aplica su inicializador).
        //    - Los siguientes accessors se ENLAZAN al campo existente (compartido), sin
        //      duplicarlo ni re-inicializarlo. Solo se enlazan si el TIPO coincide; si un
        //      mod pide el mismo nombre con otro tipo, el accessor se reescribe para
        //      LANZAR una excepción clara al ser usado (conflicto real).
        var existingField = targetType.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (existingField != null)
        {
            if (existingField.FieldType.FullName != fieldTypeInTarget.FullName)
            {
                MakeAccessorThrow(accessor,
                    $"Conflicto de campo '{targetType.FullName}.{fieldName}': ya existe con tipo " +
                    $"'{existingField.FieldType.FullName}' y {accessorAsm.FriendlyName} pide " +
                    $"'{fieldTypeInTarget.FullName}'. El primer mod gana; este accessor está roto.");
                accessorAsm.Modified = true;
                Lg.Error($"Conflicto de campo '{targetType.FullName}.{fieldName}': " +
                         $"tipo {existingField.FieldType.FullName} vs {fieldTypeInTarget.FullName} " +
                         $"(mod {accessorAsm.FriendlyName}). Accessor reescrito para lanzar.");
                return;
            }

            // Tipos iguales → campo COMPARTIDO: enlazar el accessor al campo del primer dueño.
            RewriteAccessor(accessor, existingField, accessorAsm, fieldOwnerOf(accessor), fieldName);
            Lg.Info($"Campo compartido: {targetType.FullName}.{fieldName} ya existía "
                    + $"(del juego u otro mod); el accessor de {accessorAsm.FriendlyName} se enlaza. "
                    + "El primer dueño gana la creación y el inicializador.");
            return;
        }

        // --- 4. Crear el campo nuevo ---
        var ceField = new FieldDefinition(fieldName, FieldAttributes.Public, fieldTypeInTarget);
        targetType.Fields.Add(ceField);

        var targetAsm = set.FindAssembly(targetType.Module.Assembly.Name.Name);
        if (targetAsm != null)
            targetAsm.Modified = true;
        Lg.Info($"Campo añadido: {targetType.FullName}.{fieldName}");
        Data.DataStore.InjectedFields.Add($"{targetType.FullName}.{fieldName} ({fieldTypeInTarget.Name})");

        // --- 5. Reescribir el accessor: quitar extern y poner cuerpo real ---
        //    ldarg.0
        //    ldflda / ldfld  campo
        //    ret
        // El campo se referencia desde el módulo del accessor con el declaring type
        // importado (patrón validado de Prepatcher FieldAdder.PatchAccessor).
        RewriteAccessor(accessor, ceField, accessorAsm, fieldOwnerOf(accessor), fieldName);

        // --- 6. Inicializador: por método (prioridad) o por constante Default ---
        //    Sin esto el campo nace en 0/null. Equivalente a un inicializador de C#:
        //    "public float x = 1.0f;" (Default) o "public List<Item> x = Init();" (Initializer).
        //    Prepatcher: PatchCtorsWithDefault / PatchCtorsWithInitializer.
        if (initializerName != null)
            PatchCtorsWithInitializer(targetType, ceField, accessor, initializerName);
        else if (defaultVal != null && defaultValType != null)
            PatchCtorsWithDefault(targetType, ceField, defaultVal, defaultValType);

        // --- 7. Serialización automática: solo el CREADOR del campo decide. ---
        //    Si otro mod se enlaza después (campo compartido) NO se reafirma: el
        //    primero que lo cree con Serialize=true lo guarda para todos.
        if (serialize)
        {
            FieldScribe.Request(new FieldScribe.Req(targetType, ceField, fieldName, defaultVal));
            Lg.Info($"Campo serializado: {targetType.FullName}.{fieldName} (Serializar vía Scribe en ExposeData).");
        }
    }

    /// <summary>Tipo del "this" del accessor (sin byref), para referenciar el campo.</summary>
    private static TypeReference fieldOwnerOf(MethodDefinition accessor)
    {
        var owner = accessor.Parameters[0].ParameterType;
        return owner.IsByReference ? ((ByReferenceType)owner).ElementType : owner;
    }

    /// <summary>
    /// Convierte el accessor extern en un método real: ldarg.0; ldflda/ldfld campo; ret.
    /// El campo de origen vive en el módulo DESTINO (Assembly-CSharp).
    /// </summary>
    private static void RewriteAccessor(
        MethodDefinition accessor, FieldReference sourceField,
        ModifiableAssembly accessorAsm, TypeReference fieldOwner, string fieldName)
    {
        accessor.ImplAttributes &= ~MethodImplAttributes.InternalCall; // un-extern

        var fieldRef = new FieldReference(
            fieldName,
            accessor.Module.ImportReference(sourceField.FieldType, accessor.Module.ImportReference(sourceField.DeclaringType)),
            fieldOwner);

        var body = new MethodBody(accessor);
        var il = body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(accessor.ReturnType.IsByReference ? OpCodes.Ldflda : OpCodes.Ldfld, fieldRef);
        il.Emit(OpCodes.Ret);
        accessor.Body = body;

        accessorAsm.Modified = true;
        Lg.Info($"Accessor reescrito: {accessor.FullName} → campo {fieldName}");
    }

    /// <summary>Reescribe un accessor extern para lanzar una excepción clara si se usa
    /// (conflictos reales de campo donde el primer mod ya ganó con otro tipo).</summary>
    private static void MakeAccessorThrow(MethodDefinition accessor, string message)
    {
        accessor.ImplAttributes &= ~MethodImplAttributes.InternalCall;
        var module = accessor.Module;
        var ctor = module.ImportReference(typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!);

        var body = new MethodBody(accessor);
        var il = body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Throw);
        accessor.Body = body;
    }

    /// <summary>
    /// Inyecta en cada constructor de instancia del tipo destino la inicialización del
    /// campo (<c>ldarg.0; <empujar valor>; stfld campo</c>). Prepatcher: FieldAdder.
    /// PatchCtorsWithDefault.
    /// </summary>
    private static void PatchCtorsWithDefault(TypeDefinition targetType, FieldReference field, object value, TypeReference valueType)
    {
        int patched = 0;
        foreach (var ctor in targetType.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
        {
            if (TryInsertFieldInit(ctor, field, value, valueType))
                patched++;
        }
        if (patched > 0)
            Lg.Info($"Inicializador aplicado: {targetType.FullName}.{field.Name} = {value} en {patched} constructor(es).");
        else
            Lg.Error($"Inicializador NO aplicado a {targetType.FullName}.{field.Name}: no se halló constructor válido.");
    }

    /// <summary>
    /// Inserta la inicialización al INICIO del constructor (índice 0):
    /// <c>ldarg.0; <empujar valor>; stfld campo</c>. Escribir campos antes del ctor de
    /// la base es IL válido (el CLR solo restringe llamadas/visibilidad, no asignaciones
    /// de campos). Al insertar en el índice 0 las instrucciones quedan ANTES de cualquier
    /// región try/catch (no se corrompen). Cualquier error se degrada (no aborta el reload).
    /// </summary>
    private static bool TryInsertFieldInit(MethodDefinition ctor, FieldReference field, object value, TypeReference valueType)
    {
        try
        {
            // Insertar en el índice 0 (ANTES de cualquier región try): las instrucciones
            // añadidas quedan fuera de los manejadores de excepción. Insertar en MEDIO
            // del cuerpo (p.ej. tras el call a la base) corrompe las regiones EH y
            // colgaba el boot (roto por la primera implementación).
            var first = ctor.Body.Instructions[0];
            var il = ctor.Body.GetILProcessor();

            var ldarg0 = il.Create(OpCodes.Ldarg_0);
            var push = MakePush(il, field.FieldType.MetadataType, value);
            if (push == null)
                return false;
            var stfld = il.Create(OpCodes.Stfld, field);

            // InsertBefore inserta cada uno justo antes de 'first' → orden: ldarg0, push, stfld.
            il.InsertBefore(first, ldarg0);
            il.InsertBefore(first, push);
            il.InsertBefore(first, stfld);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
/// Inicializador POR MÉTODO (estilo Prepatcher PatchCtorsWithInitializer): en cada
/// constructor se llama a un método estático público (en la misma clase estática que el
/// accessor) que produce el valor inicial. Necesario para tipos complejos (List<>, estado).
/// 0 parámetros → campo = Metodo();  1 parámetro → campo = Metodo(this).
/// </summary>
private static void PatchCtorsWithInitializer(
    TypeDefinition targetType, FieldReference field, MethodDefinition accessor, string initName)
{
    var initDef = accessor.DeclaringType.Methods.FirstOrDefault(x => x.IsStatic && x.Name == initName);
    if (initDef == null)
    {
        Lg.Error($"Inicializador por método: no se halló la función estática '{initName}' en {accessor.DeclaringType.FullName}.");
        return;
    }
    if (!initDef.IsPublic)
    {
        Lg.Error($"Inicializador por método '{initName}': debe ser PÚBLICA (acceso entre ensamblados).");
        return;
    }
    if (initDef.ReturnType.MetadataType == MetadataType.Void)
    {
        Lg.Error($"Inicializador por método '{initName}': debe devolver un valor, no void.");
        return;
    }
    if (initDef.Parameters.Count > 1)
    {
        Lg.Error($"Inicializador por método '{initName}': soporta 0 o 1 parámetro (la instancia), no {initDef.Parameters.Count}.");
        return;
    }

    MethodReference initRef;
    try
    {
        initRef = targetType.Module.ImportReference(initDef);
    }
    catch (Exception e)
    {
        Lg.Error($"Inicializador por método '{initName}': no se pudo importar al módulo destino: {e.Message}");
        return;
    }

    int takeParams = initDef.Parameters.Count;
    int patched = 0;
    foreach (var ctor in targetType.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
    {
        if (TryInsertMethodInit(ctor, field, initRef, takeParams))
            patched++;
    }
    if (patched > 0)
        Lg.Info($"Inicializador por método aplicado: {targetType.FullName}.{field.Name} ← {accessor.DeclaringType.FullName}::{initName} en {patched} constructor(es).");
    else
        Lg.Error($"Inicializador por método NO aplicado a {targetType.FullName}.{field.Name}: sin constructor válido.");
}

/// <summary>
/// Inserta al INICIO del ctor: <c>ldarg.0; [dup]; call inicializador; stfld campo</c>.
/// Si el inicializador toma 1 parámetro, se le pasa 'this' con dup (queda otra copia de
/// 'this' en pila para el stfld). Ignora ctors que fallen (no aborta el reload).
/// </summary>
private static bool TryInsertMethodInit(MethodDefinition ctor, FieldReference field, MethodReference initRef, int takeParams)
{
    try
    {
        var first = ctor.Body.Instructions[0];
        var il = ctor.Body.GetILProcessor();

        // Orden de pila para stfld: [this, valor] (this debajo). Con dup mantenemos this.
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        if (takeParams == 1)
            il.InsertBefore(first, il.Create(OpCodes.Dup));
        il.InsertBefore(first, il.Create(OpCodes.Call, initRef));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, field));
        return true;
    }
    catch
    {
        return false;
    }
}

/// <summary>
/// Empuja la constante del tipo correcto según el MetadataType del campo.</summary>
    private static Instruction? MakePush(ILProcessor il, MetadataType fieldMeta, object value)
    {
        switch (fieldMeta)
        {
            case MetadataType.Boolean:
                return il.Create(OpCodes.Ldc_I4, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) & 1);
            case MetadataType.SByte:
            case MetadataType.Int16:
            case MetadataType.Int32:
            case MetadataType.Byte:
            case MetadataType.UInt16:
            case MetadataType.UInt32:
                return il.Create(OpCodes.Ldc_I4, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
            case MetadataType.Int64:
            case MetadataType.UInt64:
                return il.Create(OpCodes.Ldc_I8, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
            case MetadataType.Single:
                return il.Create(OpCodes.Ldc_R4, Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture));
            case MetadataType.Double:
                return il.Create(OpCodes.Ldc_R8, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
            case MetadataType.String:
                return il.Create(OpCodes.Ldstr, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            default:
                // Enum u otro tipo con base numérica: probamos como int32.
                try { return il.Create(OpCodes.Ldc_I4, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
        }
    }

    /// <summary>
    /// Desempaqueta un valor de atributo de tipo 'object': Cecil lo entrega como un
    /// CustomAttributeArgument anidado; recorremos .Value hasta el primitivo real.
    /// </summary>
    private static object? UnwrapArgument(object? v)
    {
        while (v is CustomAttributeArgument caa)
            v = caa.Value;
        return v;
    }

    /// <summary>
    /// MEJORA #1 de Rework sobre Prepatcher: el modder controla el nombre exacto
    /// del campo (aquí: el nombre del método accessor). Prepatcher genera
    /// "asmShortName + accessorName + MetadataToken.RID" (FieldAdder.cs, Zetrith)
    /// sin garantías para el modder.
    /// </summary>
    private static string FieldName(MethodDefinition accessor) => accessor.Name;

    /// <summary>
    /// Fase 3: importa el tipo del campo al módulo DESTINO (Assembly-CSharp) usando el
    /// patrón DummyMethodReference de Prepatcher: la importación de un TypeReference con
    /// un contexto de método "dummy" que expone los parámetros genéricos del tipo destino.
    /// Sin esto, tipos genéricos (p.ej. List&lt;T&gt; con T = tipo destino) se importarían
    /// con un scope/contexto incorrecto.
    /// </summary>
    private static TypeReference ImportFieldTypeIntoTargetModule(MethodDefinition accessor, TypeDefinition targetType)
    {
        var fieldType = accessor.ReturnType.IsByReference
            ? ((ByReferenceType)accessor.ReturnType).ElementType
            : accessor.ReturnType;

        return targetType.Module.ImportReference(
            fieldType,
            new DummyMethodReference(
                accessor.Name,
                targetType.Module.ImportReference(accessor.DeclaringType),
                targetType.Module,
                targetType.GenericParameters));
    }

    /// <summary>
    /// Contexto de importación "dummy" (copiado del enfoque de Prepatcher): un
    /// MethodReference cuyo único cometido es transportar los GenericParameters del
    /// tipo destino durante la importación de un TypeReference a otro módulo.
    /// </summary>
    private sealed class DummyMethodReference : MethodReference
    {
        private readonly Mono.Collections.Generic.Collection<GenericParameter> genericParameters;

        public override Mono.Collections.Generic.Collection<GenericParameter> GenericParameters => genericParameters;

        public DummyMethodReference(
            string name,
            TypeReference declaringType,
            ModuleDefinition module,
            Mono.Collections.Generic.Collection<GenericParameter> genericParameters)
            : base(name, module.TypeSystem.Void, declaringType)
        {
            DeclaringType = declaringType;
            this.genericParameters = genericParameters;
        }
    }
}
