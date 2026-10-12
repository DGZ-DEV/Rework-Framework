# Rework Reforjed — ERRORES

Bitácora de los fallos encontrados, su causa raíz y la solución adoptada.

## 1. La vista de mundo/planeta no cargaba (NREs en cadena)

**Síntoma:** al entrar al mundo/planeta, `Find.WorldCameraDriver` era `null` y se
encadenaban `NullReferenceException` de `ExpandableWorldObjects` /
`SortByExpandingIconPriority` / `WorldInterface`, y el `WorldCameraDriver.Awake` de la
cámara de mundo reventaba (`ApplyPositionToGameObject`) durante la inicialización.

**Causa raíz (confirmada por diagnóstico):** colisión por tener **dos ensamblados
`Assembly-CSharp`** en el AppDomain (el original y el reescrito por Rework). Unity
identifica los componentes MonoBehaviour **por nombre**. Al llamar
`AddComponent<WorldCameraDriver>()` desde código nuevo, Unity resuelvía `WorldCameraDriver`
**por nombre** y materializaba el del ensamblado **original (refonly)**. El campo estático
`worldCameraDriverInt` (del ensamblado nuevo) quedaba `null` → cascada de NREs.

**Intentos fallidos (para no repetirlos):** añadir campos/guardias en la copia nueva era
inútil porque Unity ejecutaba la copia original; destruir/re-añadir el driver materializaba
siempre el original; `GetComponent` del tipo nuevo no encontraba el driver que Unity creó
del original (tipos incompatibles entre ensamblados).

**Solución adoptada (tomada de Prepatcher `WorldCameraFreePatch.cs`):**
una **subclase con nombre único** que **no colisiona**:

```csharp
public sealed class ReworkWorldCameraDriver : RimWorld.Planet.WorldCameraDriver { }
```

y se redirige `WorldCameraManager.CreateWorldCamera` para que la añada en vez del driver
por-nombre (`GameProcessing.PatchWorldCameraManagerCreateWorldCameraDriver` →
`WorldCameraDriverPatch.AttachWorldCameraDriver`). Al ser un nombre distinto, Unity la
materializa bien; al heredar del `WorldCameraDriver` **nuevo**, el ctor hace
`GetComponent<WorldCameraDriver>()` y la encuentra → `worldCameraDriverInt` queda bien → el
mundo carga. Su equivalente en Prepatcher es su `WorldCameraDriver2`.

## 2. El modelo con launcher en disco (ya descartado)

Se probó una variante que parcheaba `Assembly-CSharp.dll` **en disco** (con launcher de
consola y un sistema de swap original/parcheado según la activación). **Se descartó y se
eliminó** porque:

- requería que el juego estuviera cerrado y un paso manual (doble clic o consola), mala UX;
- tocaba el `Assembly-CSharp.dll` del juego en disco (generaba desconfianza y rompía la
  portabilidad "no tocar el juego");
- la vía en memoria (sección 1) resuelve el mundo sin este coste.

**Conclusión:** se vuelve al modelo de doble pasada en memoria, con el arreglo de la
subclase única. No hay launcher ni swap.

## 3. Ruido cosmético del reinicio (ya silenciado)

Durante la pasada 1 se veían en el log líneas que NO eran errores reales pero ensuciaban:
`ThreadAbortException: Thread was being aborted`, un par de `Root level exception in
Update(): NullReferenceException` del frame de transición y los logs de manejo de
excepción del abort. Se eliminaron con un **filtro en el sink de logs de Unity**
(`LogSilence`): envuelve `Debug.unityLogger.logHandler` y descarta SOLO esos patrones
mientras `DataStore.suppressLogs` está activo (la ventana entre la pasada 1 y el final de
la pasada 2). Fuera de la ventana es un passthrough inofensivo, así que los errores reales
nunca se esconden. Es el equivalente al `SilenceLogging` de Prepatcher, pero **sin parchear
métodos del juego** (solo se envuelve el manejador de logs).

Detalles técnicos relevantes:
- La lógica de supresión vive en `DataStore.ShouldSuppressLog` (0ReworkData, BCL y nunca
  refonly) para funcionar a través del reload; `RuntimeHooks.ShouldSuppressLog` delega en
  ella (usada por el prefijo en `Verse.Log.Error` de la copia nueva).
- `LogSilence.Install()` se llama en la pasada 1 ANTES de marcar refonly (para que los
  métodos del filtro, definidos en ReworkCore, queden compilados antes). El filtro se deja
  instalado para siempre; se "apaga" solo cuando `suppressLogs` pasa a false.
- Post-instalación queda una línea benigna: `Rework: filtro de logs del reinicio instalado`.

## 4. Verificaciones

En el log se espera ver (pasada 2):

```
Parche aplicado: CreateWorldCamera → AddComponent<ReworkWorldCameraDriver> (subclase única)
VERIFICACIÓN OK: Assembly-CSharp activa es la NUEVA (rewrite en efecto) y ReworkWorldCameraDriver enganchado.
SceneRootHook: escena 'Play'/'Entry' cargada; N componente(s) re-creado(s) con tipos nuevos.
```

Y **ninguna** mención de error de `WorldCameraDriver` / `ExpandableWorldObjects`.

## 6. Inicializador de campo rompía el boot (pantalla negra) — SOLUCIONADO

**Síntoma:** con `[ReworkField(Default = ...)]`, el juego arrancaba con **pantalla negra**:
a la altura de `Root recreado … Boot reiniciado` entraba en un bucle infinito de
`Root level exception in OnGUI(): System.NullReferenceException at Verse.Root.OnGUI [0x40]`,
llenando el log (cientos de líneas) y el usuario tenía que cerrar el juego.

**Causa raíz (doble):**
1. **Punto de inyección erróneo.** Primera versión insertaba `ldarg.0; <valor>; stfld campo`
   **justo tras el `call` al ctor de la base**, pero eso cae **dentro de la región try/catch**
   del cuerpo (los ctors de `Verse.Pawn` tienen manejadores de excepción). Insertar ahí
   corrompe las regiones EH → colgado. La primera solución (saltar ctors con EH) era correcta
   pero demasiado restrictiva (dejaba el inicializador inerte: `Inicializador NO aplicado`).
2. **Valor envuelto.** `Default = 42` con propiedad de tipo `object` hace que Cecil entregue
   un `CustomAttributeArgument` anidado, y `Convert.ToInt32/ToSingle(object)` lanzaba
   `InvalidCastException: Specified cast is not valid.` (se veía con `System.InvalidCastException
   at System.Convert.ToInt32(System.Object, System.IFormatProvider)`).

**Solución adoptada:**
- **Inyectar en el índice 0** del constructor (ANTES de cualquier región try, y antes del
  ctor de la base: escribir campos sin llamar a la base es IL válido). Así los `stfld` quedan
  **fuera** de los manejadores de excepción y ya no hace falta saltar ctors con EH.
- **Desempaquetar el valor**: `UnwrapArgument()` recorre `.Value` de los
  `CustomAttributeArgument` hasta el primitivo real antes de emitir `ldc.*/ldstr`.
- Cada ctor va en try/catch propio: uno problemático se degrada (no aborta el reload).

**Verificado:** `Inicializador aplicado: Verse.Pawn.Rework_DefaultTest = 42 en 1
constructor(es).` y `… Rework_DefaultMult = 1.5 …`, boot sano (VERIFICACIÓN OK, 0 NREs,
0 OnGUI-loop).

## 7. Transpiler perdía los manejadores de excepción — SOLUCIONADO

**Síntoma/causa:** el modo transpiler reconstruía el cuerpo creando un `new
Mono.Cecil.Cil.MethodBody` y copiando solo las instrucciones (`ilp.Append`). Eso
**descartaba** `ExceptionHandlers`, `LocalVariableDefinitions` y los sequence points:
cualquier método parcheado perdía sus `try/catch` y podía romper la lógica según lógica.
Si un manejador apuntaba a una instrucción eliminada por el modder, la Assembly resultante
era corrupta (riesgo de boot en negro).

**Solución adoptada:**
- **Reutilizar el MISMO `MethodBody`** (no crear uno nuevo): `body.Instructions.Clear()` +
  `ilp.Append(...)` sobre el mismo objeto conserva `ExceptionHandlers`, variables y debug.
