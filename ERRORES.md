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
nunca se esconden. Es el equivalente al `SilenceLogging` de Prepatcher, pero **sin Harmony
y sin parchear métodos del juego** (solo se envuelve el manejador de logs).

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
   sustituyen la mecánica Prefix/Postfix de Harmony (descartada: inferior, solo
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
- Aislamiento estricto de `0Harmony.dll` y ensamblados de Harmony en `Loader.cs` y `ModifiableAssembly.cs`: marcados como ensamblados de solo-lectura protegidos (`AllowPatches = false`, `ProcessAttributes = false`). Nunca se intentan duplicar ni sobreescribir.
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