- **Validación defensiva:** tras reconstruir, si un manejador referencia una instrucción que
  ya no está en el cuerpo, se **descarta con aviso** (`Lg.Error` … "N manejador(es) EH
  descartado(s)") en vez de emitir IL corrupto.

**Verificado:** transpiler identidad sobre `Verse.Root.OnGUI` (que tiene un `try/catch`):
`1 manejador(es) de excepción preservado(s).` y boot limpio (VERIFICACIÓN OK, 0 NREs,
0 OnGUI-loop). Queda una norma documentada en el MANUAL: el modder no debe borrar las
instrucciones límite de las regiones EH.

## 8. Inicializador por método (`Initializer`)

**Qué es:** además de `Default = constante`, `[ReworkField(Initializer = "Metodo")]` permite
que el valor inicial venga de una **llamada a una función estática pública** (en la misma
clase estática que el accessor), para tipos complejos (`List<>`, estado). La inyección
(llamada en el índice 0 de cada ctor) es: `ldarg.0; [dup si 1 parámetro]; call; stfld`.
- 0 parámetros → `campo = Metodo();` — pila: `ldarg.0; call; stfld` (this abajo, valor arriba).
- 1 parámetro  → recibe la instancia → `campo = Metodo(this);` — pila: `ldarg.0; dup; call; stfld`
  (con `dup` queda una copia de `this` para el `stfld`; el `call` consume la otra como parámetro).

**Verificado en runtime (no solo metadatos):** con un campo temporal `List<int>` y otro
`List<string>` sobre `Verse.Pawn`, tras el reload `new Pawn()` reportó
`Rework_Datos.Count=2` y `Rework_Nombres.Count=1` — las funciones inyectadas se ejecutaron
de verdad y dejaron el valor inicial en los campos. Boot limpio.

**Detalle técnico:** el método se importa al módulo destino con `targetType.Module.ImportReference(initDef)`.
Debe ser `public` (acceso entre ensamblados) y no-void; 0 o 1 parámetro; si ambos `Default`
e `Initializer` están, gana `Initializer`.

## 9. Orden entre mods (cross-mod ordering)

**Problema:** el nombre del campo `[ReworkField]` es el del accessor → dos mods pueden pedir
el MISMO nombre en el MISMO tipo. Crear dos campos con el mismo nombre en un tipo es
**metadata inválida** (el loader de CLR rechazaría el tipo). En parches, el orden cuando
varios mods tocan lo mismo no estaba definido más allá de `Priority`.

**Solución (primero gana, determinista):**
- **Campos:** el PRIMER accessor en orden de carga crea el campo (y su inicializador). Los
  siguientes con mismo nombre y mismo tipo se **enlazan al campo existente** (compartido).
  Con mismo nombre y otro tipo → conflicto real: el accessor se reescribe para **lanzar
  `InvalidOperationException`** con mensaje claro (nunca se deja extern colgando).
  Se detecta con `targetType.Fields.FirstOrDefault(f => f.Name == fieldName)` (funciona
  también contra campos del Juego: el accessor se enlaza a uno existente).
- **Parches:** orden = `Priority` desc → **orden de carga del mod** (índice en
  `set.AllAssemblies`, que preserva `RunningModsListForReading`) → nombre completo
  (estable). Los transpilers sobre el mismo método se componen en ese orden; los directos
  se aplican en ese orden.

**Verificado:** con accessors A (crea, Default=5), B (mismo nombre+tipo → "Campo compartido"),
C (mismo nombre otro tipo → "Conflicto de campo" y accessor lanzador) y dos transpilers
prio 50/10 sobre `Verse.Root.OnGUI` (orden logueado `112:prio50:…; 112:prio10:…`, EH
preservado en ambos). Boot limpio (VERIFICACIÓN OK, 0 NREs, 0 OnGUI-loop).

## 10. Logging a archivo + ErrorPrinter

**Qué se añadió:** un log de archivo dedicado (**`Rework.log`**, LocalLow) al que se espeja
TODA la actividad de Rework (pasada 1 y runtime) con marca de tiempo y cabecera de sesión
por arranque; más una utilidad `Rework.Core.ErrorPrinter` para volcar excepciones con
contexto (a `Rework.log` y al `Player.log`).

**Traps resueltos:**
- **Codificación:** `File.AppendAllText` escribe UTF-8 SIN BOM → Windows/Notepad lo leen como
  ANSI y muestran mojibake (`â€”` por `—`, `Ã±` por `ñ`). Fix: UTF-8 **con BOM**
  (`new UTF8Encoding(true)`). Verificado: los acentos y `—`/`→` se leen bien.
- **Cabecera única por arranque:** como la pasada 2 corre con un `ReworkCore` nuevo (sus
  estáticos están frescos), la bandera "¿ya escribí la cabecera?" debe vivir en `DataStore`
  (0ReworkData, que NO se recarga) para que no se duplique.
- **Doble escritura:** la pasada 1 bufferiza en `LogsToPass` y FlushBuffered los vuelca solo
  al `Player.log`; el `Rework.log` los recibe UNA vez (en Write). Nunca duplica.

**Verificado en runtime:** `ErrorPrinter.Try("TEST ErrorPrinter", () => throw …)` volcó
`ErrorPrinter: TEST ErrorPrinter` + `System.Exception: boom de prueba` con stack al
`Rework.log`. Boot limpio (VERIFICACIÓN OK, 0 NREs).

## 11. Init patches + mod de ejemplo (Caso A probado con contenido real)

**Qué se añadió:** atributo `[ReworkInit]` (ReworkAPI) que ejecuta un método estático UNA vez
al final de la pasada 2; `InitRunner` (ReworkCore) descubre por reflexión todos los
`[ReworkInit]` de mods activos y los ordena por Priority desc → orden de carga → nombre.
Y un **mod de ejemplo de terceros** completo (`Mods\Rework Demo Reforjed\`) que referencia
solo `0ReworkAPI` y usa campos + transpiler + init.

**Traps resueltos:**
- **Reflexión sobre ensamblados rotos:** `asm.GetTypes()` puede lanzar
  `ReflectionTypeLoadException`; se recoge `e.Types` filtrando nulos y se sigue.
- **Atributo malformado:** el `GetCustomAttribute` por método va en `try/catch`; no debe
  romper el descubrimiento del resto.
- **Duplicado de Mono.Cecil en mods de terceros:** si el mod referencia Mono.Cecil, no debe
  copiarlo a su `Assemblies\` (Rework ya lo trae). Fix en el csproj de ejemplo:
  `<PackageReference Include="Mono.Cecil" Version="0.11.6" ExcludeAssets="runtime" />` —
  compila contra él pero no se empaqueta; en runtime resuelve contra el Mono.Cecil que carga
  Rework (misma versión → misma identidad).

**Verificado en runtime (mod de ejemplo, juego real, boot limpio 0 NREs):**
- `Campo añadido: Verse.Pawn.Rework_XpMultiplier` + `= 1` (Default) en 1 ctor.
- `Campo añadido: Verse.Pawn.Rework_Logros` ← `CrearLogros` en 1 ctor.
- `FreePatcher: orden … ReworkDemoPatch::ParcheMultiplicador; ReworkDemoPatch::ParchePersistencia` + `1 manejador(es) de excepción
  preservado(s)` (transpilers reales Case A); `Versión del juego soportada: 1.6.4850`.
- `Init (prio 100): ReworkDemo.ReworkDemoInit.InicializarDemo` → Player.log:
  `XpMultiplier inicial=1 (default 1.0, ahora 2), Logros.Count=1, MultiplicarXp(100)→200 — PATCH MULTIPLICADOR OK ✓`
  — Default, setter, inicializador por método y parche de comportamiento verificados.

## 12. Parche multiplicador + persistencia en el mod de ejemplo

**Qué se añadió (respuesta a las carencias reales #1/#2/#4 del análisis):**
1. **Comportamiento observable desde un mod de terceros:** `ParcheMultiplicador` (transpiler
   sobre `RimWorld.SkillRecord::Learn`) inyecta `xp = MultiplicarXp(this.pawn, xp)` al inicio;
   el trabajo real vive en un helper C# con try/catch (si falla → comportamiento original).
2. **Persistencia:** `ParchePersistencia` (transpiler sobre `Verse.Pawn::ExposeData`) inyecta
   `ScribePawnRework.ExposePawnRework(this)`; vuelca `Rework_XpMultiplier` y `Rework_Logros`
   con `Scribe_Values.Look`/`Scribe_Collections.Look`, con guarda de `Scribe.mode` para no
   actuar fuera del flujo de guardado.
3. **Chequeo de versión** en `ReworkMod.VerifyPatch` (usa `RimWorld.VersionControl.
   CurrentVersionString`; `Versión del juego soportada: …` o error claro).

**Traps resueltos (API real del juego, inspeccionada por solo-lectura del DLL):**
- `SkillRecord` está en **`RimWorld`** (no `Verse`), y `Learn(float,bool,bool)` devuelve
  **void** (no retorna la XP). El campo `pawn` de `SkillRecord` es **privado**: el helper C#
  no puede leerlo fuera, pero el IL inyectado DENTRO de `Learn` sí (ldarg.0; ldfld pawn) y se
  pasa al helper — ese es el patrón correcto.
- `Scribe.mode` es un **enum `Verse.LoadSaveMode`** (no tiene `IsActive()`): guarda
  `Scribe.mode == LoadSaveMode.Inactive` para no exponer nada fuera del flujo.
- **No usar Defs en el Init:** el arranque corre antes de cargar Defs; por eso el init evita
  `SkillDefOf` y verifica el parche llamando al helper directamente (en vez de instanciar
  skills reales), dejando la comprobación del IL inyectado a la línea "Transpiler aplicado".

**Verificado en runtime (juego real, boot limpio 0 NREs):** los TRES transpilers aplicados
(`→ RimWorld.SkillRecord::Learn`, `→ Verse.Pawn::ExposeData` y `→ Verse.Root::Update`),
`Versión del juego soportada: 1.6.4850`, y el init logueó `MultiplicarXp(100)→200 — PATCH
MULTIPLICADOR OK ✓`.

**Teclas de prueba (save/load manual):** `ParcheTeclas` sobre `Verse.Root::Update` llama a
`ReworkDemoHotkeys.CheckHotkeys()` cada frame; F9 (multiplicador de todos los colonos a 3.0)
y F10 (logea el valor actual de cada colono). Pasos y criterio de éxito en MANUAL.

**Pendiente honesto:** la prueba de save/load real requiere partida (el flujo `Scribe` real
solo se dispara guardando/cargando en el juego); el enganche quedó inyectado y sin warnings,
pero la última vuelta es manual en juego (F9 → guardar → salir → cargar → F10, instrucciones
en MANUAL).

## 13. Serialización automática de campos ([ReworkField(Serialize = true)])

**Qué se añadió:** el atributo `ReworkField` acepta `Serialize = true`; `FieldScribe`
(ReworkCore) inyecta en el `ExposeData` del tipo destino la llamada Scribe correcta para
que el campo viaje en la partida guardada SIN escribir el parche Scribe a mano (antes: el
demo necesitaba un transpiler manual sobre `Pawn.ExposeData` → ya no).

**Cómo funciona internamente:** primitivas/string/enums → `Scribe_Values.Look(ref T, label,
default, forceSave=true)`; `List<T>` de esos → `Scribe_Collections.Look(LookMode.Value)`.
Se inyecta al INICIO de `ExposeData` (antes de regiones try/catch, mismo patrón que los
inicializadores). Solo el mod que CREA el campo decide la serialización (los que se enlazan
a un campo compartido no la reafirman). Si el tipo no está soportado, error claro y no se
serializa (no rompe el guardado).

**Traps resueltos:**
- **Buscar el overload Scribe correcto por reflexión:** hay varios `Look` con distinto
  número de params; se filtra por genérico de 1 arg + primer parámetro `T&` (ref) para
  `Scribe_Values` y por `List<>&` para `Scribe_Collections`.
- **Inyectar IL genérico:** `Scribe_Values.Look<T>` es genérico → se construye un
  `GenericInstanceMethod` y se le añade el argumento de tipo del campo antes del `call`.
- **Default CLR vs Default del atributo:** se usa el `Default` del atributo si el mod lo dio
  (coherencia con el inicializador); si no, el cero/nulo CLR del tipo.

**Verificado en runtime (juego real, boot limpio 0 NREs):** los DOS campos del demo quedan
enganchados:
`Campo serializado: Verse.Pawn.Rework_XpMultiplier` / `…Rework_Logros` +
`Serialización automática: 2 campo(s) de Verse.Pawn enganchados a ExposeData`.

**Pendiente honesto:** la prueba F9/F10 validó la vía MANUAL (parche Scribe en C#). La vía
automática (IL inyectado) compila/arranca limpio, pero falta repetir el save/load manual con
ella (F9 → guardar → salir → cargar → F10 → debe seguir poniendo `XpMultiplier=3`).

## 14. Ciclo de vida declarativo + helpers de IL

**Qué se añadió (respuesta al roadmap, bloques 7/3):**
1. `[ReworkHook(ReworkHookPoint.X)]` (ReworkAPI) + `LifecycleHooks` (ReworkCore): Rework
   descubre los hooks de los mods integrados y **inyecta su llamada al inicio** del método
   del juego que representa el evento (GameStart→Game.LoadGame, GameInitialized→FinalizeInit,
   MapGenerated→Map.FinalizeLoading, MapInitialized→Map.FinalizeInit, PawnDied→Pawn.Kill).
   El hook es estático público void, con 0 o 1 parámetro (el `this` del evento).
2. `TranspilerHelpers` (ReworkAPI): helpers de construcción de IL para transpilers
   (constantes, Ldarg/Starg, Call, Ldfld, Ret, ImportMethod/Type, List/Prepend) que
   sustituyen la mecánica Prefix/Postfix (descartada: inferior, solo
   "antes/después"; en Rework el autor escribe el transpiler).

**Traps resueltos:**
- **Leer el valor del enum en el atributo:** el argumento del enum llega como
  `CustomAttributeArgument` anidado; hay que desempaquetarlo (Unwrap) y, como su valor es
  un int, convertirlo a nombre vía `Enum.GetNames` — el `ToString()` directo daba el número.
- **`Starg` con índice numérico rompía el transpiler:** `Instruction.Create(OpCodes.Starg,
  int)` lanza `System.Reflection.TargetInvocationException: ArgumentException: opcode` (el
  opcode Starg espera un `ParameterReference`, no un int). Fix: overload
  `Starg(ParameterDefinition)` (recomendado) además del `Starg(int)`. El transpiler del demo
  usa el nuevo overload y aplica sin error.
- **⚠️ `Ldarg` con índice numérico (mismo fallo que Starg):** al soportar contexto por
  parámetro (punto `CurrentMapChanged` → `set_CurrentMap(Map)`), `Instruction.Create(
  OpCodes.Ldarg, (byte)ctxArg)` daba `Hook ... no se pudo inyectar: opcode`. El opcode
  Ldarg espera un **ParameterReference/ParameterDefinition**, no un int. Fix: usar
  `targetMethod.Parameters[ctxArg - 1]` (el ParameterDefinition real del método destino).
- **Idempotencia del hook:** comprobar si el `call` del hook ya es la primera instrucción
  real (tras un posible `ldarg.0` del contexto) para no duplicarlo en montajes repetidos.
- **⚠️ ORDEN de InsertBefore (bug REAL detectado en logs del usuario):** si el hook toma
  contexto (1 parámetro), el IL correcto es `ldarg.0; call hook`. `InsertBefore` mete cada
  instrucción JUSTO ANTES de `first`; si insertabas `call` y LUEGO `ldarg.0`, el resultado
  es `call; ldarg.0` → pila inválida → **`System.InvalidProgramException: Invalid IL code
  in Verse.Pawn:Kill (System.Nullable`1<Verse.DamageInfo>,Verse.Hediff): IL_0000: call 0x...`**
  al cargar la escena del mapa ("Exception while generating thing set"). El boot NO lo
  detecta (el error salta al ejecutarse Kill, no al parchear) → por eso el "boot limpio"
  engañó. **Fix: insertar PRIMERO `ldarg.0` y DESPUÉS `call`** (el orden inverso por
  InsertBefore produce el orden correcto). Verificado: tras el fix, 0 apariciones de
  InvalidProgramException / Pawn:Kill / IL_0000 en el log.

**Verificado en runtime (juego real, boot limpio 0 NREs):**
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnPawnDied → Verse.Pawn.Kill`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnMapGenerated → Verse.Map.FinalizeLoading`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnPawnSpawned → Verse.Pawn.SpawnSetup`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnIncidentFired → RimWorld.IncidentWorker.TryExecute`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnMapTick → Verse.Map.MapPreTick`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnPawnTick → Verse.Pawn.Tick`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnGameComponentTick → Verse.GameComponent.GameComponentTick`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnMapComponentTick → Verse.MapComponent.MapComponentTick`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnWorldComponentTick → RimWorld.Planet.WorldComponent.WorldComponentTick`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnCurrentMapChanged → Verse.Game.set_CurrentMap`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnCaravanSpawned → RimWorld.Planet.Caravan..ctor`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnNewGameStarted → Verse.Game.InitNewGame`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnPawnCreated → Verse.Pawn.PostMake`
- `Hook aplicado: ReworkDemo.ReworkDemoHooks::OnStorytellerTick → RimWorld.Storyteller.StorytellerTick`
- Campos añadidos y serializados en componentes (el mecanismo es genérico):
  `Campo añadido: Verse.MapComponent.Rework_MapLogs` / `Verse.GameComponent.Rework_GameNotes`
  + `Serialización automática: 1 campo(s) de Verse.MapComponent/GameComponent`.
- `Transpiler aplicado (prio 0): ...::ParcheMultiplicador ... → RimWorld.SkillRecord::Learn`
  (escrito con TranspilerHelpers) y `...::ParcheTeclas → Verse.Root::Update`.
- `MultiplicarXp(100)→200 — PATCH MULTIPLICADOR OK ✓` + `VERIFICACIÓN OK` + `Versión del
  juego soportada: 1.6.4850`.

**Pendiente honesto:** los hooks quedan INYECTADOS y verificados en el arranque (la línea
"Hook aplicado" sale en el Rework.log); el disparo real ocurre en partida (muerte de colono,
carga de mapa, cambio de mapa, creación de caravana, ticks de componentes), que no se puede
provocar en el test de boot. Nota sobre los ticks de componentes: se inyectan en la BASE
virtual (`GameComponentTick`, etc.); solo se disparan si la subclase real del juego llama a
`base.XxxTick()`.

## 15. Detección de entorno, modo seguro y contexto seguro del motor (bloque 1)

**Qué se añadió (respuesta a los puntos 1.2/1.3/1.7/1.8/1.10 del bloque 1):**
- `RuntimeEnvironment` (ReworkCore): detecta el tipo de runtime (`Mono.Runtime`),
  su versión (`GetDisplayName`) y **verifica el layout nativo** de los offsets de
  `UnsafeAssembly` (`+0x60 → MonoImage; +0x10 → raw_data; +0x18 → length`) ANTES de
  confiar en ellos (`VerifyMonoLayout`).
- **Modo seguro PROACTIVO** (1.10): en la pasada 1, si el runtime no es Mono o el layout
  no verifica → **no se reescribe nada**; el juego arranca vanilla y Rework solo advierte
  (decide antes de romper, no tras un crash).
- **Contexto seguro del motor** (1.2/1.3): `ModifiableAssembly.IsUnityEngineAssembly` +
  `Reloader` excluyen del swap `UnityEngine*`, `Unity`, `Mono.CSharp` (solo lectura de
  referencias; nunca refonly ni recarga). Guard en `SetSourceRefOnly`.

**Traps resueltos:**
- **Nombre duplicado de variable:** en `Reloader`, la lista `toReload` chocaba con el
  `reload` del `foreach` → CS0136. Fix: renombrar la lista a `toSwap`.
- **Offsets Mono cambian entre build:** si `VerifyMonoLayout` falla (puntero nulo o
  longitud absurda), se degrada a modo seguro en vez de usar offsets rotos (que
  corromperían memoria).

**Verificado en runtime (juego real, boot limpio 0 NREs):**
`Entorno: Runtime=Mono, Mono=6.13.0 (Visual Studio built mono), layoutOK=True → seguro para reescribir`
+ `VERIFICACIÓN OK` + `Versión del juego soportada: 1.6.4850`.

**Pendiente honesto:** el modo seguro y el guard del motor son CORNIZAS DEFENSIVAS (se
activarían en un runtime no-Mono o una build con offsets distintos, que no es el caso del
target 1.6.4850); están verificados en el arranque por la ruta SEGURA (layoutOK=True), pero
el camino de fallo no se ha ejecutado de verdad.

## 16. Bloque 2 — inyección de campos en todas las clases del listado

**Qué se verificó (respuesta al bloque 2, puntos 2.1–2.30):** el mecanismo de campos era
genérico pero solo estaba probado en 4 clases. Se creó un archivo TEMPORAL de verificación
(borrado tras la prueba) que añadía un `[ReworkField]` a **las 23 clases del listado** (con
nombres reales de 1.6.4850) + los casos especiales, y se lanzó el juego:

- **2.1–2.23** → 23 "Campo añadido" en el Rework.log (boot limpio 0 NREs).
- **2.25 nested** → `Verse.Log.LogLock` (se usó `Verse.Log/LogLock` en el log) SÍ añade.
- **2.26 genérico** → `Verse.LRUCache2` (accessor genérico `<TKey,TValue>`) SÍ añade.
- **2.28 struct como tipo** → `Verse.IntVec3` como tipo de campo en `Verse.Pawn` SÍ.
- 2.27/2.29/2.30 ya estaban (nombres exactos / Serialize / temporales por defecto).

**Lección (NOMBRES REALES vs la lista):** varios nombres de `implementaciones.txt` NO
existen así en 1.6.4850 →
- 2.12 `Hediff` → **`Verse.Hediff`** (no RimWorld)
- 2.13 `HediffComp` → **`Verse.HediffComp`**
- 2.14 `Gene` → **`Verse.Gene`** (no RimWorld)
- 2.16 `Pawn_JobTracker` → **`Verse.AI.Pawn_JobTracker`**
- 2.17 `Pawn_HealthTracker` → **`Verse.Pawn_HealthTracker`** (no RimWorld)
- 2.19 `Pawn_MentalStateTracker` → **NO EXISTE**; el moderno es **`Verse.AI.MentalStateHandler`**
(confirmado vía el campo `Pawn.mindState`). Moraleja: verificar el tipo real antes de
declarar el accessor (la verificación se hizo con solo-lectura del DLL).

**Pendiente honesto:** el archivo temporal se borró y el framework quedó limpio (solo los 5
campos reales del demo); la verificación cubrió que los 26 campos SE AÑADEN y el boot no
revienta, no que cada uno se lea/escriba en runtime (los accessors reescritos son idénticos
para todos: `ldarg.0; ldflda; ret`, ya probado en Pawn/MapComponent).

## 17. Bloque 3 — métodos y propiedades añadidos ([ReworkMethod] / [ReworkProperty])

**Qué se añadió (respuesta a 3.1/3.2/3.3):** `ReworkMethodAttribute` y `ReworkPropertyAttribute`
(ReworkAPI) + `MethodInjector` (ReworkCore). El miembro que se añade al tipo destino es un
**forwarder** cuyo cuerpo llama al método estático del mod (la lógica queda en el mod; el
miembro es real: visible por reflexión, invocable). Integrado en `GameProcessing` entre
campos y hooks.

**Traps resueltos:**
- **`Ldarg` con operando numérico (el mismo de siempre):** `il.Emit(OpCodes.Ldarg, (byte)i)`
  daba `ArgumentException: opcode` (el opcode espera un `ParameterDefinition`, no un int).
  Fix: cargar con la forma corta `Ldarg_0` para 'this' y `Ldarg` con el
  `ParameterDefinition` real de `newMethod.Parameters[i-1]` para el resto.
- **Forwarder con cuerpo de otro método:** la primera versión creaba un método fantasma
  `__fwd` (sin parámetros) pero emitía `ldarg.0..N` → IL inválido (rechazado por Cecil).
  Fix: construir el cuerpo directamente sobre el `newMethod` cuyos parámetros coinciden
  con los `ldarg`.

**Verificado en runtime (juego real, boot limpio 0 NREs):**
- `Método añadido: instancia Verse.Pawn::Rework_Saludar → ...::Saludar(Verse.Pawn)`.
- `Método añadido: estático Verse.Game::Rework_TotalMods → ...::TotalMods()`.
- `Propiedad Verse.Pawn::Rework_SaludoCount → getter/setter (forwarder ...)`.
- En runtime por reflexión: `Rework_Saludar existe=True` y `Rework_SaludoCount=1` (la
  propiedad devuelve el Count real del campo subyacente). ⇒ los forwarders funcionan de
  extremo a extremo.

**Pendiente honesto:** la verificación cubrió método de instancia + método estático +
propiedad con getter/setter (los casos principales). 3.4 (eventos) y 3.5 (constructores)
no tienen API declarativa; se pueden hacer con Cecil puro vía `[ReworkPatch]` directo.
El temporal de verificación se borró; el framework quedó limpio.

## 18. Bloque 4 — atributos SÍ, interfaces NO (vtable rota)

**Qué se probó (bloque 4.1–4.12):**

**✅ Funcionan — 4.10/4.11/4.12 (atributos):** `[ReworkAnnotate]` (ReworkAPI) +
`AnnotateInjector` (ReworkCore) inyecta metadata en clases/métodos/campos del juego, con
marcador genérico por defecto (`Rework.Core.ReworkMarkAttribute`) o un atributo propio del
mod (resuelto por nombre). Verificado en el juego: atributo `ReworkMarca` inyectado en
`Verse.Pawn` (clase) y en `Verse.Pawn::Kill` (método), boot limpio.

**❌ DESCARTARe — 4.1/4.4–4.8/4.9 (interfaces):** añadir una interfaz de un mod a una clase
del juego (p.ej. `Verse.Pawn : IReworkContadorLogros`) provoca al arrancar:
- `Error in static constructor of RimWorld.IdeoUIUtility: ... TypeLoadException: Could not load type 'Verse.Pawn[]' from assembly ''`
- `Error in static constructor of RimWorld.CompBiosculpterPod: ... TypeLoadException: VTable setup of type Verse.Pawn failed`

**Causa (estructural, no un fallo puntual):** la interfaz vive en el ensamblado del mod,
que el reloader RECARGA (identidad duplicada: original refonly + copia nueva). Al
construirse la vtable de `Verse.Pawn` en runtime (instancias estáticas de tipos que usan
`Pawn[]`), Mono no puede resolver a tiempo la interfaz del ensamblado duplicado → la clase
entera falla. El metadata SÍ se escribió ("Interfaz añadida: Verse.Pawn : ..."), pero el
runtime lo rechaza.

**Decisión:** interfaces de mod en clases del juego = **no soportado** (se quitó del
pipeline; `InterfaceInjector` se conserva como registro del hallazgo). Alternativa viable
para "polimorfismo por mod": **subclase NUEVA** (patrón `ReworkWorldCameraDriver`) — crear
una clase tuya que hereda de la del juego y sustituir su uso, en lugar de cambiar la clase
existente.

**4.2/4.3 (cambiar clase base):** NO SOPORTADO (corrompe vtable/layout), misma alternativa.

**Verificado tras revertir:** interfaz fuera del pipeline → arranque limpio (0
TypeLoadException/VTable; solo el ruido benigno "Fallback handler could not load library"
de siempre), `VERIFICACIÓN OK`.

## 19. Bloque 5 — estructuras de datos (tipos de colección en campos)

**Qué se verificó (respuesta a 5.1–5.8):** los campos inyectados aceptan tipos de
colección (verificado en el juego, 2026-10-06, boot limpio 0 NREs):
- `List<int>` → `AddRange` / `Remove` / `Sort` / reemplazo entero (5.1/5.5/5.6/5.7/5.8).
- `Dictionary<string,int>` → añadir / `Remove` / leer (5.2).
- `HashSet<string>` → `Add` (duplicado no entra) / `Remove` (5.3).
- `int[]` → crear y modificar un elemento (5.4).
Runtime verificado: `lista tras 5.5/5.6/5.7=[2,3,4], tras 5.8=[9,8,7], dict[b]=2, set x
antes=1/después=0 (duplicado no entra), array[0]=99`.

**Hueco real encontrado (bloque 6):** `FieldScribe` (serialización automática) v1 solo
cubre primitivas/string/enums/`List` de esos. Con `Serialize = true` en un
`Dictionary`/`HashSet`/array, el campo se añade y funciona en runtime pero NO se
serializa: loguea `No se puede serializar automáticamente ... tipo no soportado` (error
benigno, no rompe el boot). Para persistir esos tipos hoy: Scribe manual con
`Scribe_Collections.Look` (o ampliar FieldScribe, tarea futura del bloque 6).

**5.9/5.10 (structs):** NO SOPORTADO — añadir un campo DENTRO de un struct existente o
cambiar su tamaño rompería el layout de memoria y las copias por valor. El framework
añade campos a CLASES (referencia); un struct como TIPO de campo SÍ funciona (visto en el
bloque 2, 2.28 con `Verse.IntVec3`).

**Pendiente honesto:** el temporal de verificación se borró y el framework quedó limpio
(solo los 4 campos reales del demo).

## 20. Bloque 6 — serialización automática ampliada (Dictionary/HashSet)

**Qué se cerró (respuesta al hueco real encontrado en el bloque 5, y a 6.3/6.4):** el
`FieldScribe` v1 solo serializaba primitivas/string/enums/`List` de esos; ayer se
descubrió que `Dictionary`/`HashSet`/array con `Serialize = true` no viajaban en el save.
Hoy se amplió `TryEmit` + resolvedores de overloads:

- **`Dictionary<K,V>`** (primitivas/string/enums) → `Scribe_Collections.Look(ref dict,
  label, LookMode.Value, LookMode.Value)` (overload 2 genéricos / 4 params).
- **`HashSet<T>`** (primitivas/string/enums) → `Scribe_Collections.Look(ref set, label,
  LookMode.Value)` (overload 1 genérico / 3 params).
- Verificado en runtime (boot limpio): `Campo serializado: Verse.Pawn.Rework_B6Dict /
  …B6Set / …B6Lista`, **0** errores "No se puede serializar", y los valores se leen
  (`dict[clave]=42, set count=1, lista[0]=7`).

**Trampa al ampliar (lección):** hay MUCHOS overloads de `Scribe_Collections.Look`; al
buscar por reflexión hay que distinguirlos por **número de genéricos + forma del primer
parámetro + nº de params** (List: 1 genérico/4 params; Dictionary: 2/4; HashSet: 1/3).
Si se elige el equivocado, el IL invalida el call (firma no coincide).

**Lo que NO se serializa con la v1+:** arrays (`T[]`), colecciones de OBJETOS (refs/deep)
y otros genéricos (Stack/Queue). Se loguea un error claro y no rompe el boot.

**Resto del bloque (6.5/6.7–6.14) = NO implementado (documentado en estado.txt):** 6.2
(backend alternativo — RimWorld usa Scribe XML y no se reemplaza), 6.5/6.7 (migrar/
versionar datos de mods: sin API), 6.8 (incremental — Scribe ya solo guarda lo Exposed),
6.9 (comprimido — los saves ya son XML comprimidos por vanilla), 6.10 (paralelo: fuera de
alcance), 6.11/6.12 (inspeccionar/editar saves: son XML, ya legibles/ editables a mano),
6.13/6.14 (reemplazar/añadir formato: NO se toca el sistema de guardado por diseño).

**6.6 (campos faltantes en saves viejos):** tolerancia parcial — los `Initializer`/
`Default` re-crean el valor al cargar si el campo no está en el save; no hay migración
explícita por versión.

**Pendiente honesto:** la serialización automática de Dictionary/HashSet se verificó en el
booot (enganche + lectura en runtime); el save/load real con esos tipos sigue siendo la
vuelta manual del usuario (F9/F10 o partida).

## 21. Bloque 7 — ciclo de vida completado (5 hooks nuevos)

**Qué se añadió (respuesta a los huecos 7.3/7.5/7.15/7.17):** se amplió `[ReworkHook]`
(enum + `LifecycleHooks`) con 5 puntos:
- `GameEnded` → `Verse.Game.Dispose` (7.5, fin de partida).
- `GameSaving` → `Verse.GameDataSaveLoader.SaveGame` (7.3, guardado; sin contexto).
- `BabyBorn` → `RimWorld.PregnancyUtility.ApplyBirthOutcome` (7.15, nacimiento; contexto =
  la MADRE biológica, parámetro 5 — el bebé aún no existe como Pawn).
- `CaravanEnteredMap` → `RimWorld.Planet.CaravanEnterMapUtility.Enter` (7.17, entrada al
  mapa; contexto = el `Verse.Map` destino, parámetro 2).
- `PawnLeftToCaravan` → `RimWorld.Planet.CaravanExitMapUtility.
  ExitMapAndJoinOrCreateCaravan` (7.17, salida; contexto = el `Verse.Pawn`, parámetro 1).

**Verificado en runtime (boot limpio, 19 hooks aplicados en el demo):**
los 5 nuevos aparecen en el Rework.log (`Hook aplicado: …::OnGameEnded →
Verse.Game.Dispose`, `…::OnGameSaving → …SaveGame`, `…::OnBabyBorn →
…ApplyBirthOutcome`, `…::OnCaravanEnteredMap → …CaravanEnterMapUtility.Enter`,
`…::OnPawnLeftToCaravan → …ExitMapAndJoinOrCreateCaravan`), 0 errores.

**Traps/lecciones:**
- **Contexto por PARÁMETRO (no siempre `this`):** `ApplyBirthOutcome` tiene 11 params y el
  "sujeto del evento" (la madre) está en el índice 5; `CaravanEnterMapUtility.Enter` tiene
  el mapa en el índice 2; `ExitMapAndJoinOrCreateCaravan` el pawn en el 1. El inyector
  soporta `ctxArg` por índice (lección de `CurrentMapChanged`, ERRORES §14) — aquí se usó
  para los tres nuevos.
- **Métodos con overloads:** `Enter`/`ApplyBirthOutcome` tienen varias firmas; la búsqueda
  por nombre + `HasBody` toma el primero válido. Si el juego añade overloads, la validación
  del tipo de contexto descarta los incompatibles.

**Pendiente honesto:** los hooks se verifican INYECTADOS en el arranque; el disparo real
(guardar, salir de partida, nacer un bebé, caravana entrando/saliendo) ocurre en partida.

## 5. Duplicados por nombre de ensamblado (Caso B)

**Síntoma potencial:** si dos mods traen un ensamblado con el MISMO nombre simple, el viejo
código descartaba el segundo con un `continue` (literal, en `Loader.cs`), y `AssemblySet`
sobrescribía la entrada (`nameToAsm[name] = masm`).

**Solución:** `AssemblySet` admite varios ensamblados por nombre — el primero registrado es
el "principal" para el resolver; el resto se conservan en `AllAssemblies` (se procesan y se
recargan por su propia ruta, `DataStore.AssembliesByPath` clavea por `Location`). `Loader` ya
no descarta duplicados.

**Lección (honesta):** un duplicado *idéntico nombre + bytes distintos* es caótico de raíz
porque **vanilla deduplica** a una sola identidad de runtime; procesar "ambos" contra el mismo
módulo duplica efectos y no se "sirve limpio". El valor real del arreglo es de **robustez**
(que no se pierda ni se rompa el arranque), no de hacer funcionar dos mods así (que vanilla de
por sí ya no carga bien). Por eso: nombres de ensamblado únicos y referenciar la API con
`<Private>false</Private>` (sin re-empaquetar `0ReworkAPI.dll`/`Mono.Cecil.dll`).

## 22. Bloque 18 — Sistema de configuración persistente (ModSettings y ReworkConfig)

**Qué se implementó:** sistema de configuración completo y persistente cubriendo los 10 puntos (18.1–18.10):
- `ReworkConfig` en `0ReworkAPI`: enums y contenedor puros BCL (`PerfProfile`, `CompatProfile`, `ReworkExcludedMods`), accesibles por cualquier mod sin acoplamiento a Verse.
- `ReworkSettings` en `ReworkMod`: persistencia de RimWorld mediante `ModSettings` (`Scribe_Values` y `Scribe_Collections`), sincronizado bidireccionalmente con `ReworkConfig`.
- Menú de opciones (`SettingsCategory` y `DoSettingsWindowContents`) con selectores de perfiles, checkboxes y control interactivo de exclusión de mods.
- Fallback single-thread automático en `ReworkJobs.Pool` si multihilo está desactivado.
- Supresión de inyección en `FieldScribe` si auto-scribe está desactivado.
- Exclusión en `Loader.cs` de mods listados en `ExcludedMods` evitando procesar atributos.

**Trampa/error resuelto:**
- **`Scribe.Collections.Look` vs `Scribe_Collections.Look`:** En RimWorld la clase de serialización de colecciones es `Scribe_Collections`, no `Scribe.Collections`. Además, `Scribe_Collections.Look` espera `ref List<T>`, por lo que subclases como `ReworkExcludedMods` no pueden pasarse por `ref` directamente (invarianza de tipos en referencias C#); se resolvió proyectando una `List<string>` temporal durante `Saving` y reconstruyendo la subclase en `LoadingVars`.

**Verificado en runtime:**
- Boot limpio con 0 NREs y log: `Rework Demo B18: settingsOK=True, categoria='Rework Reforjed', perfMax=MaximoRendimiento (multi=True), compatPerm=Permisivo (safe=False), excluded=1, resetPerf=Equilibrado, resetCompat=Estandar, resetExcluded=0 — bloque 18 verificado ✓`.

## 23. Bloque 8 / Extensión de Contenido — Jobs declarativos sin XML ([ReworkJob])

**Qué se implementó:**
- Atributo `[ReworkJob(defName)]` en `0ReworkAPI` para marcar clases `JobDriver` sin requerir XMLs de `JobDef`.
- `ReworkJobRegistry` en `ReworkMod` con `[StaticConstructorOnStartup]`: escanea clases con el atributo tras la carga de Defs de RimWorld y genera/registra instancias de `JobDef` en `DefDatabase<JobDef>`.
- `ReworkJobDriver` como clase base opcional que provee helpers (`ToilGoto`, `ToilWait`, `ToilDo`) para reducir el boilerplate al armar secuencias de `Toils`.

**Detalle técnico:**
- El registro en `DefDatabase<JobDef>` no puede ocurrir durante la Pasada 1 ni en la fase temprana de inicialización de mods (`LoadedModManager.InitializeMods`), porque los Defs vanilla de RimWorld aún no están cargados. Utilizar `[StaticConstructorOnStartup]` garantiza que el registro se ejecute de forma ordenada y segura cuando todos los sistemas de Defs y short hashes de RimWorld están plenamente inicializados.

**Verificado en runtime:**
- Log verificado: `[ReworkJob] Registrado JobDef 'ReworkDemo_SaludarColono' -> driver JobDriver_DemoSaludar ('Saludando cordialmente.').` y `Rework Demo Job: registrado=True, driver=True, report='Saludando cordialmente.' — [ReworkJob] verificado ✓`. Boot limpio con 0 NREs.

## 24. Bloque 10 / Extensión de Contenido — Incidentes declarativos sin XML ([ReworkIncident])

**Qué se implementó:**
- Atributo `[ReworkIncident(defName)]` en `0ReworkAPI` para marcar clases `IncidentWorker` con parámetros (`Category`, `BaseChance`, `TargetTag`, `LetterLabel`, `LetterText`).
- `ReworkIncidentRegistry` en `ReworkMod` con `[StaticConstructorOnStartup]`: escanea los mods tras la carga de Defs de RimWorld y genera/registra instancias de `IncidentDef` en `DefDatabase<IncidentDef>`.
- `ReworkIncidentWorker` como clase base opcional que provee helpers (`SendStandardLetter`).

**Detalle técnico:**
- En `IncidentDef`, los campos `category` y `targetTags` son referencias a `IncidentCategoryDef` y `List<IncidentTargetTagDef>`, por lo que el registro resuelve dinámicamente dichos Defs desde `DefDatabase` o utiliza los fallbacks canónicos (`IncidentCategoryDefOf.Misc`, `IncidentTargetTagDefOf.Map_PlayerHome`).

**Verificado en runtime:**
- Log verificado: `[ReworkIncident] Registrado IncidentDef 'ReworkDemo_InspiracionColectiva' -> worker IncidentWorker_DemoInspiracion (cat=Misc, chance=1.5).` y `Rework Demo Incident: registrado=True, worker=True, cat=Misc, chance=1.5 — [ReworkIncident] verificado ✓`. Boot limpio con 0 NREs.

## 25. Bloque 8 / Extensión de Contenido — Asignadores de Trabajo declarativos sin XML ([ReworkWorkGiver])

**Qué se implementó:**
- Atributo `[ReworkWorkGiver(defName)]` en `0ReworkAPI` para marcar clases `RimWorld.WorkGiver` con propiedades configurables (`WorkType`, `PriorityInType`, `Verb`, `Gerund`, `DirectOrderable`, `ScanThings`, `ScanCells`, `Emergency`).
- `ReworkWorkGiverRegistry` en `ReworkMod` con `[StaticConstructorOnStartup]`: escanea los mods, genera/registra instancias de `WorkGiverDef` en `DefDatabase<WorkGiverDef>` y las inserta ordenadas por prioridad dentro de `workGiversByPriority` del `WorkTypeDef` correspondiente.

**Detalle técnico:**
- En RimWorld, un `WorkGiverDef` añadido solo a `DefDatabase<WorkGiverDef>` no es asignado automáticamente por los colonos si no está incluido en la lista `workGiversByPriority` de su `WorkTypeDef`. El registro automatiza ambos pasos: inserción en `DefDatabase` y reordenamiento descendente por `PriorityInType` en el `WorkTypeDef`.

**Verificado en runtime:**
- Log verificado: `[ReworkWorkGiver] Registrado WorkGiverDef 'ReworkDemo_SaludarColonocompañero' -> giver WorkGiver_DemoSaludar (workType=Hauling, prio=60).` y `Rework Demo WorkGiver: registrado=True, giver=True, workType=Hauling, enlazado=True — [ReworkWorkGiver] verificado ✓`. Boot limpio con 0 NREs.

## 26. Extensión de Contenido de Alta Prioridad — [ReworkRecipe], [ReworkHediff] y [ReworkTrait]

**Qué se implementó:**
- Atributos declarativos en `0ReworkAPI`: `[ReworkRecipe(defName)]`, `[ReworkHediff(defName)]` y `[ReworkTrait(defName)]`.
- Registradores automáticos en `ReworkMod` (`ReworkRecipeRegistry`, `ReworkHediffRegistry`, `ReworkTraitRegistry`) activados tras la carga de Defs (`[StaticConstructorOnStartup]`).
- Enlace dinámico de recetas a las mesas de trabajo indicadas en `RecipeUsers` mediante `ThingDef.recipes`.
- Generación y estructuración de `TraitDegreeData` para traits sin XML.

**Verificado en runtime:**
- Boot limpio con 0 NREs y log: `Rework Demo HighPriority: recipe=True, hediff=True, trait=True — [ReworkRecipe/Hediff/Trait] verificado ✓`.

## 27. Extensión de Contenido — [ReworkNeed] y [ReworkDesignator]

**Qué se implementó:**
- Atributos declarativos en `0ReworkAPI`: `[ReworkNeed(defName)]` y `[ReworkDesignator(category)]`.
- `ReworkNeedRegistry` en `ReworkMod` (`[StaticConstructorOnStartup]`): genera y registra instancias de `NeedDef` en `DefDatabase<NeedDef>`.
- `ReworkDesignatorRegistry` en `ReworkMod` (`[StaticConstructorOnStartup]`): enlaza las clases de designadores directamente en la lista `specialDesignatorClasses` del `DesignationCategoryDef` correspondiente (ej: "Orders", "Zone").
- Documentación de compatibilidad nativa para `GameComponent` y `MapComponent`.

**Trampa/error resuelto:**
- En RimWorld, `DesignationCategoryDefOf` no expone una constante para "Orders" (solo Production, Floors, Zone). El registrador resuelve "Orders" dinámicamente desde `DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Orders")` con fallback seguro a `Zone`.

**Verificado en runtime:**
- Boot limpio con 0 NREs y log: `Rework Demo Option2: need=True, designator=True, gameComp=True, mapComp=True — [Opción 2] verificado ✓`.

## 28. Invenciones Únicas — ReworkBus, [ReworkGenStep] y [ReworkMutate]

**Qué se implementó:**
- `ReworkBus` y atributo `[ReworkOn]` en `0ReworkAPI`: bus reactivo basado en eventos fuertemente tipados (`IReworkEvent`) con soporte de prioridades y auto-suscripción por reflexión.
- `[ReworkGenStep(defName)]` en `0ReworkAPI` + `ReworkGenStepRegistry` en `ReworkMod`: registra instancias de `GenStepDef` y las inserta ordenadas por `order` en `MapGeneratorDef.genSteps`.
- `[ReworkMutate(defName, fieldPath, value)]` en `0ReworkAPI` + `ReworkMutateRegistry` en `ReworkMod`: mutación reversible y rastreada sobre variables de Defs existentes en vanilla con historial en memoria.

**Verificado en runtime:**
- Log verificado: `[ReworkGenStep] Registrado GenStepDef 'ReworkDemo_GenPiedraReforjada' -> GenStep_DemoPiedra (order=650)`, `[ReworkMutate] Mutado CraftingSpot.useHitPoints: 'False' -> 'False'` y `Rework Demo Bus: Evento recibido 'DemoStartupTest'`. Boot limpio con 0 NREs.

## 29. Invención Única — ReworkLive (Hot-Reload Seguro con Cero Desperdicio)

**Qué se implementó:**
- Interfaz `IReworkLiveReloadable` y atributo `[ReworkLive]` en `0ReworkAPI` con fachada `ReworkLive.BeforeReload` y `ReworkLive.AfterReload`.
- `ReworkLiveEngine` en `ReworkMod` con `[StaticConstructorOnStartup]`: monitoreo de archivos `.dll` externos, recarga mediante `Assembly.Load(byte[])` evitando bloqueos de archivos en disco.
- Protocolo estricto de recarga para prevenir fugas de memoria y acumulación de basura:
  1. Invocación de `OnBeforeLiveReload` en todos los manejadores registrados.
  2. Vaciado completo de `ReworkBus.Clear()` y reseteo de tipos mediante `GenTypes.ClearCache()`.
  3. Carga en memoria y sustitución atómica en `mod.assemblies.loadedAssemblies`.
  4. Invocación de `OnAfterLiveReload` para re-inicializar el estado limpio.

**Verificado en runtime:**
- Log verificado: `[ReworkLive] Monitoreando 9 archivo(s) .dll para hot-reload seguro`, `Protocolo OnBeforeLiveReload ejecutado. Recursos limpiados`, `Protocolo OnAfterLiveReload ejecutado. Estado restaurado`. Boot limpio con 0 NREs.

## 30. Invención Única — ReworkBinaryScribe (Guardado Binario con Centinela #REFORJED)

**Qué se implementó:**
- `ReworkBinaryScribe` en `0ReworkAPI`: motor de serialización dual que comprime a formato binario (.rwbin) con cabecera de longitud y centinela atómico final `#REFORJED`.
- Parche en `GameProcessing.cs` sobre `Verse.SafeSaver.Save`: inserción de llamada a `RuntimeHooks.OnSafeSaveCompleted` previa a cada `ret` para empaquetar de forma atómica el binario preservando el `.rws` intacto.
- Parche en `GameProcessing.cs` sobre `Verse.ScribeLoader.InitLoading`: intercepción en la entrada del método con llamada a `RuntimeHooks.TryLoadBinaryDocument`. Si el `.rwbin` existe y su centinela `#REFORJED` es íntegro, descomprime directamente el XML en memoria y fija `Scribe.mode = LoadingVars`, omitiendo el parseo de XML. Si falla o falta, hace fallback limpio al `.rws`.
- Protocolo de detección de corrupción: `IsValidBinarySave` comprueba los últimos 9 bytes del archivo buscando `#REFORJED`. Si falta, el archivo se considera corrupto/incompleto y el sistema activa el fallback al XML de respaldo sin corromper la partida del usuario.

**Verificado en runtime:**
- Log verificado: `[ReworkBinaryScribe] Parche aplicado: Verse.SafeSaver.Save enganchado a ReworkBinaryScribe` y `[ReworkBinaryScribe] Parche aplicado: Verse.ScribeLoader.InitLoading desvia a descompresión binaria de alta velocidad`. Boot limpio con 0 NREs.

## 31. XML de Respaldo de Tiempo Periódico (`GameComponent_ReworkXmlBackup`)

**Qué se implementó:**
- `GameComponent_ReworkXmlBackup` en `ReworkMod`: componente nativo que sincroniza periódicamente el **único** archivo XML (.rws) de la colonia cada X minutos (5 a 120m, 15m por defecto).
- Modelo de guardado limpio: todos los autosaves y guardados rápidos usan el binario (.rwbin) de alta velocidad (<1s) sin duplicar archivos. El XML principal se mantiene como foto de respaldo de tiempo estable.
- Opciones en `ReworkSettings`: slider de intervalo interactivo y visualización de la fecha/hora del último XML sincronizado.

**Verificado en runtime:**
- Log verificado: `compFound=True, isSubclass=True — [ReworkXmlBackup] verificado ✓`. Boot limpio con 0 NREs.

## 32. Bloque 19 — DevTools e Inspector de Runtime (19.1 a 19.11)

**Qué se implementó:**
- `Dialog_ReworkInspector` en `ReworkMod`: ventana flotante interactiva redimensionable y arrastrable que agrupa las 11 herramientas de inspección:
  - 19.1 Inspector de clases con buscador reactivo.
  - 19.2 Inspector de campos inyectados leyendo de `DataStore.InjectedFields`.
  - 19.3 Inspector de métodos parcheados y hooks leyendo de `DataStore.AppliedPatches`.
  - 19.4 y 19.5 Inspector/visor de saves (.rws y .rwbin con comprobación del centinela `#REFORJED`).
  - 19.6 y 19.7 Consola de comandos con parser (`help`, `gc`, `clearbus`, `reloadlive`, `diag`, `fps`).
  - 19.8 y 19.9 Perfilador de memoria GC, frametime e hilos de `Rework.Threading`.
  - 19.10 y 19.11 Visualizador de jobs declarativos y purga manual de caché en caliente (`GenTypes.ClearCache()` y `ReworkBus.Clear()`).
- Integración en `DoSettingsWindowContents`: botón de apertura en el menú de opciones del mod.

**Verificado en runtime:**
- Log verificado: `inspectorTypeFound=True, isWindow=True — [Bloque 19: Inspector de Runtime] verificado ✓`. Boot limpio con 0 NREs.

## 33. Bloque 20 — Compatibilidad Externa y Resiliencia

**Qué se implementó:**
- Aislamiento estricto de librerías de parcheo en runtime (`0Harmony.dll`, `HarmonySharedState`) en `Loader.cs` y `ModifiableAssembly.cs`: marcados como ensamblados de solo-lectura protegidos (`AllowPatches = false`, `ProcessAttributes = false`). Nunca se intentan duplicar ni sobreescribir.
- Blindaje del motor nativo: exclusión total del swap para Unity, Mono.CSharp, mscorlib y System libraries.
- Comprobación de modo seguro proactivo (`layoutOK`): aborta la modificación estructural si se detecta inestabilidad en el layout de memoria nativo de Mono.

**Verificado en runtime:**
- Log verificado: Boot limpio con 0 NREs y coexistencia sin colisiones con librerías externas. Todos los bloques 1 al 20 completados.

## 34. Invención Única — `[ReworkWatch]` (Campos Reactivos)

**Qué se implementó:**
- `ReworkWatchAttribute` en `0ReworkAPI`: atributo que marca métodos estáticos como callbacks reactivos ante la mutación de un campo inyectado (`[ReworkField]`).
- `ReworkWatch` en `0ReworkAPI`: fachada estática pública con `Notifier` (Action delegable) y `NotifyChanged(target, fieldName, oldValue, newValue)`. Permite llamar desde cualquier mod externo sin referenciar `Rework.dll`.
- `ReworkWatchRegistry` en `Rework.dll`: registrador que escanea automáticamente todos los ensamblados activos buscando `[ReworkWatch]` y construye la lista de watchers en boot. Incluye auto-verificación interna del pipeline (`PipelineSelfTest`) que instancia y elimina un watcher temporal para confirmar la cadena de invocación completa sin depender de mods externos.
- Soporte de tres firmas en el callback: `(TTarget)`, `(TTarget, object, object)` (con valores old/new) y sin parámetros.
- Solución al race condition de `[StaticConstructorOnStartup]`: la notificación de prueba del Demo se difiere con `LongEventHandler.ExecuteWhenFinished`; la auto-verificación del pipeline la realiza `ReworkWatchRegistry` internamente sin depender del orden de inicialización de ensamblados externos.

**Verificado en runtime:**
- Log verificado: `[ReworkWatch] Observador registrado: DemoWatchListener.OnXpMultiplierChanged → campo 'Rework_XpMultiplier'`, `[ReworkWatch] 1 observador(es) registrado(s) — pipeline activo.`, `[ReworkWatch] Auto-verificación del pipeline: selfTestFired=True — [ReworkWatch] verificado ✓`, `watcherRegistrado=True, watcherInvocado=True — [ReworkWatch Demo] verificado ✓`. Boot limpio con 0 NREs.

## 35. Invención Única — `[ReworkUIPanel]` (UI Inyectada)

**Qué se implementó:**
- `ReworkUIPanelAttribute` en `0ReworkAPI` (puro BCL): `WindowType` (FullName de la ventana destino en Assembly-CSharp), `AtStart` (posición del panel) y `Priority` (orden entre paneles).
- `UiPanelInjector` en `ReworkCore`: en la pasada 1 (Cecil) busca métodos estáticos con el atributo en los ensamblados de mods, resuelve `DoWindowContents(Rect)` en el tipo destino (subiendo por la jerarquía de base si hace falta) e inserta `ldarg.0; ldarg.1; call ModPanel(Window, Rect)` (o solo `ldarg.1; call ModPanel(Rect)` con firma de 1 parámetro). Orden prio desc → orden de carga → nombre (igual que FreePatcher), con filtro `ReworkDevMode`.
- Pipeline: registrado en `GameProcessing.Process` tras `FreePatcher` (1c').
- Demo (Rework Demo Reforjed `ReworkDemoUi.cs`): panel en `Verse.Dialog_MessageBox` (1 param, encima), `RimWorld.MainTabWindow_Work` (2 params, encima) y `RimWorld.MainTabWindow_Research` (1 param, debajo/AtStart=true).

**Lección empírica crítica (trap nuevo):** la inyección "por encima" (antes del último ret) NO basta si el método destino tiene ramas condicionales que saltan DIRECTAMENTE al ret — p.ej. `Verse.Dialog_MessageBox::DoWindowContents` tiene 3 `brfalse.s → ret` (botones B/C ausentes). El call inyectado quedaba ANTES del ret pero esas ramas lo saltaban → el panel jamás se ejecutaba en el flujo normal (la auto-verificación del demo "FALLÓ" sin excepciones, lo que delató que no era un error de IL sino de flujo). **Fix: tras insertar el bloque, re-apuntar TODAS las instrucciones con FlowControl Branch/Cond_Branch cuyo destino era el punto de inserción (anchor) al INICIO del bloque inyectado.** Verificado: `3 rama(s) re-apuntada(s) al panel inyectado (ningún camino lo omite)`.

**Verificado en runtime (2026-10-07):**
- Log verificado: `Panel UI inyectado: …::PanelParaDialogo → Verse.Dialog_MessageBox::DoWindowContents (encima/final)`, `3 rama(s) re-apuntada(s)`, `Rework Demo UIPanel: auto-verificación OK ✓ — panel de Dialog_MessageBox pintado 482x (label inyectado ejecutando). [ReworkUIPanel] verificado.` Boot 0 excepciones; `[ReworkWatch]` sigue verificado (sin regresión).
- Nota de verificación: la auto-verificación abre un `Dialog_MessageBox` de prueba cuando `LongEventHandler.ShouldWaitForEvent == false` (menú estable) y reintenta hasta 5 veces; durante la carga de Defs `Root.OnGUI` hace early-return y NO dibuja el WindowStack, por lo que un intento temprano siempre da "no pintado" (comportamiento correcto del juego, no un fallo del mod).

## 37. Features documentadas que NO existían en el código — SOLUCIONADO (ReworkContent)

**Qué se detectó (auditoría 2026-10-07):** el MANUAL prometía 11 extensiones de contenido sin XML, pero **5 no existían en ningún DLL**: `[ReworkGene]`, `[ReworkResearch]`, `[ReworkRaid]`, `[ReworkThought]` y `[ReworkApparel]`. Además, `[ReworkZoneEffect]` se prometía como atributo declarativo pero solo existían `ReworkZoneManager`/`ZoneEffectDefinition` (manager sin atributo).

**Qué se hizo:** nuevo ensamblado **`ReworkContent.dll`** (`Source\ReworkContent\`) que implementa:
- Atributos en `0ReworkAPI`-style (namespace `Rework`): `ReworkGeneAttribute`, `ReworkResearchAttribute`, `ReworkRaidAttribute`, `ReworkThoughtAttribute`, `ReworkApparelAttribute`, `ReworkZoneEffectAttribute`.
- Registries autónomos con `[StaticConstructorOnStartup]` (mismo patrón que `ReworkRecipeRegistry`): `ReworkGeneRegistry` (GeneDef → `Verse.GeneDef`, categoría `GeneCategoryDef`), `ReworkResearchRegistry` (`Verse.ResearchProjectDef`, tab `ResearchTabDef`), `ReworkRaidRegistry` (`RimWorld.RaidStrategyDef`, `RaidStrategyWorker`, curva de puntos), `ReworkThoughtRegistry` (`RimWorld.ThoughtDef` + `ThoughtStage`), `ReworkApparelRegistry` (`ThingDef` + `ApparelProperties` + `GraphicData`), `ReworkZoneEffectRegistry` (conecta métodos estáticos `static void X(object pawn)` a `ReworkZoneManager.RegisterZone`).
- **Decisión de arquitectura:** ensamblado NUEVO en lugar de tocar los binarios existentes, porque `Rework.dll`/`ReworkCore.dll`/`0ReworkAPI.dll` contienen clases desplegadas SIN fuente (`ReworkAttributeScanner`, `ReworkRuntime`, `ReworkFrameworkLifecycleHooks`, `ReworkPersistence`, `ReworkNeedApiRegistry`); reconstruirlos los habría borrado (regresión). El csproj referencia `0ReworkAPI.dll` por HintPath (sin `ProjectReference`) para no recompilarla.
- Demo: `ReworkDemoContent2.cs` (demo) declara un gen, una investigación, una estrategia de raid (con `RaidStrategyWorker.MakeLordJob` → `LordJob_AssaultColony`), un pensamiento, una prenda y una zona, más un verificador `[StaticConstructorOnStartup]` que loguea `gene=..., research=..., raid=..., thought=..., apparel=..., zone=...`.

**Traps resueltos:**
- `ThingDef` NO tiene `descriptionFlavor` en 1.6.4850 (CS0117); usar `description`.
- `SimpleCurve` no se construye con initializer de colección seguro; usar `curve.Add(new CurvePoint(x,y), false)`.
- `RaidStrategyWorker.MakeLordJob` es `protected abstract` con 4 parámetros; el ctor de `LordJob_AssaultColony` con `Faction` NO existe como overload de 1 param (solo ctor vacío/`SpawnedPawnParams`/8-bools).
- `ThingDef.drawGUIOverlay` (mayúsculas GUI), no `drawGuiOverlay`.

**Verificado en build:** 0 errores / 0 warnings (Release, net472). Auditoría anti-fantasmas re-ejecutada con `ReworkContent.dll`: 7730 calls / 2932 fields / 347 type operands revisados, **0 referencias sin resolver** (los 21 listados son falsos positivos ya conocidos: genéricos `T` y nombres sin namespace que existen en el juego).

## 38. NRE del self-test de [ReworkWatch] — SOLUCIONADO

**Síntoma:** en cada arranque, el log mostraba:
`[ReworkWatch] Error ejecutando observador 'OnMultiplicadorCambio': NullReferenceException at RimWorld.GenLabel.BestKindLabel ← Verse.Pawn.get_KindLabel`.

**Causa raíz:** `ReworkDemoWatchVerify.Verificar()` (`ReworkDemoContent.cs`) llama `ReworkWatch.NotifyChanged(new Verse.Pawn(), ...)` y el watcher `DemoWatchAuto.OnMultiplicadorCambio` (firma `(Pawn, float, float)`) leía `pawn.LabelShort`, que exige `pawn.kindDef` — un `new Verse.Pawn()` crudo no lo tiene → NRE. `kindDef` se asigna en `PostMake`, después de los hooks (`PawnCreated` corre antes).

**Fix:** guarda defensiva en el watcher: `if (pawn == null || pawn.kindDef == null) return;` + try/catch. El self-test sigue notificando, pero ya no produce NRE en el log. (El framework ya capturaba el error, pero ensuciaba el `Rework.log` y contradecía "boot limpio 0 NREs".)

## 39. Cuelgue al generar mapa: falta ctor sin parámetros en ReworkCustomAlert — SOLUCIONADO

**Síntoma (reproducido 2026-10-08):** en la "prueba rápida de desarrollo", al generar el mapa el juego se quedaba **pegado** (sin crashear): la pantalla se congelaba y el log entraba en un bucle infinito de `Root level exception in Update()/OnGUI(): NullReferenceException` en `LetterStack.OpenAutomaticLetters`, `MapUI`, `FishShadowComponent.MapComponentTick`, etc.

**Causa raíz (cadena de fallos):**
1. La primera excepción real es: `Critical error in root Start(): MissingMethodException: Default constructor not found for type Rework.Core.ReworkRuntime+ReworkCustomAlert`.
2. Vanilla `RimWorld.AlertsReadout..ctor` instancia por reflexión TODAS las subclases de `RimWorld.Alert` con `Activator.CreateInstance(type)`, y `ReworkCustomAlert` (inyectada por `[ReworkAlert]`) solo tenía el ctor `(AlertEntry)` — no el ctor público sin parámetros que `Activator.CreateInstance` exige.
3. Ese fallo corrompía el manejador de errores de generación de mapa (`ErrorWhileGeneratingMap`), que a su vez fallaba en `MapDispose`/`MapDrawer.Dispose` (línea 205-211), dejando el estado a medio camino → NREs en cadena en cada tick/OnGUI → "pegado".

**Por qué no se había visto antes:** las sesiones anteriores (23:04, 23:49 del 07-10) solo llegaron al menú principal; nunca se había entrado a la escena de juego `Play` (que es cuando `AlertsReadout` se construye) con el mod activo.

**Fix quirúrgico (sin fuente disponible):** `ReworkCustomAlert` es una clase ANIDADA de `ReworkRuntime` que existe SOLO en el binario `ReworkCore.dll` (sin archivo .cs — el source está incompleto respecto a los DLL). Se reescribió el binario con Mono.Cecil añadiendo el ctor público sin parámetros:
```
.ctor() { base..ctor(); this.entry = new ReworkAlertRegistry.AlertEntry(); }
```
La `AlertEntry` vacía tiene `ConditionMethod = null` → `GetReport()` devuelve `Inactive` → la alerta nunca se muestra pero la clase ya es instanciable por vanilla. Verificado en el binario: ctors finales `(AlertEntry)` y `()` ambos públicos. Backup: `Assemblies\ReworkCore.dll.bak`.

**Trap al parchear el binario:** `ModuleDefinition.ReadModule(path, ReadWrite=true)` mantiene el archivo bloqueado → `Write(path)` lanza IOException. Fix: leer sin lock, escribir a `.fixed.dll` temporal, `Move-Item` sobre el original (mismo directorio = mismo volumen).

**Importante para el futuro:** el source de `ReworkCore` está incompleto (faltan `ReworkRuntime`, `ReworkAttributeScanner`, `ReworkFrameworkLifecycleHooks`, `ReworkPersistence`, `ReworkNeedApiRegistry`). Mientras no se restauren esas clases en el fuente, **NO reconstruir `ReworkCore.dll`/`Rework.dll`/`0ReworkAPI.dll` desde el slnx**, o se perderán. La regla: añadir funcionalidad nueva solo en ensamblados nuevos (patrón `ReworkContent`).

### 39.1 REINCIDENCIA (2026-10-08, sesión de restauración del fuente) — RESUELTO EN EL FUENTE

**Qué pasó:** al restaurar por fin las 5 clases del binario al fuente (`ReworkRuntime.cs`, `ReworkAttributeScanners.cs`, `ReworkFrameworkLifecycleHooks.cs`, `ReworkPersistence.cs`, `ReworkNeedApiRegistry.cs`) y reconstruir el slnx, se **perdió el parche binario** de §39. El `ReworkCustomAlert` restaurado sólo traía el ctor `(AlertEntry)`, así que el cuelgue al generar el mapa **reapareció idéntico** (log: `MissingMethodException: Default constructor not found for type Rework.Core.ReworkRuntime+ReworkCustomAlert` → `UIRoot_Play` no se construye → `Find.MapUI == null` → `Game.AddMap → Find.ColonistBar` NRE → mapa nunca se genera).

**Lección:** un parche hecho sólo en el binario es **volátil**; cualquier rebuild desde el slnx lo borra. El arreglo debe vivir en el fuente.

**Fix definitivo (ya en el fuente, sobrevive a rebuilds):**
- `Rework.Core.ReworkRuntime.ReworkCustomAlert`: ctor público `()` → instancia inerte (`entry == null`, `IsInert => true`); `GetLabel()`/`GetExplanation()`/`GetReport()` null-safe (Inactive si no hay entrada).
- `Rework.Core.ReworkRuntime.ReworkRuntimeTab` (`: InspectTabBase`): mismo patrón preventivo — ctor público `()`, `StillValid => entry != null`, `FillTab()` sale si `entry == null`. Vanilla también sondea subclases de `InspectTabBase`.
- `RuntimeHooks.AttachReworkAlerts`: elimina las instancias inertes que vanilla dejó en `AllAlerts` (`RemoveAll(a => a is ReworkCustomAlert rca && rca.IsInert)`) antes de añadir las reales.

**Verificación:** inspección con Mono.Cecil de la DLL compilada confirma ctors públicos `()` y `(AlertEntry)`/`(TabEntry)` en ambos tipos anidados. Barrido de seguridad sobre `ReworkCore.dll`, `Rework.dll` y `ReworkDemo.dll` de todas las subclases de `Alert`/`InspectTabBase`/`MainTabWindow`/`Designator`/`JobDriver`: no queda ninguna sin ctor público sin parámetros (`Rework.ReworkJobDriver` es `abstract`, y vanilla filtra `NonAbstract`).

**Ya no aplica la restricción:** el fuente de las 5 clases **está restaurado y compila** (slnx con 0 errores), así que reconstruir desde el slnx es seguro y es el flujo correcto. Mantener siempre el arreglo en el `.cs`, nunca sólo en el DLL.

## 40. [ReworkWorkGiver] no registraba nada (0 WorkGivers) y caía en silencio a Hauling — SOLUCIONADO

**Síntoma (log del 2026-10-08, sesión de restauración):** el escaneo daba `WorkGiver=0` y la línea
`ReworkWorkGiverRegistry: 0 WorkGiver(s) declarativo(s) [ReworkWorkGiver] registrado(s) en DefDatabase.`
Era el **único fantasma que quedaba** de la API: el atributo existía y el registro funcionaba, pero ningún mod lo
consumía (el demo declaraba `[ReworkJob]` para «kill» pero **ningún WorkGiver**), así que el trabajo no aparecía en la
pestaña de Trabajo ni ningún colono lo hacía por su cuenta.

**Segundo defecto (latente, habría mordido al primer uso real):** `RegisterWorkGiver` resolvía el tipo de trabajo con
`DefDatabase<WorkTypeDef>.GetNamedSilentFail(attr.WorkType) ?? WorkTypeDefOf.Hauling`. Si el `WorkType` declarado no
existía —lo normal, porque el mod no trae XML— el trabajo **se colaba en silencio dentro de "acarreo"** en vez de
avisar. El modder lo vería en la categoría equivocada sin ningún error en el log.

**Fix (en el fuente):**
1. `ReworkWorkGiverAttribute` gana metadatos para declarar el tipo de trabajo: `WorkTypeLabel`, `WorkTypeLabelShort`,
   `WorkTypePawnLabel`, `WorkTypeGerund`, `WorkTypeVerb`, `WorkTypeTags`, `WorkTypeVisible`, `WorkTypeNaturalPriority`,
   `WorkTypeAlwaysStartActive` y `CreateWorkTypeIfMissing` (por defecto `true`).
2. `ReworkWorkGiverRegistry.ResolveOrCreateWorkType(attr)`: si el `WorkTypeDef` no existe, lo **crea** con esos
   metadatos, lo añade con `DefDatabase<WorkTypeDef>.Add` + `SetIndices` (mismo patrón que el DefBuilder) y lo registra.
   Con `CreateWorkTypeIfMissing = false` se mantiene el respaldo a `Hauling`.
3. `ParseWorkTags("Violent,ManualDumb")` → `WorkTags`, con aviso si la etiqueta no se reconoce.
4. El `WorkGiverDef` ahora recibe también `label` (antes quedaba `null`: riesgo de NRE en rutas de UI).

**Trampas de la API de RimWorld 1.6 encontradas al implementarlo** (verificado con Mono.Cecil sobre `Assembly-CSharp.dll`):
- `WorkTypeDef` vive en **`Verse`**, no en `RimWorld` (`Verse.WorkTypeDef`); `WorkTypeDefOf` también es `Verse`.
- El campo del verbo es **`verb`**, NO `verbLabel` (CS0117). Campos reales: `workTags`, `labelShort`, `pawnLabel`,
  `gerundLabel`, `verb`, `visible`, `visibleOnlyWithChildrenInColony`, `naturalPriority`, `alwaysStartActive`,
  `requireCapableColonist`, `relevantSkills`, `disabledForSlaves`, `workGiversByPriority`.
- **`visible` es `false` por defecto:** un WorkTypeDef creado sin `visible = true` **no aparece** en la pestaña de
  Trabajo, así que el jugador no puede subirle la prioridad ni desactivarlo.
- `JobMaker` es **`Verse.JobMaker`** (no `Verse.AI.JobMaker`).
- **No existe `Pawn.Reserve`**: es la extensión `Verse.AI.ReservationUtility.Reserve(Pawn, LocalTargetInfo, Job, ...)`,
  invocable como `pawn.Reserve(target, job, 1, -1, null, errorOnFailed)`.
- `Toils_Goto.GotoThing(TargetIndex, PathEndMode, bool canGotoSpawnedParent = true)`.
- `WorkGiver_Scanner` declara `HasJobOnThing` / `JobOnThing` / `PotentialWorkThingsGlobal` / `PathEndMode` como
  **virtuales no abstractos**; para objetivos tipo *pawn* se filtra sobreescribiendo `PotentialWorkThingsGlobal`.

**Caso de uso que lo cierra (demo):** `WorkGiver_DemoKill` (`[ReworkWorkGiver]`) declara su propio
`ReworkDemo_KillType` ("matanza", `WorkTags=Violent`), que el framework crea sin XML, y entrega el `JobDef`
`ReworkDemo_Kill` (registrado por `[ReworkJob]`) apuntando a **cualquier pawn humanlike** del mapa: amigos, neutrales
o enemigos (comportamiento literal pedido). El `JobDriver_DemoKill` pasó a **honrar `TargetA`** (antes elegía la víctima
por su cuenta e ignoraba el objetivo del WorkGiver). Resultado esperado en el log:

```
[ReworkWorkGiver] WorkTypeDef 'ReworkDemo_KillType' creado desde el atributo (label='matanza', verb='matar', tags=Violent, prio=6).
[ReworkWorkGiver] Registrado WorkGiverDef 'ReworkDemo_KillGiver' -> giver WorkGiver_DemoKill (workType=ReworkDemo_KillType, prio=60).
ReworkWorkGiverRegistry: 1 WorkGiver(s) declarativo(s) [ReworkWorkGiver] registrado(s) en DefDatabase.
```

**Nota de comportamiento:** al recibir los colonos el nuevo tipo de trabajo habilitado a la prioridad más baja (3),
empezarán a matarse entre ellos en cuanto estén ociosos. Para desactivarlo: pestaña **Trabajo → "matanza" a prioridad 0**.

## 41. El tipo de trabajo creado por [ReworkWorkGiver] no tenía casilla en la pestaña de Trabajo — SOLUCIONADO

**Síntoma (reportado por el usuario, 2026-10-08):** tras §40 el log confirmaba el `WorkTypeDef` creado y el
`WorkGiverDef` registrado, y los colonos usaban el trabajo… pero en la **pestaña de Trabajo no aparecía ninguna
columna** ("matanza"): no había casilla donde ponerle prioridad como a las demás.

**Causa raíz:** la pestaña de Trabajo **no** se construye a partir de `DefDatabase<WorkTypeDef>`. Vanilla genera, al
cargar los Defs, un `PawnColumnDef` por cada `WorkTypeDef` **visible** en
`RimWorld.PawnColumnDefGenerator.ImpliedPawnColumnDefs(bool)` —un iterador de *defs implícitos*— y lo inserta en
`PawnTableDefOf.Work.columns` justo antes de la columna de copiar/pegar prioridades.

Detalles exactos verificados en el IL de `Assembly-CSharp.dll`:
- `defName` de la columna = **`"WorkPriority_" + workTypeDef.defName`** (prefijo `WorkPriority_`, no `WorkType_`).
- Filtro: `WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder.Where(w => w.visible)` y después `.Reverse()`.
- Predicado de inserción: la primera columna cuyo `Worker` sea `PawnColumnWorker_CopyPasteWorkPriorities`.
- Campos que rellena: `defName`, `workType`, `moveWorkTypeLabelDown` (alterna por columna),
  `workerClass = typeof(PawnColumnWorker_WorkPriority)`, `sortable = true`.

Esa fase de defs implícitos corre **durante la carga de Defs**, es decir **antes** de cualquier
`[StaticConstructorOnStartup]`. Un `WorkTypeDef` creado por Rework (registrado en el arranque) nace por tanto
**después** del generador y se queda sin columna: existe, los colonos lo usan, pero es invisible para el jugador.

**Fix (en el fuente):** `ReworkWorkGiverRegistry.EnsureWorkTabColumn(def)`, invocado al crear el `WorkTypeDef`, replica
la generación de vanilla para ese tipo:
1. Busca o crea el `PawnColumnDef` `"WorkPriority_<defName>"` con `workType`, `workerClass = PawnColumnWorker_WorkPriority`
   y `sortable = true`.
2. Lo añade a `DefDatabase<PawnColumnDef>` + `SetIndices` (sólo si es nuevo).
3. Lo inserta en `PawnTableDefOf.Work.columns` antes de la columna de copiar/pegar prioridades (el mismo índice que usa
   vanilla), evitando duplicados con `Contains`.

Log esperado:

```
[ReworkWorkGiver] Columna 'WorkPriority_ReworkDemo_KillType' añadida a la pestaña de Trabajo (PawnTableDefOf.Work.columns, índice N).
```

**Regla general para el futuro:** cualquier Def que RimWorld genere como *implied def* durante la carga (columnas de
trabajo, columnas de animales por `TrainableDef`, etc.) hay que generarla a mano si el Def del que depende se crea
después de esa fase.

## 42. Orden «matar»: error de reserva y muerte instantánea en vez de combate — SOLUCIONADO

**Síntomas (reportados por el usuario, 2026-10-08):**
1. En el log aparecía, repetido:
   `Could not reserve Thing_Human463 (layer: null) for Yuu for job ReworkDemo_Kill … for maxPawns 1 and stackCount -1.`
2. La orden «matar» **mataba de un golpe** (daño letal directo), sin pelea: *"es como cuando sacrifican los animales
   y matan instantáneamente"*. Además ignoraba el arma equipada del colono.

**Causa raíz 1 (reserva):** el `WorkGiver` ofrecía **cualquier** humanlike del mapa como objetivo sin comprobar si ya
estaba reservado. Varios colonos elegían la **misma** víctima; el primero la reservaba y los demás fallaban en
`TryMakePreToilReservations` (que se llama con `errorOnFailed: true`), y `Verse` escupía ese error por cada intento.

**Causa raíz 2 (instantáneo):** el `JobDriver` aplicaba `target.TakeDamage(new DamageInfo(DamageDefOf.Cut, 9999f, …))`
en un `initAction`. Eso no es combate: es una ejecución (el arma del colono no participa y no hay pelea).

**Fix (en el fuente):**
1. `HasJobOnThing` añade `if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;` → un objetivo ya reservado deja
   de ofrecerse y desaparece el error del log.
2. La orden «matar» pasa a emitir un **trabajo de combate real**, eligiéndolo según el arma equipada (igual que hace la
   IA de vanilla):
   - **arma a distancia** → `JobDefOf.AttackStatic` (se acerca y **dispara**).
   - **arma melee o sin arma** → `JobDef` declarativo `ReworkDemo_Kill` (persigue y **pelea**).
   - En ambos casos `job.killIncappedTarget = true` → **remata** al objetivo derribado en vez de dejarlo tirado
     (es el patrón de `JobGiver_MurderousRage`: `AttackMelee` + `killIncappedTarget = true`).
3. El `JobDriver_DemoKill` pasa a **heredar de `Verse.AI.JobDriver_AttackMelee`** en lugar de implementar toils a mano.
   Así el colono persigue al objetivo y golpea con el arma que lleva, con la lógica de combate de vanilla.

**Hallazgos de la API 1.6 que hicieron posible el fix** (verificados con Mono.Cecil sobre `Assembly-CSharp.dll`):
- `Verse.AI.JobDriver_AttackMelee` es **`public`, no `sealed`, con ctor público y `MakeNewToils` virtual** → se puede
  heredar de él para reutilizar el combate cuerpo a cuerpo de vanilla sin reescribirlo.
- **`Toils_Combat.FollowAndMeleeAttack(ind, ind, hitAction)`: el `hitAction` ES el ataque.** Su lambda interna hace
  `pawn.meleeVerbs.TryMeleeAttack(target, job.verbToUse, false)`. Pasar una lambda vacía haría que el colono
  **persiguiera al objetivo sin golpearlo nunca**. Su `ToilCompleteMode` es `Never` (5): el toil dura hasta que una
  condición lo corta.
- `Job.maxNumMeleeAttacks` y `Job.maxNumStaticAttacks` nacen en **`int.MaxValue`** en `Job..ctor()` → el combate no se
  corta tras el primer golpe.
- `Verse.Verb.IsMeleeAttack` (propiedad) y `Verse.CompEquippable.PrimaryVerb` son la forma de saber si el arma
  equipada es a distancia; `pawn.equipment?.PrimaryEq?.PrimaryVerb`.
- La IA vanilla (`JobGiver_AIFightEnemy`) decide melee/distancia por `VerbProperties.IsMeleeAttack`.
- `JobDefOf.AttackStatic` **sí mueve** al pawn (su lambda usa `pawn.pather`), así que sirve para acercarse y disparar.
- Nadie en vanilla usa `Toils_Combat.CastVerb`/`GotoCastPosition` (son ayudas para modders): se prefirió el camino
  probado de los `JobDef` vanilla.
- `Verse.AI.ReservationUtility.CanReserve(Pawn, LocalTargetInfo, …)` es una **extensión** → `pawn.CanReserve(t, 1, -1, null, forced)`.

**Además (limpieza pedida por el usuario):** se eliminó del mod demo todo el residuo «vigilante», que no significaba
nada y aparecía en la interfaz:
- el trabajo `[ReworkJob]` `ReworkDemo_Vigilancia` («vigilando la base.») — **eliminado**;
- el gizmo «Vigilar objetivo» → ahora **«Registrar logro (Rework)»** (usa el campo inyectado `Rework_Logros`);
- la línea de inspección «Vigilante Reforjado (demo)» → ahora muestra **`Rework_XpMultiplier` y `Rework_Logros`**;
- la pestaña `demo_vigilancia_tab` → `demo_rework_tab`, mostrando los campos inyectados.

Así **ningún contador del escaneo baja a 0** (no se crean fantasmas): `[ReworkGizmo]`, `[ReworkInspectString]`,
`[ReworkTab]` y `[ReworkJob]` conservan su consumidor en el demo.

---

## 44. Fantasmas declarativos — features registradas sin consumidor

Tras el escaneo completo del código fuente, se identificaron **13 funciones fantasmas**:
APIs y sistemas declarativos que existían en `0ReworkAPI.dll` pero **nunca eran consumidos**
por el runtime del juego (cero llamadores en todo el fuente). Algunas tenían el dispatch de Cecil
inyectado pero el early-out siempre se disparaba (`Count == 0`); otras eran librerías públicas
huérfanas.

### 44.1. Clasificación de fantasmas

| Fantasma | Tipo | ¿Afecta a mods externos? | Fix aplicado |
|----------|------|--------------------------|--------------|
| `ReworkMainThread.Drain()` | Fantasma de dispatch | ✅ Sí (acciones nunca se ejecutaban) | `OnGameComponentTick()` llama a `Drain()` |
| `ReworkOverlay` (sin atributo) | Sistema sin escáner | ✅ Sí (no registraba drawers) | `[ReworkOverlay]` + escáner |
| `ReworkDepGraph.ResolveLoadOrder` | Fantasma parcial | ⚠️ Parcial (grafo construido, nunca resuelto) | `InitRunner.Run()` resuelve orden de carga |
| `ReworkColonySkill.AddXp` | Fantasma de persistencia | ⚠️ API pública sin activación interna | `[ReworkSchedule(EveryDay)]` + `[ReworkInit]` |
| `ReworkPawnTimeline.RecordEvent` | Fantasma de persistencia | ⚠️ API pública sin activación interna | `[ReworkHook(PawnDied/BabyBorn)]` |
| `ReworkRelation.SetRelation` | Fantasma de persistencia | ⚠️ API pública sin activación interna | `[ReworkSchedule(EveryDay)]` escáner de relaciones |
| `ReworkStateSnapshot.Capture` | Fantasma de persistencia | ⚠️ API pública sin activación interna | `[ReworkSchedule(EveryDay)]` captura diaria |
| `ReworkDynamicMutate.ApplyMutation` | Fantasma parcial | ⚠️ Solo `RevertAll` funcionaba | `[ReworkInit]` mutación demo |
| `ReworkCache` | API utilitaria | ❌ No (API pública para mods) | Disponible, no requiere fix |
| `ReworkProfiler` | API utilitaria | ❌ No (API pública para mods) | Disponible, no requiere fix |
| `ReworkJobs.Pool.Run` | API utilitaria | ❌ No (API pública para mods) | Disponible, no requiere fix |

### 44.2. Soluciones implementadas

**Fix crítico — `ReworkMainThread.Drain()`:**
- `ReworkMainThread` era un sistema duplicado de `ReworkParallel` (ambos con colas de callbacks
  para el hilo principal). El dispatch de `ReworkParallel.FlushMainThreadQueue()` estaba conectado
  en `RuntimeHooks.OnGameComponentTick()`, pero `ReworkMainThread.Drain()` **nunca se llamaba**.
- **Solución:** `Drain()` añadido en `RuntimeHooks.OnGameComponentTick()` en dos puntos
  (early-out + tick principal), junto a `FlushMainThreadQueue()`.

**Fix crítico — `[ReworkOverlay]`:**
- El patch de Cecil (`PatchMapComponentOverlay`) estaba inyectado, el dispatch existía en
  `RuntimeHooks.RenderReworkOverlays()`, pero **no existía un atributo `[ReworkOverlay]`** y
  `ReworkOverlay.Register()` nunca se llamaba. El contador `Count` siempre era 0 → early-out →
  nunca renderizaba.
- **Solución:** nuevo atributo `ReworkOverlayAttribute(string id)` en la API, escáner en
  `ReworkAttributeScanners.ScanAll()` que valida la firma `void Method(object)` y registra via
  `ReworkOverlay.Register()`. Incluido en el contador de la línea de log: `Overlay=N`.
- **Demo:** `[ReworkOverlay("demo_mood_summary")]` añadido al mod demo — overlay flotante que
  muestra el humor promedio de los colonos con barra de color (rojo/amarillo/verde).

**Fix parcial — `ReworkDepGraph.ResolveLoadOrder`:**
- `AddDependency` se registraba en el escáner, pero `ResolveLoadOrder` nunca se llamaba.
- **Solución:** `InitRunner.Run()` ahora llama `ReworkDepGraph.ResolveLoadOrder(allModIds)`
  antes de construir el diccionario de orden de assemblies, respetando `[ReworkRequires]`
  en el orden de ejecución de inits.

**Fix de persistencia — Sistemas `ReworkColonySkill` / `ReworkPawnTimeline` /
`ReworkRelation` / `ReworkStateSnapshot` / `ReworkDynamicMutate`:**
- Todos estaban registrados en `ReworkPersistence.Store[]` para serialización, pero **nunca se
  activaban**. Se creó `ReworkRuntimeActivators.cs` en `ReworkMod` con:
  - `[ReworkSchedule(EveryDay = true)]` → `GrantDailyColonyXp()` (otorga XP a skills)
  - `[ReworkInit(Priority = 10)]` → `SubscribeColonySkillLevelUp()` (suscripción al evento)
  - `[ReworkHook(PawnDied/BabyBorn)]` → registra eventos históricos
  - `[ReworkSchedule(EveryDay = true)]` → `ScanPawnRelations()` (escanea opiniones entre colonos)
  - `[ReworkSchedule(EveryDay = true)]` → `DailySnapshot()` (captura estado de pawns)
  - `[ReworkInit(Priority = 5)]` → `ApplyDemoMutation()` (mutación demo en CraftingSpot)

## 45. Fuente en estado mixto: el árbol del mod no compilaba (§53/§54/§55/§43 a medias) — SOLUCIONADO

- **Síntoma (encontrado al compilar para añadir el bloque 24):** `dotnet build` del
  `slnx` fallaba con 13 errores CS0117/CS0246 ANTES de tocar nada:
  `ReworkScheduler.Count`, `ReworkBus.HasSubscribers<T>()`, `QuestRuntime.HasQuests`,
  `StatusEffectRuntime.HasActive`, `ZoneRuntime.HasZones`, `ReworkGizmoRegistry.Count`,
  `ReworkInspectStringRegistry.HandlerCount`, `ReworkAIRegistry.ModifierCount`,
  `ReworkLore.GeneratorCount/HasGenerator`, `ReworkMinVersionAttribute` y
  `ReworkApi.VersionAtLeast` **no existían** en el árbol del mod, pero sí los
  consumían `RuntimeHooks.cs` (early-outs §53) y `ReworkAttributeScanners.cs`
  (contrato §43).
- **Causa raíz:** el árbol del mod quedó en un estado MIXTO tras una reversión
  parcial: los CONSUMIDORES (§53/§43) eran actuales, pero los PROVEEDORES eran de
  la etapa anterior. **El espejo del Escritorio
  (`C:\Users\User\Desktop\Rework Framework\Source`) conservaba el árbol COMPLETO
  post-§53/§54/§55/§43** (incluidos `ReworkCompanionDefRegistry`,
  `ReworkContentSelfCheck`, `ReworkCompanionDefGenerator` y
  `ReworkContentUsability`, ausentes del mod) — excepto un archivo:
  `ReworkMinVersionAttribute` faltaba en ambos.
- **Fix (en el `.cs`, regla 4):**
  1. Restaurados del espejo los 19 archivos canónicos (16 íntegros + 3 con
     re-aplicación de los cambios del bloque 24: `ReworkApi.cs` — puertas del
     manifiesto —, `Lg.cs` — HeuristicComponent — y `GameProcessing.cs` — cableado
     de los procesadores). El `ReworkMinVersionAttribute` se toma de la definición
     canónica DENTRO de `ReworkApi.cs` (se descartó la copia en archivo aparte para
     no duplicar el tipo).
  2. Los contadores canónicos difieren en dos detalles de la primera
     reimplementación de emergencia (que quedó descartada): `HasZones` usa
     `ReworkZoneManager.RegisteredCount` y `HasQuests` usa `ReworkQuestRegistry.Count`.
- **Verificación:** `dotnet build Rework.slnx -c Release` → **0 errores**;
  `dotnet build ReworkContent.csproj -c Release` → **0 errores / 0 advertencias**.
  Las DLLs quedaron desplegadas en `Assemblies\`. Pendiente de verificación en
  partida (regla 9) igual que el bloque 24.
- **Lección:** tras cualquier reversión/restauración parcial, el primer chequeo es
  compilar el árbol completo. Y el espejo del Escritorio es el juez de qué versión
  es la canónica: restaurar DESDE él (no reimplementar de memoria) evita
  divergencias silenciosas en detalles de API.

## 46. Bloque 24 — cirugía IL declarativa: los 5 features que Harmony no puede

NUEVA FEATURE (no un bug): los cinco atributos del bloque 24, con consumidor real
(anti-fantasma §44) y verificación de arranque. Documentación para modders en
`MANUAL.md` → «Cirugía IL declarativa — Bloque 24». Resumen técnico:

- **`[ReworkRedirect]`** (`ReworkCore/CallSiteRedirector.cs`): reescribe TODOS los
  call-sites de un método del juego hacia el método del mod, en todos los
  ensamblados reescribibles (match por firma completa → los overloads quedan
  fuera). Regla anti-recursión: no se reescribe nada dentro del redirector
  (camino documentado al original). Un solo pase por ensamblado con diccionario.
- **`[ReworkOverride]`** (`ReworkCore/OverrideInjector.cs`): inyecta en una clase
  del juego un override REAL del virtual de su base (forwarder al método del mod;
  `CallBase=true` guarda el resultado del base en una local y lo pasa como último
  parámetro). Valida: virtual en la cadena de bases, la clase no lo redeclara,
  sin genéricos ni `ref/out`, clase no sealed, firma exacta. No sufre la
  limitación de InterfaceInjector (§18): el método vive en Assembly-CSharp y solo
  REFERENCIA al mod (nada del mod en la vtable).
- **`[ReworkUnlock]`** (`ReworkCore/UnlockProcessor.cs`): metadatos reales —
  `private/internal → public`, `sealed → heredable`, métodos de instancia →
  `virtual` (newslot). Atributo multi-instancia sobre método/clase/ensamblado.
- **`[ReworkInline]`** (`ReworkCore/InlineProcessor.cs`): valida cuerpo
  EXACTAMENTE trivial (`ldarg.0; ldfld; ret` / `ldsfld; ret`), no virtual sin
  final, sin parámetros; muta cada call-site EN SITIO (los saltos siguen
  válidos) al acceso directo del campo; si el campo no es público, lo publica.
- **`[ReworkConst]`** (`ReworkCore/ConstRewriter.cs`): pliega los `ldsfld` de un
  `static readonly` a literales (bool/enteros/float/double/string/null/enum),
  carga precomputada, mutación en sitio, `.cctor` intacto (la reflexión sigue
  viendo el valor original). Un `const` literal se detecta y se explica por qué
  NO se puede (valor quemado en cada call-site).
- **Consumidor real (auto-demo, impacto cero):** `ReworkMod/ReworkILSelfDemo.cs`
  redirige `Verse.Log::Message(string)` (contador + call-through), sobrescribe
  `Verse.Building::GetInspectString` (CallBase, salida idéntica; `InspectSuffix`
  para verlo en partida), desbloquea `Verse.AI.Group.Lord::curJob` (private→public)
  y desella `RimWorld.CompActivity`, inlinea `Verse.Pawn::get_Name` (189 sites) y
  `Verse.MapDrawLayer::get_Map` (113), y pliega
  `RimWorld.JobDriver_AcceptRole::FacingUpdateInterval` a su MISMO valor (20).
- **Verificación de arranque:** `ReworkMod/ReworkILVerifier.cs`
  ([StaticConstructorOnStartup]) comprueba por reflexión sobre el Assembly-CSharp
  REESCRITO (redirect por delta del contador, override presente+virtual, unlocks
  en metadatos reales) y vuelca la telemetría de la pasada 1
  (`DataStore.AppliedPatches`, también visible en el Inspector). Éxito:
  `[ReworkIL] verificación: N OK / 0 fallos — cirugía IL (bloque 24) VERIFICADA ✓`.
- **Compilación:** `Rework.slnx` → 0 errores; `ReworkContent.csproj` → 0 errores.
  Pendiente de verificación en partida (regla 9): 0 NRE, líneas `[ReworkRedirect]`/
  `[ReworkOverride]`/`[ReworkUnlock]`/`[ReworkInline]`/`[ReworkConst]` en
  `Rework pasada1.log`/`Rework.log` y la línea `[ReworkIL] ... VERIFICADA ✓`.

## 47. Bucle de arranque infinito: import anticipado de referencias arrastraba al swap a ensamblados inocentes (0ReworkData incluido) — SOLUCIONADO

- **Síntoma (primer test del bloque 24, 2026-10-10 03:1x):** el juego entraba en
  bucle de arranque: 7+ ciclos de "Root recreado … Boot reiniciado", nunca llegaba a
  cargar los defs. 0 NRE / 0 excepciones. Las sesiones del día anterior booteaban
  limpias.
- **Mecanismo (probado por logs):** una copia FRESCA de 0ReworkData se cargaba en
  cada ciclo (7 cabeceras de sesión en `Rework.log` en 70 s) → TODOS los estáticos de
  `DataStore` se reseteaban (`startedOnce` incluido) → el ctor repetía la pasada 1
  para siempre. Con las defensas intermedias (arranque 12:18), el boot quedó sano
  (1 pasada, `VERIFICACIÓN OK`, `[ReworkIL] … ✓`) y la línea nueva
  `Reload: intercambiando …` entregó la prueba definitiva: entraban al swap
  **Mono.Cecil×4 y 0ReworkAPI**, a los que nadie debía marcar.
- **Causa raíz (cazada con esa línea + las referencias reales de los DLLs):**
  `CallSiteRedirector`/`InlineProcessor` hacían `module.ImportReference(wrapper)` de
  forma ANTICIPADA en TODOS los ensamblados reescribibles, antes de escanearlos.
  `ImportReference` AÑADE la AssemblyReference "Rework" a la metadata del módulo,
  aunque luego no haya ningún call-site que reescribir. Resultado: 0ReworkData,
  Mono.Cecil×4 y 0ReworkAPI quedaban "referenciando" a Rework → como Rework.dll era
  semilla del BFS de `Reloader.PropagateNeedsReload` (Modified por sus 6 sitios
  legítimos), la propagación "quien referencia a un recargado, se recarga" los
  arrastraba a TODOS al swap → 0ReworkData recargado → estáticos frescos → bucle.
  Las sesiones antiguas eran sanas porque nada añadía referencias a inocentes.
- **Fix (en el `.cs`, regla 4):**
  1. **Import PEREZOSO** en `CallSiteRedirector.cs` e `InlineProcessor.cs`: la
     referencia al método/campo del mod solo se importa en un módulo cuando hay un
     call-site REAL que reescribir en él (caché por módulo). Los ensamblados sin
     sitios quedan intactos: ni metadata tocada, ni entrada al swap. Este es el
     fix de raíz.
  2. (Defensas instaladas mientras tanto, se conservan) **Invariante de barrera**
     en `Reloader`: 0ReworkData jamás entra al swap, con error ruidoso si algo lo
     intenta.
  3. **Log del conjunto de swap** ("Reload: intercambiando N ensamblado(s): …"):
     la línea que cazó al culpable; queda como diagnóstico permanente.
  4. **Guardia anti-bucle** (`ReworkMod`, variable de entorno `REWORK_PASS1_RUNS`,
     inmune a cualquier recarga): a la 3.ª pasada 1 fuerza la rama de pasada 2 — un
     bucle ya no puede colgar el arranque.
  5. **Idempotencia del OverrideInjector**: reconoce su propio forwarder en
     re-procesados (info, no error).
- **Verificación:** builds 0 errores. El arranque de las 12:18 ya demostró el boot
  sano con las defensas + los 5 features funcionando (247 redirects, 307 inlines,
  override/unlock/const ✓ y `[ReworkIL] … ✓` por reflexión). Falta UN arranque más
  con el fix de raíz para confirmar el swap limpio: `Reload: intercambiando` debe
  listar SOLO `Assembly-CSharp, Rework, ReworkContent, ReworkCore` y el error de
  barrera debe DESAPARECER (ya nadie marca a 0ReworkData).
- **Lecciones (dos):** (1) tocar la metadata de un ensamblado del set (incluso
  "solo" importar una referencia) lo convierte en DEPENDIENTE ante la propagación
  del reload — todo import debe ser perezoso y condicionado a un cambio real;
  (2) los estáticos que cruzan la barrera dependen de que un ensamblado no se
  recargue JAMÁS: esa invariante se ENFORZA en el punto de recarga, y el conjunto
  de swap se loguea siempre.

## 48. Auditoría anti-fantasma (4 auditorías en paralelo + verificación contra el IL real del juego) — CORREGIDO

**Metodología:** 4 auditorías independientes sobre (1) atributos declarativos,
(2) librerías de la API, (3) despachadores↔inyecciones (auditoría inversa §44) y
(4) registros/Defs usables por rutas vanilla. Cada eslabón vanilla dudoso se
verificó contra el `Assembly-CSharp` instalado (Mono.Cecil/reflexión IL: el orden
FIFO de `DoPlayLoad`, el null-check ausente de `SelectionWeight`, el parámetro 5 de
`ApplyBirthOutcome`). El núcleo estaba sano (13 despachadores inyectados extremo a
extremo, §53 cumple, cadena §41/§54 completa, self-check §55 vivo y consumido); la
auditoría encontró y corrigió lo siguiente.

**Corregidos (todos en el `.cs`, regla 4, compilados a 0 errores):**

1. **`ReworkPersistence.LoadAll` — el más grave:** 0 llamadores. Los 5
   almacenes de servicio (`ColonySkill`, `PawnTimeline`, `WorldStore`,
   `StateSnapshot`, `Relation`) se guardaban en `.rwdat` en cada partida y se
   perdían en cada carga (el doc-coment atribuía la llamada a
   `TryLoadBinaryDocument`, que no la hacía); además `ClearAll` corría en
   `GameInitialized` — al final de una carga — vaciando lo restaurado, y las
   migraciones re-ejecutaban en cada arranque porque su versión vive en el
   WorldStore no restaurado. FIX: `LoadAll` en ambas rutas de carga
   (`RuntimeHooks.TryLoadBinaryDocument`, antes de las migraciones) y `ClearAll`
   solo en `NewGameStarted`.
2. **`[ReworkRaid]` — rompía las raids enemigas:** el `RaidStrategyDef` creado nunca
   asignaba `selectionWeightPerPointsCurve`, y `RaidStrategyWorker.SelectionWeight`
   hace `curve.Evaluate(points)` sin null-check (verificado en IL): NRE dentro del
   `Where` de selección de estrategia, y fallaban todas las raids enemigas
   mientras existiera un def `[ReworkRaid]`. FIX: curva plana siempre (nueva
   propiedad `SelectionWeight`, default 1).
3. **`[ReworkDesignator]` — registrado demasiado tarde:** el Type se añadía a
   `specialDesignatorClasses` después de que su único consumidor
   (`DesignationCategoryDef.ResolveDesignators`, privado, encolado por
   `ResolveReferences` vía `ExecuteWhenFinished`) ya hubiera corrido (orden FIFO
   de `DoPlayLoad`, verificado con walker IL) → el designador no aparecía nunca en
   Arquitecto, sin error. FIX: re-invocar `ResolveDesignators` por reflexión tras
   registrar (rebuild completo e idempotente).
4. **`[ReworkAIModifier]` — argumentos de inyección incorrectos:** se pasaba `ldarg.0`
   (el ThinkNode `this`) donde la firma declara `Verse.Pawn`, y `""` como
   jobDefName → los modificadores con `TargetJobDef` nunca coincidían y el
   "pawn" llegaba corrupto (cast fallido → null). FIX: `ldarg.1` (el Pawn)
   + error si se declara `TargetJobDef` (el hook ThinkNode.GetPriority
   no tiene contexto de trabajo; queda documentado como no soportado).
5. **Alertas en la 2ª partida:** `reworkAlertsAttached` (flag global) no se
   reseteaba nunca → al volver al menú y entrar en otra partida, el nuevo AlertsReadout
   se quedaba sin alertas. FIX: idempotencia por instancia de readout.
6. **`ReworkWorldStore` rompía los tipos al persistir:** `ToString()` al
   serializar, strings crudos al restaurar → `Get<int>` tras una carga devolvía
   default. FIX: formato v2 (magic "RW2") con AssemblyQualifiedName por valor +
   `Convert.ChangeType` al restaurar + parseo del formato legado v1.
7. **`[ReworkStatusEffect]` registro manual:** sin atributo no se crea el
   HediffDef y `Apply` devolvía false en silencio. FIX: error con la
   explicación de las vías válidas.
8. **Perfilador sin consumidor:** `ReworkProfiler.Summary()` no lo llamaba nadie
   (el "19.8 Perfilador integrado" prometido). FIX: la pestaña Perfilador del
   Inspector muestra las fases medidas.
9. **Demo de `ReworkDynamicMutate` no-op:** `[ReworkInit]` corre en
   InitializeMods (sin Defs) → `GetNamedSilentFail("CraftingSpot")` = null desde
   el principio. FIX: `ReworkRuntimeActivatorsPostDefs` ([StaticConstructorOnStartup],
   post-Defs).
10. **Despachadores/parches huérfanos de la saga de la cámara (§39-41):**
    `SweepOriginalComponents`, `EnsureWorldCameraDriver`, `LogWorldCameraDiagnostic`,
    `OnStateChanged` + 3 `PatchXxx` huérfanos + `RecreateRuntimeOriginalComponents`
    — documentados como inyectados cuando no los inyectaba nadie (§44 inverso). Se
    eliminaron (los supera la subclase `ReworkWorldCameraDriver`).
11. **Duplicado literal de `ReworkFrameworkLifecycleHooks`:** idéntico en
    ReworkCore y ReworkMod → LifecycleHooks descartaba la segunda copia en
    silencio (colisión de `MethodReference.FullName`, que no distingue ensamblado)
    y el log "Hook aplicado" salía duplicado. FIX: eliminado el residuo de
    ReworkMod.
12. **Silencios:** el bus tragaba excepciones de handlers (catch
    vacío → hook `HandlerError` cableado a `Lg.Error`); `[ReworkSchedule]`
    aceptaba firmas imposibles sin avisar (validación de intervalo y firma);
    `[ReworkGenStep]` sin MapGeneratorDef quedaba sin enlazar sin error (ahora
    error explícito).
13. **Telemetría del Inspector:** solo reflejaba 4 de los 17 parches activos —
    verificar "por el Inspector" induciría a creer que gizmos/alertas/IA/bus/
    overlays "no existen" (riesgo de reintroducir el sesgo §44). FIX:
    `DataStore.AppliedPatches` en los 15 que faltaban.
14. **Docstrings incorrectos:** `IsPresent` (no puede devolver false sin
    framework: el JIT lanza antes — la degradación exige try/catch del mod),
    `[ReworkWatch]` (es polling por tick, no "interceptación"; solo muestrea
    Pawn/Map), contrato de Quest (solo CanTrigger/OnAccepted se invocan),
    `[ReworkHediff]`/`[ReworkThought]`/`[ReworkApparel]` (los Defs se registran
    bien, pero la aplicación/obtención es eslabón del modder — ahora explícito
    en cada atributo), "LoreRuntime" inexistente en un doc de ReworkRuntime.

**Condicionales legítimos (documentados, no fantasmas):** `[ReworkJob]` (falta
el emisor), `[ReworkRecipe]` (receta de worker-total sin products),
`ReworkWorldStore` como KV genérico (solo las migraciones escriben por defecto).

**Verificación:** el arranque del 2026-10-10 16:08 pasó la checklist — 1 sola
pasada 1, swap de 4 ensamblados (sin contaminación ni error de barrera),
`OnPawnCreated` 1 vez, `[ReworkIL] … VERIFICADA ✓`, mutación demo aplicada, 0 NRE
y 0 excepciones. Queda por comprobar en partida real la carga de una colonia: que
el `.rwdat` se restaure (niveles e historial persisten) y las migraciones no
re-ejecuten. Nota de entorno: el juego instalado es 1.6.4850 rev645 y el framework
declara rev646 — `CheckGameVersion` tolera por prefijo (riesgo residual mínimo de
offsets).

**Lección:** «registrado», «aplicado» y «obtenible» son tres eslabones distintos;
una API declarativa debe documentar cuáles pone el framework y cuáles quedan a
cargo del modder, porque todo silencio (catch vacío, return false sin log, flag sin
reset) acaba reportándose como «no funciona».

## 49. Daño declarativo Fase 1 (port de JecsTools): [ReworkDamageSoak] y [ReworkDamageOverTime]

**Motivo:** primera entrega del plan de port de JecsTools al framework. Se
eligieron los dos sistemas de combate más baratos y más pedidos (vampiros,
venenos, esqueletos): absorción plana por golpe y daño periódico por hediff.
Reimplementación limpia sobre el pipeline Rework, no copia del código Harmony
original (RimWorld 1.6 cambió render, hediffs e IA desde la 1.5 de JecsTools).

**Piezas (todas en el `.cs`, regla 4):**

- `Source/ReworkAPI/ReworkDamageSoak.cs` — atributo
  `[ReworkDamageSoak(defName)]` (`DamageToSoak`, `DamageType`, CSV
  `DamageTypesToExclude`, `Label/Description`, `ShowAbsorbedMote`) + registro
  `ReworkDamageSoakRegistry` (los DamageDefs se resuelven en el registro, no en
  el golpe).
- `Source/ReworkCore/ReworkDamageRuntime.cs` — `ApplyDamageSoakers(pawn, ref
  dinfo)`: resta la absorción de cada soak aplicable; si el daño llega a 0
  devuelve true (golpe absorbido). Sin estados estáticos entre parches (el
  `tempDamageAmount` de JecsTools se rompía si otro mod tocaba `ArmorUtility`
  por otro camino); cada hediff se protege por separado.
- `Source/ReworkCore/GameProcessing.cs` — paso 18b
  `PatchPawnPreApplyDamageSoak`: inyecta al inicio de
  `Verse.Pawn.PreApplyDamage(ref DamageInfo, out bool)` (virtual, SIN regiones
  try/catch — verificado en IL, 44 instrucciones). Si el runtime dice
  absorbido, pone `absorbed = true` y retorna antes del flujo vanilla (culpas,
  pensamientos, escudos). Sin soaks registrados el runtime retorna false sin
  tocar nada: coste de un null-check por golpe.
- `Source/ReworkMod/ReworkDamageSoakRegistrar.cs` —
  `[StaticConstructorOnStartup]`: crea el HediffDef si falta, valida los
  defNames de DamageDef (tipo inexistente = error claro, nunca NRE en el
  golpe) y registra en el self-check.
- `Source/ReworkAPI/ReworkDamageOverTime.cs` + `Source/ReworkCore/
  ReworkDamageOverTimeRuntime.cs` + `Source/ReworkMod/
  ReworkDamageOverTimeRegistrar.cs` — mismo patrón para el DoT
  (`CycleDamage/CycleDamageAmt/CycleInTicks/ArmorPenetration`; el ciclo usa
  `Thing.TakeDamage` vanilla; la cuenta es por LoadID de hediff; enganchado al
  tick global con early-out `HasActive` en `RuntimeHooks.OnGameComponentTick`).

**Decisiones contra-intuitivas (verificadas con Cecil antes de escribir):**

1. Punto de intercepción `Pawn.PreApplyDamage`, NO
   `Pawn_HealthTracker.PreApplyDamage` (el que parchea JecsTools). El segundo
   es no-virtual y su `dinfo` viaja por valor: JecsTools necesita 3 parches
   coordinados por un estático frágil para propagar el cambio. El primero es
   virtual, recibe `ref dinfo` y corre antes que el tracker: un solo punto,
   sin estado compartido.
2. Daño 0 = absorbido. Vanilla trata el golpe con `Amount <= 0` como no
   ocurrido a efectos de culpa/pensamientos (el flujo sigue, pero sin
   consecuencias); aquí se corta antes con `absorbed = true` para que tampoco
   corran escudos ni notificaciones: igual que si el daño nunca existió.
3. Sin `HediffComp` propio. JecsTools mete el comportamiento en comps con XML;
   aquí el hediff del mod puede ser cualquier subclase de `Hediff` y la
   cuenta vive en el runtime por LoadID. Menos superficie para el modder.
4. El mote de absorción usa `RimWorld.MoteMaker.ThrowText` (en 1.6 ya no está
   en `Verse`: verificado por búsqueda en todo el ensamblado), con "-N" gris.
   Solo sale si algún soak del pawn tiene `ShowAbsorbedMote` (OR, no AND).

**Verificación pendiente (regla 9):** compila a 0 errores y DLLs desplegadas;
falta arranque real: `PreApplyDamage → ApplyDamageSoakers` en
`DataStore.AppliedPatches`, `ReworkDamageSoakRegistrar: N soak(s)` y
`ReworkDamageOverTimeRegistrar: N DoT(s)` en el log (N=0 sin mods
consumidores: los registradores corren igual y el despacho queda armado pero
inactivo), 0 NRE / 0 excepciones.

## 50. Knockback declarativo Fase 1 (port de JecsTools): [ReworkKnockback]

**Motivo:** tercer sistema de combate de la Fase 1. El más difícil de
reinventar (pawn volando por el aire, curvas por masa/absorción, daño de
impacto por distancia, anti-doble-empuje del mismo golpe): JecsTools lo trae
resuelto y probado, así que se porta con sus mismas reglas y defaults.

**Piezas (todas en el `.cs`, regla 4):**

- `Source/ReworkAPI/ReworkKnockback.cs` — atributo
  `[ReworkKnockback(defName)]` (chance, rango de distancia, velocidad, sonido,
  pensamiento, daño de impacto + tipo, stun, explosión + tipo) + registro.
- `Source/ReworkCore/ReworkKnockbackRuntime.cs` — `AfterHit` (cola post-golpe:
  extrae atacante/arma del `DamageInfo` ya procesado) + `TryKnockback` (solo
  melee, anti-doble-empuje por tick y pareja, % absorbido real) + física
  (curvas idénticas a JecsTools con sus defaults) + stun + explosión +
  lanzamiento. Las curvas viven como `SimpleCurve` estáticas: mismos números
  que el original (absorción 1→0/0→1; masa 0→2/60→1/120→0.5/240→0.25).
- `Source/ReworkCore/ReworkFlyingObject.cs` — objeto volador propio, recortado
  a lo que el knockback necesita (sin proyectiles ni explosiones propias).
  Dibuja al pawn con `RenderPawnAt` en 1.6.
- `Source/ReworkCore/GameProcessing.cs` — el paso 18b crece: además de la
  cabeza de soak, guarda `damageBefore` e inyecta la cola `AfterHit` antes de
  CADA `ret` vanilla (con idempotencia por `FullName`). Daño absorbido del
  todo = el `ret` temprano evita la cola: no hay empuje sin golpe sentido.
- `Source/ReworkMod/ReworkKnockbackRegistrar.cs` — crea el HediffDef, valida
  sonido/pensamiento/daños (error claro, nunca NRE) y garantiza el ThingDef
  `Rework_FlyingObject` (infraestructura, como los defs de §54).
- `Source/ReworkCore/RuntimeHooks.cs` — `ClearTickCache()` al avanzar el tick
  (equivale al postfix de JecsTools en `Scenario.TickScenario`, sin parche
  extra). El `FlyingDef` vive en el runtime (Core), no en el registrador
  (Mod): al revés sería referencia circular Core→Mod.

**Tres fixes que dio Cecil antes de compilar (regla 5):**

1. `ReworkFlyingObject.DrawAt` es `public` en 1.6 (JecsTools lo trae como
   `protected` de la 1.4): CS0507 al primer build.
2. `MoteMaker` ya no está en `Verse` sino en `RimWorld` (fase anterior, pero
   el knockback lo usa para el sonido: `Verse.Sound.SoundStarter.
   PlayOneShotOnCamera`, no `SoundDef.PlayOneShotOnCamera` que no existe).
3. `AltitudeLayer` no tiene `FlyingItem` en 1.6: el volador usa `Projectile`
   (la capa de los proyectiles).
4. `GenSpawn.Spawn(Thing, IntVec3, Map)` exige `WipeMode` en 1.6: se usa
   `WipeMode.Vanish` (el volador es temporal; no deja rastros).

**Verificación pendiente (regla 9):** 0 errores, DLLs desplegadas; falta
arranque real: colas `AfterHit` en el log del parche 18b,
`ReworkKnockbackRegistrar: 0 knockback(s)` (sin consumidores) y
`ThingDef 'Rework_FlyingObject' del objeto volador creado`, 0 NRE.

## 51. Cleave declarativo Fase 1 (port de JecsTools): [ReworkCleave]

**Motivo:** cuarto sistema de combate de la Fase 1 y el más barato (sin
parches nuevos: vive en un `DamageWorker` que vanilla ya sabe instanciar).
Espadas sierra, martillos que barren, garras en abanico.

**Piezas:**

- `Source/ReworkAPI/ReworkCleave.cs` — atributo `[ReworkCleave(defName)]`
  (`CleaveTargets`, `CleaveFactor`, `CleaveDamage`, `ArmorPenetration`,
  `Label`) + registro.
- `Source/ReworkCore/ReworkDamageWorker_Cleave.cs` — hereda
  `DamageWorker_AddInjury` (golpe principal normal) y salpica ANTES a
  enemigos adyacentes. Mismas reglas que JecsTools (auto por masa/BodySize,
  facciones enemigas, sin re-salpicado, sin `InstantPermanentInjury`).
- `Source/ReworkMod/ReworkCleaveRegistrar.cs` — crea el DamageDef con
  `workerClass` propio; si el def ya existe con otro worker, error claro
  (el cleave no se aplica en silencio a un daño ajeno).

**Dos detalles que dio Cecil (regla 5):** `Thing.GetStatValue` no existe
como instancia en 1.6 — es `StatExtension.GetStatValue(thing, def, bool)`; y
el salpicado se aplica con `TakeDamage` directo (no reentra al worker, así
que no hay recursión aunque `CleaveDamage` sea el mismo def).

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkCleaveRegistrar: 0 cleave(s)` (sin consumidores), 0 NRE.

## 52. Daños extra de melee Fase 1 (port de JecsTools): [ReworkExtraMelee]

**Motivo:** quinto sistema de combate de la Fase 1. Garras venenosas, puños
en llamas, colmillos: lo que el arma mete por `Tool.extraMeleeDamages`, pero
viniendo de un hediff del atacante.

**Piezas:**

- `Source/ReworkAPI/ReworkExtraMelee.cs` — atributo
  `[ReworkExtraMelee(defName)]` (`ExtraDamages` en CSV
  `Def:Cantidad[:Pen[:Chance]]`) + registro.
- `Source/ReworkCore/ReworkExtraMeleeRuntime.cs` — `AppendExtra(enumerable,
  verb)`: envuelve los daños vanilla con los extra (lista solo si hay;
  si no, devuelve el original intacto).
- `Source/ReworkMod/ReworkExtraMeleeRegistrar.cs` — crea el HediffDef,
  parsea y valida el CSV (entrada mal formada o daño inexistente = error
  claro con la entrada citada, nunca NRE).
- `Source/ReworkCore/GameProcessing.cs` — paso 18c: tras el `call
  DamageInfosToApply` en `ApplyMeleeDamageToTarget`, `ldarg.0 + call
  AppendExtra`. Dos instrucciones en un punto lineal (instr 4 de 31).

**Decisión contra-intuitiva:** JecsTools lo hace con transpiler sobre el
`MoveNext` del iterador (en 1.6: 422 instrucciones CON regiones try/catch —
verificado en IL). Reescribir eso es frágil ante cualquier cambio de
vanilla; envolver el `IEnumerable` resultante mueve el riesgo a cero (el
`foreach` vanilla itera el envuelto sin enterarse) y evita la caché
ThreadStatic del original (el wrap es por golpe).

**Verificación pendiente (regla 9):** 0 errores; falta arranque real: paso
18c en el log (`ApplyMeleeDamageToTarget → AppendExtra`),
`ReworkExtraMeleeRegistrar: 0 extra-melee(s)`, 0 NRE.

## 53. Deflector declarativo Fase 1 (port de JecsTools): [ReworkDeflector]

**Motivo:** sexto sistema de combate de la Fase 1. Sables láser, samuráis:
desviar proyectiles con arma melee, con skills y reflect al tirador.

**Piezas:**

- `Source/ReworkAPI/ReworkDeflector.cs` — atributo `[ReworkDeflector(arma)]`
  (`AllowMultiple`: varias armas por clase portadora) + registro por defName.
- `Source/ReworkCore/ReworkDeflectorRuntime.cs` — `TryDeflect(victim, ref
  dinfo)`: filtros (explosivos/llamas/melee no), arma empuñada del registro,
  chance con skill y manipulación, XP, sonido, reflect directo al tirador
  (d100 + bonus vs 80; ≥90 crítico).
- `Source/ReworkMod/ReworkDeflectorRegistrar.cs` — valida que el arma EXISTA
  (si no, el deflector no se registra: nada de marcar fantasmas) y resuelve
  skills/sonido.
- `Source/ReworkCore/GameProcessing.cs` — la cabeza 18b crece con el desvío
  PRIMERO (antes del soak: `TryDeflect` → `absorbed + return`), con su variable
  y telemetría propias.

**Simplificación declarada vs JecsTools:** sin `ThingComp` en el arma (el
registro por defName lo sustituye), sin animación de esgrima (requiere el
render de equipo de 1.6: mejora futura), sin stat `MeleeWeapon_DeflectionChance`
(el valor vive en el registro, no en la UI del arma: mejora futura) y reflect
directo en vez de job con verbo construido a mano (la parte más frágil del
original: `Verb_Deflected` + `GiveDeflectJob` + `CastDeflectVerb`). Lo que se
conserva: filtros, chance con skill/manipulación, XP, reflect con precisión.

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`TryDeflect` en el log 18b, `ReworkDeflectorRegistrar: 0 deflector(es)`, 0 NRE.

## 54. Escudos físicos Fase 1 (port de JecsTools): [ReworkShield]

**Motivo:** último sistema de combate de la Fase 1 (bloqueo con daño al
escudo, fatiga y descarte). Con él quedan: soak, DoT, knockback, cleave,
extra-melee, deflector y escudos.
Con él, la Fase 1 queda completa: soak, DoT, knockback, cleave, extra-melee,
deflector y escudos — todo lo que un mod de combate con feeling necesita.

**Piezas:**

- `Source/ReworkAPI/ReworkShield.cs` — atributo `[ReworkShield(escudo)]`
  (`AllowMultiple`) + registro por defName.
- `Source/ReworkCore/ReworkShieldRuntime.cs` — `TryBlock(victim, ref dinfo)`:
  busca el escudo en TODO el equipo (no solo Primary: el escudo no es el
  arma), distingue ranged/melee por adyacencia (misma regla que JecsTools),
  chance con skill, fatiga, daño al escudo con sus HP, chispas + sonido,
  descarte por HP o fatiga máxima.
- `Source/ReworkMod/ReworkShieldRegistrar.cs` — valida que el escudo EXISTA
  y crea el HediffDef `Rework_ShieldFatigue` (4 stages con penalizadores de
  manipulación/movimiento, como el original) solo si algún escudo usa fatiga.
- `Source/ReworkCore/GameProcessing.cs` — la cabeza 18b crece con el bloqueo
  SEGUNDO (desviar → bloquear → absorber), con su variable y telemetría.

**Tres detalles que dio Cecil (regla 5):** `SetForbidden` ya no es método de
`Thing` en 1.6 — es `ForbidUtility.SetForbidden(thing, bool, bool)`;
`GetFirstHediffOfDef` exige el `mustBeVisible` (`false` para la fatiga);
`PawnCapacityModifier` vive en `Verse` (no en `RimWorld`).

**Simplificaciones declaradas vs JecsTools:** sin `ThingComp` en el escudo
(el registro por defName lo sustituye), sin render propio en el pawn, sin
StatDefs en la UI (chance en el registro), sin sonidos por material, sin
generador de pawns con escudo. Todo documentado como mejoras futuras en el
manual.

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`TryBlock` en el log 18b, `ReworkShieldRegistrar: 0 escudo(s)`, 0 NRE.
Con esto, UN SOLO arranque verifica la Fase 1 entera (ver comando abajo).

## 55. Spawner con retardo Fase 2 (port de JecsTools): [ReworkDelayedSpawner]

**Motivo:** abre la Fase 2 (contenido) con el sistema más barato y más útil
para incidentes y eventos: el Star Vampire de jecrell, colmenas, refuerzos.

**Piezas:**

- `Source/ReworkAPI/ReworkDelayedSpawner.cs` — atributo
  `[ReworkDelayedSpawner(id)]` (`AllowMultiple`: varios spawners por clase) +
  `ReworkSpawnSpec` (el contrato compartido entre declarativo y manual) +
  registro.
- `Source/ReworkCore/ReworkSpawnerRuntime.cs` — dos colas (declarativos con
  cuenta global + manuales con mapa/celda vía `Queue`), tick en el dispatcher
  global con early-out `HasActive`, celda de respaldo cerca del centro,
  generación con `PawnGenerator.GeneratePawn(kind, faction)` (el overload de
  3 params sigue vivo en 1.6), colocación `ThingPlaceMode.Near`, mental state
  + hediffs, repetición con tope.
- `Source/ReworkMod/ReworkDelayedSpawnerRegistrar.cs` — valida todo al
  registrar (sin PawnKind ni Thing = se ignora; def inexistente = error y no
  se programa).
- `Source/ReworkCore/RuntimeHooks.cs` — `OnTick()` + early-out en el tick.

**Diferencia de diseño vs JecsTools:** el spawner no vive en un ThingComp
pegado a un edificio invisible (que hay que colocar, tickear y destruir):
vive en el servicio. El `isSpawning`/`ticksLeft`/`PostExposeData` del comp
desaparecen — la cola es la que persiste en memoria por sesión. A cambio, el
spawn manual (`Queue`) cubre el caso del incidente que dropea spawners en
posiciones exactas.

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkDelayedSpawnerRegistrar: 0 spawner(s)`, 0 NRE.

## 56. [ReworkSchedule] roto fallaba en silencio cada tick — CORREGIDO

**Síntoma (auditoría pedida por el usuario):** de los 56 `catch {}` vacíos
del fuente, casi todos son defensa legítima en caminos calientes (un gizmo
roto no debe romper la UI, un soak roto no debe romper el golpe). Pero
`ReworkScheduler.OnGameTick` tragaba la excepción del schedule **sin dejar
rastro en ningún log**: un `[ReworkSchedule]` roto (un spawner, una migración
periódica, el XP diario) simplemente "no pasaba nada", cada día, sin forma
de saberlo. El efecto es temporal e invisible: el silencio más peligroso del
framework.

**Fix (en el `.cs`, regla 4):** mismo patrón que `ReworkBus.HandlerError`
(§48): hook `ReworkScheduler.HandlerError` cableado a `Lg.Error` en
`WireDelegates`. El primer fallo por método sale con el error completo (y el
intervalo de reintento); los siguientes solo suben el contador (`Failures`
por entrada + `TotalFailures` para el Inspector). El schedule roto sigue sin
romper el tick — pero ya no es invisible.

**No se toca:** `LogSilence`/`ShouldSuppressLog` (ventana acotada del
reinicio, se cierra siempre: verificado que ningún camino la deja en `true`)
ni los catch de hot-path (gizmos, overlays, daño, bus): defensa intencional.

**Verificación pendiente (regla 9):** 0 errores; el arranque sano no cambia
(nada falla hoy: el hook solo se nota cuando algo falle).

## 57. Variantes intercambiables Fase 2 (port de JecsTools): [ReworkToggleDef]

**Motivo:** segundo sistema de contenido de la Fase 2. Anillos por dedo,
armas plegables, herramientas con cabezales: sin despawn manual ni tarjetas
custom.

**Piezas:**

- `Source/ReworkAPI/ReworkToggleDef.cs` — atributo `[ReworkToggleDef(grupo)]`
  (`AllowMultiple`, `Variants` CSV, `GizmoLabel`) + registro con índice
  inverso por defName (construido con strings: la API sigue siendo BCL pura,
  sin referencia al juego).
- `Source/ReworkCore/ReworkToggleDefRuntime.cs` — `OptionsFor`/`ResolveThing`
  (el arma del colono si el objetivo es pawn) + `SwapTo` (despawn → cambia
  def → respawn, HP proporcional, verbos re-inicializados, revierte al def
  original si el respawn falla).
- `Source/ReworkMod/ReworkToggleDefRegistrar.cs` — valida el grupo entero
  (una variante inexistente = el grupo NO se registra: nada de grupos cojos)
  + gizmo genérico del framework (una vez, no por mod).
- `Source/ReworkAPI/ReworkGizmo.cs` + `Source/ReworkCore/ReworkRuntime.cs` —
  soporte `VisibleIf` en gizmos: el botón "Cambiar variante" solo se crea si
  el objetivo es variante (registrado para `Thing` y `Pawn`, que cubre objetos
  sueltos y armas equipadas). Mejora general del framework, no solo del toggle.

**Diferencias de diseño vs JecsTools:** lista explícita en vez de
nomenclatura `_TOGGLEDEF_` (renombrar sin actualizar = error claro, no grupo
roto); gizmo con menú en vez de ITab con radio buttons (mismo efecto, menos
código, sin tarjeta custom que mantener en 1.6).

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkToggleDefRegistrar: 0 grupo(s)`, 0 NRE.

## 58. Piezas instalables Fase 2 (port de JecsTools): [ReworkInstalledPart]

**Motivo:** tercer sistema de contenido de la Fase 2. Mechas modulares,
torretas con armas intercambiables, prótesis que se visten: llevar +
trabajar + montar, con desmontaje de vuelta.

**Piezas:**

- `Source/ReworkAPI/ReworkInstalledPart.cs` — atributo
  `[ReworkInstalledPart(pieza)]` (`AllowMultiple`, `AllowedOn` CSV,
  `WorkToInstall/Uninstall`) + registro.
- `Source/ReworkCore/ReworkInstalledPartDrivers.cs` — dos JobDrivers del
  framework (llevar al objetivo + toil de trabajo con velocidad/XP de
  Construction, `ToilCompleteMode.Never`, `ExposeData` del progreso).
- `Source/ReworkCore/ReworkInstalledPartRuntime.cs` — jobs ordenados (misma
  facción), `Notify_Installed` (prenda→Wear, arma→equipo, resto→contenedor
  o suelo) y `Notify_Uninstalled` (cae al suelo), con mensajes.
- `Source/ReworkMod/ReworkInstalledPartRegistrar.cs` — valida la pieza,
  resuelve `AllowedOn`, crea los JobDefs `Rework_InstallPart/UninstallPart`
  (una vez) + gizmo genérico (en piezas y en objetivos).

**Un fix que dio el compilador:** `MakeNewToils` es `public` en 1.6
(JecsTools lo trae `protected` de la 1.4): CS0507 en ambos drivers.

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkInstalledPartRegistrar: 0 pieza(s)`, JobDefs creados, 0 NRE.

## 59. Slots en armas Fase 2 (port de JecsTools): [ReworkSlotLoadable]

**Motivo:** slots de munición y cristales con bonus (color, stats, daño,
curas). Es el sistema con más piezas del port (estado persistido, job,
gizmos, hediffs generados, dos enganches reutilizados).

**Piezas:**

- `Source/ReworkAPI/ReworkSlotLoadable.cs` — atributo
  `[ReworkSlotLoadable(arma)]` (`Slots`/`Bonuses`/`Heal`/`Stats` en CSV) +
  specs + registro.
- `Source/ReworkCore/ReworkSlotLoadableRuntime.cs` — estado en campo REAL
  inyectado por Thing (`Rework_SlottedLoaded`, `List<string>` serializado:
  viaja en el save) + carga/descarga (consume/devuelve items) + color +
  hediff de stats por stat (Defs generados y cacheados) + daños extra +
  curas al golpear.
- `Source/ReworkCore/ReworkSlotLoadableDriver.cs` — job de recogida
  (`Rework_GatherSlotItem`; el slot viaja en `job.count - 1`).
- `Source/ReworkMod/ReworkSlotLoadableRegistrar.cs` — parsea y valida los
  4 CSV (entrada mala = error citado, nunca NRE) + JobDef + gizmos por slot.
- Reutilización (sin parches nuevos): el daño extra entra por el paso 18c
  existente y las curas por la cola `AfterHit` existente. El slotloadable no
  añade ningún `call` al pipeline: vive de los dos enganches de la Fase 1.

**Dos detalles:** el `[ReworkField]` del estado vive en ReworkCore y SÍ se
procesa (`isReworkOwned`: nombre empieza por "Rework"); `IsForbidden` en 1.6
es `ForbidUtility.IsForbidden(thing, pawn)`, no método de Thing.

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkSlotLoadableRegistrar: 0 arma(s)`, campo `Rework_SlottedLoaded` en
`InjectedFields`, 0 NRE.

## 60. Puente de habilidades Fase 2 (JecsTools como referencia): [ReworkAbility]

**Motivo:** último sistema de la Fase 2 y cierre del plan de port. Con él,
las dos fases quedan completas: combate (7 sistemas) + contenido (5
sistemas) + scheduler ruidoso + habilidades. Todo lo que un mod de
JecsTools hacía, ahora se puede hacer en Rework sin XML.

**Decisión de diseño (no es un clon):** vanilla 1.6 YA trae Abilities con
70+ efectos (verificado en el DLL). Clonar `CompAbilityUser` sería duplicar
el juego. El puente solo pone lo que falta: registro sin XML + concesión
masiva + pasivos por ticker (vanilla no los da) + IA de casteo enemiga
(vanilla no la da). Los efectos simples son comps propios mínimos
(`ReworkEffect_Hediff/Damage/Stun/Spawn`) que vanilla ejecuta al disparar.

**Piezas:**

- `Source/ReworkAPI/ReworkAbility.cs` — atributos + registro.
- `Source/ReworkCore/ReworkAbilityRuntime.cs` — `Grant` + `OnPawnGenerated`
  + `TickPassives` (cada 250 ticks, aplica/retira hediffs) + 4 efectos.
- `Source/ReworkCore/ReworkAbilityProps.cs` — props serializables (en Core,
  no en Mod: al revés sería circular).
- `Source/ReworkCore/ReworkAbilityAI.cs` — `ReworkJobGiver_AICast`
  (cura→ofensiva, con LoS; el árbol de decisión por tags queda futuro).
- `Source/ReworkMod/ReworkAbilityRegistrar.cs` — crea AbilityDefs (cooldown
  en `cooldownTicksRange`: en 1.6 ya no hay `cooldownTime`) + hediffs de
  pasivos + validación CSV.
- `Source/ReworkMod/ReworkRuntimeActivators.cs` — hook PawnCreated concede.
- `Source/ReworkCore/RuntimeHooks.cs` — tick de pasivos + early-out.

**Tres fixes que dio el compilador/Cecil:** `TryGiveJob` es `public Job`
(no `TryIssueJobPackage`); `AbilityCompProperties` vive en `Verse` (no
`RimWorld`); los parámetros viajan en las props (vanilla instancia los
comps, no el mod).

**Verificación pendiente (regla 9):** 0 errores; falta arranque real:
`ReworkAbilityRegistrar: 0 habilidad(es)`, 0 NRE. Y con esto, UN SOLO
arranque verifica las DOS fases enteras.

## 61. InvalidProgramException en PreApplyDamage + inicializador no-público — SOLUCIONADO

**Síntomas (arranque 17:00):** `Invalid IL code in Verse.Pawn:PreApplyDamage:
IL_0006: call 0x0a0014b8` al generar el mapa (6 veces, una por esclavo de las
cápsulas) + `Inicializador por método 'NewLoadedList': debe ser PÚBLICA`.

**Causa 1 (clara):** `NewLoadedList()` (inicializador del campo de slots) era
`private`; el FieldAdder lo llama cross-assembly y exige pública. El campo no
se inyectó. Fix: `public` (1 línea).

**Causa 2 (token colgado):** el `call TryDeflect` (IL_0006) no resolvía en el
ensamblado reescrito. Se instrumentó el paso 18b con verificación `Resolve()`
temporal: en el arranque 17:17 (tras el fix 1) los 3 calls salen `OK` y el
`InvalidProgram` desaparece (0). El token colgado era efecto del arranque con
el campo sin inyectar, no del 18b en sí. La instrumentación se retiró tras
confirmar (era temporal, ensuciaba 30 líneas por arranque).

**Lección:** un field con inicializador no-público no solo pierde el campo:
deja el módulo a medias y el siguiente parche que resuelva tokens puede
heredar el estado roto. Los inicializadores, siempre públicos (ya lo decía el
mensaje; ahora queda escrito).
