# Rework Reforjed — Manual

Framework de parcheo pre-carga para RimWorld: reescribe `Assembly-CSharp.dll` **en memoria**
con Mono.Cecil **antes** de que el juego la use, así el código parcheado corre como bytecode
CLR normal (modelo de Prepatcher). No usa ni se integra con librerías de intercepción de métodos
en runtime (trampolines, prefixes/postfixes): no hay nada que combinar con ellas. Portable (una
carpeta en `Mods/`), autocontenido. Target exclusivo: **RimWorld 1.6.4850 rev646**.

---

## Qué hace (modelo de doble pasada en memoria)

```
Arranque del juego
  └─ Pasada 1 (nuestro constructor de mod):
       • recoge Assembly-CSharp + nuestros ensamblados (ReworkAPI, ReworkData, ReworkCore, Rework)
       • reescribe en memoria con Mono.Cecil:
           - añade campos [ReworkField] (p. ej. Verse.Pawn.Rework_Test) y reescribe los accessors
           - añade métodos/propiedades [ReworkMethod]/[ReworkProperty] (forwarders al mod)
           - añade atributos [ReworkAnnotate]
           - engancha [ReworkHook] (ciclo de vida) e inyecta [ReworkInit]
           - redirige el driver de la cámara de mundo a una subclase única (ReworkWorldCameraDriver)
           - guardias de música / re-creación de escena / Root
       • marca los originales como ReflectionOnly (los "esconde")
       • carga las copias ya parcheadas y aborta el hilo
  └─ Reinicio interno en el sitio: el juego re-arranca su secuencia con tipos nuevos
       └─ Pasada 2 (verificamos y no reescribimos nada)
```

El proceso del juego **no se cierra**: solo ves la pantalla de carga reiniciarse una vez
(igual que Prepatcher). El `Assembly-CSharp.dll` del disco queda **intacto**.

## Detección de entorno y modo seguro

Antes de reescribir nada, Rework detecta el runtime:

- **1.7 — ¿es Mono?** `Mono.Runtime.GetDisplayName()` → loguea `Runtime=Mono, Mono=X.Y.Z`.
- **1.8 — ¿el layout nativo sigue siendo el esperado?** los offsets que usa `UnsafeAssembly`
  (`+0x60 → MonoImage`, `+0x10 → raw_data`, `+0x18 → length`) se verifican ANTES de confiar
  en ellos. Si la build de Mono cambió los offsets → `layoutOK=False`.
- **1.10 — modo seguro PROACTIVO:** si el runtime no es Mono o el layout no verifica, Rework
  **no reescribe nada**: el juego arranca vanilla y Rework solo advierte.
- **1.2/1.3 — ensamblados del motor en "contexto seguro":** `UnityEngine*.dll`, `Unity.dll`
  y `Mono.CSharp` se añaden al set solo para resolver referencias; nunca se marcan refonly
  ni se recargan.

Verificado en el arranque:
`Entorno: Runtime=Mono, Mono=6.13.0 (Visual Studio built mono), layoutOK=True → seguro para reescribir`

## Instalación

1. Copia la carpeta `Rework Reforjed` en `RimWorld\Mods\`.
2. En el menú **Mods** de RimWorld, actívalo.
3. **Orden de carga:** arrastra `Rework Reforjed` **arriba del todo**, justo debajo de
   `Core`/expansiones.
4. Jugador: la primera apertura reinicia la carga en el sitio automáticamente.

## Desinstalar

Solo desactiva el mod en el menú Mods (el DLL de disco nunca se tocó). El archivo
`Rework.log` en LocalLow puede eliminarse; se regenera solo.

---

## Características

### Campos [ReworkField] — tipos arbitrarios (bloque 2)

Añade campos **reales** a **cualquier clase del juego** (primitivas, `string`, clases,
genéricos, colecciones). El nombre del campo es EXACTAMENTE el nombre del método accessor.
Verificado en las 23 clases del bloque 2 (Pawn, Thing, Building, Map, MapComponent,
GameComponent, ThingComp, WorldComponent, Settlement, World, Faction, Hediff, HediffComp,
Gene, Ideo, JobTracker, HealthTracker, NeedsTracker, MentalStateHandler, RelationsTracker,
PathFollower, Lord, LordManager). También nested types (2.25), genéricos (2.26) y struct
como tipo de campo (2.28).

```csharp
[ReworkField]
public static extern ref int MiCampo(this Verse.Pawn pawn);       // campo en Pawn

[ReworkField]
public static extern ref List<string> Notas(this Verse.MapComponent mc); // campo en cada MapComponent
```

Uso desde un mod:

```csharp
using Rework;
pawn.MiCampo() = 5;               // setter int
int v = pawn.MiCampo();           // getter int
pawn.MiCadena() = "texto";        // setter string
string s = pawn.MiCadena();       // getter string
```

**Tipos de colección (bloque 5, verificado en el juego):** `List<T>`, `Dictionary<K,V>`,
`HashSet<T>` y arrays (`T[]`) con `Initializer` por método — las operaciones de C# funcionan
en runtime (Add/Remove/Sort/reemplazo entero/…).

> ⚠️ **Serialización automática:** `[ReworkField(Serialize = true)]` soporta primitivas,
> `string`, enums, `List` de esos, `Dictionary<K,V>` y `HashSet<T>` (de primitivas/string/
> enums). **NO se serializan: arrays (`T[]`) y colecciones de objetos** (referencias/Deep) —
> el campo se añade y funciona en runtime, pero no viaja en el save (error claro, no rompe).
> Para arrays usa un `List` o Scribe manual.

#### Inicializador (valores por defecto) — `[ReworkField(Default = ...)]`

Sin inicializador, el campo nace en el default de CLR (0 / null). `Default` hace que Rework
inyecte un `this.campo = valor` al **inicio de todos los constructores** del tipo destino:

```csharp
[ReworkField(Default = 1.0f)]
public static extern ref float MiModificador(this Verse.Pawn pawn);

[ReworkField(Default = 42)]
public static extern ref int MiContador(this Verse.Pawn pawn);
```

Soporta primitivas, `string` y enums. Inyección en el **índice 0** del ctor (antes de la
base: IL válido, no corrompe try/catch).

#### Inicializador POR MÉTODO — `[ReworkField(Initializer = "...")]`

Para tipos complejos (`List<>`, estado) el valor inicial no puede ser una constante: se
indica el nombre de una función estática pública **en la misma clase estática que el
accessor**:

```csharp
[ReworkField(Initializer = nameof(CrearInventario))]
public static extern ref List<ThingDef> Inventario(this Verse.Pawn pawn);
public static List<ThingDef> CrearInventario() => new();
```

- 0 parámetros → `campo = Metodo();`; 1 parámetro → recibe `this`.
- Requisitos: método público (acceso entre ensamblados), no void, 0 o 1 parámetro.
- **Tiene prioridad sobre `Default`** si pones ambos.

#### Métodos añadidos — `[ReworkMethod]` (bloque 3.1/3.2)

Añade un método REAL a un tipo del juego: Rework inyecta un **forwarder** que llama a tu
método estático (la lógica vive en tu mod; el miembro queda real, visible por reflexión):

```csharp
[ReworkMethod(Type = "Verse.Pawn", Name = "Rework_Saludar")]
public static void Saludar(this Verse.Pawn pawn) => Log.Message($"Saludo de {pawn.NameShortColored}");

[ReworkMethod(Type = "Verse.Game", Instance = false, Name = "Rework_TotalMods")]
public static int TotalMods() => LoadedModManager.RunningModsListForReading.Count;
```

Reglas: método estático y público; `Name` opcional; si ya existe el miembro se omite.

#### Propiedades añadidas — `[ReworkProperty]` (bloque 3.3)

Añade una propiedad REAL con getter/setter forwarder (emparejados por `Get_`/`Set_` o `Name`):

```csharp
[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static int Get_Rework_SaludoCount(this Verse.Pawn pawn) => pawn.Rework_Logros().Count;

[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static void Set_Rework_SaludoCount(this Verse.Pawn pawn, int value) { /* ... */ }
```

#### Atributos añadidos — `[ReworkAnnotate]` (bloque 4.10/4.11/4.12)

Añade un atributo (metadata) a una clase, método o campo del juego:

```csharp
[ReworkAnnotate(Type = "Verse.Pawn")]                              // clase
[ReworkAnnotate(Type = "Verse.Pawn", Member = "Kill")]             // método
[ReworkAnnotate(Type = "Verse.Pawn", Member = "health")]           // campo
```

Sin `Attribute` se usa el marcador genérico `Rework.Core.ReworkMarkAttribute`. Verificado:
`ReworkMarca` inyectado en `Verse.Pawn` y `Verse.Pawn::Kill`.

#### Límite importante — interfaces y cambios de clase base (bloque 4.1–4.9/4.2–4.3)

⚠️ **No soportado:** añadir una **interfaz de un mod** a una clase del juego
(`[ReworkInterface]`) o **cambiar la clase base**. La interfaz vive en el ensamblado del mod
(recargado, identidad duplicada) y rompe la **vtable** de la clase al arrancar
(`TypeLoadException: VTable setup of type Verse.Pawn failed`). Alternativa segura: **subclase
NUEVA** heredando de la clase del juego (patrón `ReworkWorldCameraDriver`).

#### Orden entre mods para campos (cross-mod ordering)

El primer accessor con un nombre crea el campo (y su inicializador); los siguientes con el
mismo nombre+tipo se **enlazan** (campo compartido); mismo nombre con otro tipo → **conflicto**
(accessor reescrito para lanzar). El primer mod en orden de carga gana.

### Parches libres [ReworkPatch] y transpilers (Fase 3/4)

Dos modos detectados por la firma (conviven, respetan `Priority`):

```csharp
// Directo (Cecil puro): modifica el ModuleDefinition de Assembly-CSharp.
[ReworkPatch]
public static void MiParche(ModuleDefinition module) { /* Cecil puro */ }

// Transpiler amigable: recibe las instrucciones del método destino y devuelve las nuevas.
[ReworkPatch(Type = "RimWorld.SkillRecord", Method = "Learn")]
public static IEnumerable<Instruction> MiTranspiler(IEnumerable<Instruction> instrs, ModuleDefinition module)
{ ... }
```

El transpiler **preserva manejadores de excepción** (reusa el mismo MethodBody); si el
modder borra un límite EH, se descarta ese manejador con aviso (nunca IL corrupto).
Orden de aplicación: Priority desc → orden de carga del mod → nombre.

### Helpers de IL para transpilers — TranspilerHelpers

En Rework no hay Prefix/Postfix (la mecánica "antes/después", **descartada** por inferior).
Los helpers cubren los casos comunes sin pelear con el IL a mano:
`Ldc`/`Ldstr`/`Ldnull`, `Ldarg`/`Starg`, `Call`, `Ldfld`, `Ret`, `ImportMethod`/`ImportType`,
`List`/`Prepend`. (Ver ejemplo en la sección de [ReworkPatch].)

> ⚠️ **Trap documentado (ERRORES.md):** `Starg`/`Ldarg` con `(byte)` numérico lanza
> `ArgumentException: opcode` — usar `Starg(ParameterDefinition)` / `Ldarg` con el
> `ParameterDefinition` real o las formas cortas `Ldarg_0`… `Ldarg_3`.

---

## Serialización automática de campos

`[ReworkField(Serialize = true)]` hace que el campo **viaje en la partida guardada** sin
escribir Scribe a mano (Rework inyecta la llamada en el `ExposeData` del tipo destino):

```csharp
[ReworkField(Default = 1.0f, Serialize = true)]
public static extern ref float Rework_Reputacion(this Pawn p);

[ReworkField(Initializer = nameof(CrearHistorial), Serialize = true)]
public static extern ref List<string> Rework_Historial(this Pawn p);
```

- **Tipos soportados:** primitivas, `string`, enums, `List<primitiva/string/enum>`,
  `Dictionary<K,V>` y `HashSet<T>` (de primitivas/string/enums). **NO:** arrays y
  colecciones de objetos.
- **Mecánica:** primitiva/string/enum → `Scribe_Values.Look`; `List`/`Dictionary`/`HashSet`
  → `Scribe_Collections.Look` (overload correcto por tipo: List 1 genérico/4 params,
  Dictionary 2/4, HashSet 1/3). Etiqueta = nombre del accessor, con `forceSave` y default.
- **Solo el mod que CREA el campo** decide la serialización.
- Si el tipo no es soportado → error claro y no se serializa (no rompe el guardado).
- **6.6 (campos faltantes):** los `Initializer`/`Default` re-crean el valor al cargar si el
  campo no está en un save viejo.

## Logging a archivo y ErrorPrinter

Todo lo que Rework loguea se vuelca además a **`Rework.log`** (LocalLow, junto a
`Player.log`): fuente limpia, con marca de tiempo y cabecera de sesión por arranque, UTF-8
con BOM (cualquier editor lo lee bien). Para volcar excepciones con contexto:

```csharp
Rework.Core.ErrorPrinter.Print("al procesar la partida", e);
var ex = Rework.Core.ErrorPrinter.Try("cargar inventario", () => MiLogica());
```

## Init patches (correr código una vez al arrancar)

```csharp
[ReworkInit]            // prioridad 0
public static void Preparar() { ... }

[ReworkInit(100)]       // prioridad explícita (mayor → primero)
public static void Inicializar() { ... }
```

- **Orden (cross-mod):** Priority desc → orden de carga del mod → nombre.
- **Contención de errores:** si un init lanza, se reporta y se sigue (no revienta el boot).
- Nota: corre en fase de carga de mods; si necesitas Defs cargados, combina con vanilla
  `[StaticConstructorOnStartup]`.

## Ciclo de vida declarativo — [ReworkHook] (bloque 7, completo)

`[ReworkHook(ReworkHookPoint.X)]` engancha tu método estático **al inicio** del método del
juego que representa el evento:

| Hook | Método del juego | Contexto (parámetro opcional) |
|---|---|---|
| `GameStart` | `Verse.Game.LoadGame` | `Verse.Game` |
| `GameInitialized` | `Verse.Game.FinalizeInit` | `Verse.Game` |
| `MapGenerated` | `Verse.Map.FinalizeLoading` | `Verse.Map` |
| `MapInitialized` | `Verse.Map.FinalizeInit` | `Verse.Map` |
| `PawnDied` | `Verse.Pawn.Kill` | `Verse.Pawn` |
| `PawnSpawned` | `Verse.Pawn.SpawnSetup` | `Verse.Pawn` |
| `IncidentFired` | `RimWorld.IncidentWorker.TryExecute` | `RimWorld.IncidentWorker` |
| `MapTick` | `Verse.Map.MapPreTick` | `Verse.Map` |
| `PawnTick` | `Verse.Pawn.Tick` | `Verse.Pawn` |
| `GameComponentTick` | `Verse.GameComponent.GameComponentTick` | `Verse.GameComponent` |
| `MapComponentTick` | `Verse.MapComponent.MapComponentTick` | `Verse.MapComponent` |
| `WorldComponentTick` | `RimWorld.Planet.WorldComponent.WorldComponentTick` | `RimWorld.Planet.WorldComponent` |
| `CurrentMapChanged` | `Verse.Game.set_CurrentMap` | el `Verse.Map` NUEVO (parámetro del setter) |
| `CaravanSpawned` | `RimWorld.Planet.Caravan` (ctor) | `RimWorld.Planet.Caravan` (en construcción) |
| `NewGameStarted` | `Verse.Game.InitNewGame` | `Verse.Game` |
| `PawnCreated` | `Verse.Pawn.PostMake` | `Verse.Pawn` |
| `StorytellerTick` | `RimWorld.Storyteller.StorytellerTick` | `RimWorld.Storyteller` |
| `GameEnded` | `Verse.Game.Dispose` | `Verse.Game` (saliendo) |
| `GameSaving` | `Verse.GameDataSaveLoader.SaveGame` | — (sin contexto) |
| `BabyBorn` | `RimWorld.PregnancyUtility.ApplyBirthOutcome` | la MADRE biológica (el bebé aún no es Pawn) |
| `CaravanEnteredMap` | `RimWorld.Planet.CaravanEnterMapUtility.Enter` | el `Verse.Map` destino |
| `PawnLeftToCaravan` | `RimWorld.Planet.CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan` | el `Verse.Pawn` que sale |

> ⚠️ **Rendimiento:** `MapTick`, `PawnTick`, `GameComponentTick`, `MapComponentTick`,
> `WorldComponentTick` y `StorytellerTick` se disparan 60/s → haz el cuerpo baratísimo o usa
> contador/intervalo.
>
> **Contexto por parámetro:** la mayoría pasan el `this`; casos especiales:
> `CurrentMapChanged` → el mapa nuevo; `BabyBorn` → la madre biológica;
> `CaravanEnteredMap` → el mapa destino; `PawnLeftToCaravan` → el pawn que sale. Rework
> valida que el tipo del parámetro coincida.
>
> **CaravanSpawned:** corre mientras el ctor aún está en progreso; solo registra.
>
> **NewGameStarted ≠ GameStart:** `GameStart` es cualquier partida (nueva o cargada);
> `NewGameStarted` solo partida nueva.
>
> **PawnCreated = pawn creado por código** (`PostMake`), no bebé nacido biológico (eso es
> `BabyBorn`).

Contrato: estático, público, `void`; 0 o 1 parámetro (el contexto). Si no cumple, se
reporta y se omite (no rompe el boot).

## Comprobar la persistencia (save/load) tú mismo

El demo tiene teclas de prueba (enganchadas con `ParcheTeclas` sobre `Verse.Root.Update`):
- **F9** → pone el multiplicador `Rework_XpMultiplier` de todos los colonos vivos a 3.0.
- **F10** → escribe en el `Player.log` el multiplicador actual de cada colono.

Pasos: F9 → guardar → salir del juego → cargar → F10. Si pone `XpMultiplier=3` → la
persistencia funcionó; si `1` → se perdió. Comprobar que NO haya warnings de Scribe al
cargar. (La vía manual fue validada por el usuario: 3 colonos con 3 tras save/load.)

## Chequeo de versión en runtime

`ReworkMod.VerifyPatch` comprueba la versión con `RimWorld.VersionControl.
CurrentVersionString`: loguea `Versión del juego soportada: 1.6.4850` o un error claro si
el juego cambió.

## Integración de mods de terceros (Caso A)

Un mod activo puede subirse al ecosistema Rework referenciando **`0ReworkAPI.dll`** y
declarando sus atributos (`[ReworkField]`/`[ReworkPatch]`/`[ReworkInit]`/`[ReworkHook]`/
`[ReworkMethod]`/`[ReworkProperty]`/`[ReworkAnnotate]`). Requisitos: referenciar la API (y
Mono.Cecil para transpilers) sin empaquetarla (`<Private>false</Private>` /
`ExcludeAssets="runtime"`), About en `About/About.xml`, y que Rework cargue antes.

> **Caso B (duplicados por nombre):** si dos mods traen un ensamblado con el mismo nombre,
> Rework los admite (el primero es el principal para el resolver) y los recarga por su propia
> ruta, sin descartar ninguno (robustez; no "sirve limpio" duplicados patológicos de vanilla).

## Trabajos Declarativos sin XML ([ReworkJob])

Rework Reforjed permite declarar nuevos trabajos (`JobDef`) en C# sin escribir una sola línea de XML ni lidiar con `Defs/JobDefs`:

```csharp
[ReworkJob("MiMod_RepararArmadura", ReportString = "Reparando armadura.")]
public class JobDriver_RepararArmadura : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    protected override IEnumerable<Toil> MakeNewToils()
    {
        yield return Toils_Goto.Goto(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(120);
        yield return Toils_General.Do(() => { /* lógica de reparación */ });
    }
}
```

- **Registro automático:** `ReworkJobRegistry` escanea los mods activos al arrancar el juego (`[StaticConstructorOnStartup]`) y registra automáticamente el `JobDef` en `DefDatabase<JobDef>`.
- **Propiedades configurables:** `ReportString`, `PlayerInterruptible`, `CasualInterruptible`, `Suspendable`.
- **Consulta:** `ReworkJobRegistry.Get("MiMod_RepararArmadura")` devuelve el `JobDef` listo para asignarse con `pawn.jobs.TryTakeOrderedJob(...)`.
- **Clase base opcional:** `ReworkJobDriver` provee helpers como `ToilGoto`, `ToilWait` y `ToilDo` para simplificar la creación de Toils.

## Incidentes Declarativos sin XML ([ReworkIncident])

Rework Reforjed permite declarar nuevos incidentes y eventos (`IncidentDef`) en C# sin escribir XMLs en `Defs/IncidentDefs`:

```csharp
[ReworkIncident("MiMod_OlaDeCalorEspiritual", Category = "Misc", BaseChance = 1.2f, 
                 LetterLabel = "Calor espiritual", LetterText = "El aire vibra con energía psíquica.")]
public class IncidentWorker_CalorEspiritual : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms) => true;

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        // Lógica del evento
        return true;
    }
}
```

- **Registro automático:** `ReworkIncidentRegistry` escanea los ensamblados en el arranque (`[StaticConstructorOnStartup]`) e inyecta el `IncidentDef` en `DefDatabase<IncidentDef>`.
- **Propiedades configurables:** `Category` ("Misc", "ThreatSmall", "ThreatBig", etc.), `BaseChance`, `TargetTag` ("Map_PlayerHome", "World", etc.), `LetterLabel`, `LetterText`.
- **Consulta:** `ReworkIncidentRegistry.Get("MiMod_OlaDeCalorEspiritual")` devuelve el `IncidentDef` listo para ejecutarse vía `Storyteller` o manualmente.
- **Clase base opcional:** `ReworkIncidentWorker` provee el helper `SendStandardLetter` para enviar cartas informativas estándar fácilmente.

## Asignadores de Trabajo Declarativos sin XML ([ReworkWorkGiver])

Rework Reforjed permite conectar trabajos a la IA de asignación automática de la colonia (`WorkGiverDef`) en C# sin escribir XMLs en `Defs/WorkGiverDefs`:

```csharp
[ReworkWorkGiver("MiMod_ReparadorArmaduraGiver", WorkType = "Crafting", PriorityInType = 70, Verb = "reparar", Gerund = "reparando")]
public class WorkGiver_RepararArmadura : WorkGiver_Scanner
{
    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Apparel);

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        return t is Apparel a && a.HitPoints < a.MaxHitPoints;
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        var jobDef = ReworkJobRegistry.Get("MiMod_RepararArmadura");
        return jobDef != null ? JobMaker.MakeJob(jobDef, t) : null;
    }
}
```

- **Registro y enlace automático:** `ReworkWorkGiverRegistry` escanea clases en el arranque (`[StaticConstructorOnStartup]`), registra el `WorkGiverDef` en `DefDatabase<WorkGiverDef>` y lo inserta ordenado por `PriorityInType` dentro de `workGiversByPriority` del `WorkTypeDef` correspondiente.
- **Propiedades configurables:** `WorkType` ("Hauling", "Cleaning", "Crafting", "Doctor", etc.), `PriorityInType`, `Verb`, `Gerund`, `DirectOrderable`, `ScanThings`, `ScanCells`, `Emergency`.
- **WorkTypeDef propio, también sin XML:** si el `WorkType` indicado **no existe**, Rework lo **crea** con los metadatos del atributo y lo añade a `DefDatabase`. (Antes caía en silencio a `Hauling`, escondiendo el trabajo dentro de "acarreo".)

```csharp
[ReworkWorkGiver(
    "MiMod_MatarGiver",
    WorkType = "MiMod_MatanzaType",    // se crea si no existe
    WorkTypeLabel = "matanza",         // etiqueta visible
    WorkTypeLabelShort = "matanza",
    WorkTypePawnLabel = "matar",
    WorkTypeGerund = "matando",
    WorkTypeVerb = "matar",
    WorkTypeTags = "Violent",          // WorkTags (separa con comas para varios)
    WorkTypeNaturalPriority = 6,       // posición en la pestaña de Trabajo
    WorkTypeVisible = true,            // aparece como columna en Trabajo
    WorkTypeAlwaysStartActive = false, // false → prioridad 3 (la más baja)
    Verb = "matar", Gerund = "matando", PriorityInType = 60)]
public class WorkGiver_Matar : WorkGiver_Scanner
{
    public override PathEndMode PathEndMode => PathEndMode.Touch;

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        foreach (var p in pawn.MapHeld.mapPawns.AllPawnsSpawned)
            if (p != pawn && !p.Dead && p.RaceProps.Humanlike) yield return p;
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        => t is Pawn v && v != pawn && !v.Dead && v.Spawned && v.RaceProps.Humanlike;

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamedSilentFail("MiMod_Matar"), t);
}
```

- **Campos del WorkTypeDef creado:** `WorkTypeLabel` / `LabelShort` / `PawnLabel` / `Gerund` / `Verb` (si se omiten se usa el `defName`), `WorkTypeTags`, `WorkTypeNaturalPriority`, `WorkTypeVisible`, `WorkTypeAlwaysStartActive`. Con `CreateWorkTypeIfMissing = false` se recupera el comportamiento antiguo (caer a `Hauling`).
- **Ojo con `visible`:** por defecto el WorkTypeDef creado es visible (`WorkTypeVisible = true`); si se pusiera a `false`, el tipo de trabajo no aparecería en la pestaña de Trabajo y el jugador no podría regularlo.
- **Casilla en la pestaña de Trabajo (automática):** al crear el `WorkTypeDef`, Rework genera también su `PawnColumnDef` (`"WorkPriority_<defName>"`, worker `PawnColumnWorker_WorkPriority`) y la inserta en `PawnTableDefOf.Work.columns` antes de la columna de copiar/pegar prioridades. Sin esto el tipo de trabajo existiría y los colonos lo harían, pero **no tendría casilla donde ponerle prioridad**: vanilla genera esas columnas como *defs implícitos* al cargar los Defs, antes de que Rework registre el tipo.
- **Consulta:** `ReworkWorkGiverRegistry.Get("MiMod_ReparadorArmaduraGiver")` devuelve la instancia de `WorkGiverDef`.

## Recetas, Salud y Rasgos Declarativos sin XML

Rework Reforjed extiende el desarrollo de contenido permitiendo crear recetas, condiciones de salud y rasgos directamente en C#:

### Recetas de Fabricación y Cirugía ([ReworkRecipe])
```csharp
[ReworkRecipe("MiMod_FabricarVenda", Label = "fabricar venda", WorkAmount = 300f, RecipeUsers = "CraftingSpot")]
public class Recipe_FabricarVenda : RecipeWorker
{
    public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
    {
        // Lógica de aplicación
    }
}
```
- Registra `RecipeDef` en `DefDatabase` y lo enlaza automáticamente a las mesas indicadas en `RecipeUsers`.

### Condiciones Médicas y Buffs ([ReworkHediff])
```csharp
[ReworkHediff("MiMod_BendicionSolar", Label = "bendición solar", InitialSeverity = 1.0f, IsBad = false)]
public class Hediff_BendicionSolar : HediffWithComps
{
}
```
- Registra `HediffDef` en `DefDatabase` con severidad inicial y clasificación médica.

### Rasgos de Personalidad ([ReworkTrait])
```csharp
[ReworkTrait("MiMod_AlmaReforjada", Label = "alma reforjada", Degree = 0, Commonality = 1.0f)]
public class Trait_AlmaReforjada
{
}
```
- Registra `TraitDef` en `DefDatabase` estructurando sus `degreeDatas` automáticamente.

### Necesidades de Colonos ([ReworkNeed])
```csharp
[ReworkNeed("MiMod_Entusiasmo", Label = "entusiasmo", BaseLevel = 0.8f, FallPerDay = 0.3f, ColonistsOnly = true)]
public class Need_Entusiasmo : Need
{
    public Need_Entusiasmo(Pawn pawn) : base(pawn) { }
    public override void NeedInterval() => CurLevel -= 0.001f;
}
```
- Registra `NeedDef` en `DefDatabase` con tasa de caída y restricciones de colonos.

### Designadores de Órdenes en la Barra Inferior ([ReworkDesignator])
```csharp
[ReworkDesignator("Orders")]
public class Designator_Inspeccionar : Designator
{
    public Designator_Inspeccionar() { defaultLabel = "Inspeccionar"; }
    public override AcceptanceReport CanDesignateCell(IntVec3 loc) => true;
    public override void DesignateSingleCell(IntVec3 c) { /* acción */ }
}
```
- Enlaza el `Designator` directamente en `specialDesignatorClasses` de la categoría indicada ("Orders", "Zone", "Production", etc.).

### Componentes de Juego y Mapa (GameComponent / MapComponent)
RimWorld descubre e instancia automáticamente cualquier subclase no abstracta de `GameComponent` y `MapComponent` mediante `GenTypes.AllSubclassesNonAbstract`, por lo que basta con heredar de la clase correspondiente para recibir ciclos de vida (`Tick`, `FinalizeInit`) y serialización persistente (`ExposeData`).

### §48 — Condiciones reales de la API declarativa (leído tras la gran auditoría)
"Registrado" no es "aplicado" ni "obtenible". Esto es lo que el framework hace
por ti y lo que queda a tu cargo:

- **`[ReworkHediff]`** — el HediffDef se registra bien, pero **nada lo aplica
  solo**: necesita una receta con `addsHediff`, código tuyo
  (`pawn.health.AddHediff`) o un `[ReworkStatusEffect]` con el MISMO defName
  (esa vía sí lo aplica y expira el framework).
- **`[ReworkThought]`** — igual: nada concede un ThoughtDef arbitrario
  (usa un `ThoughtWorker`/situación o `TryGainMemory`). Y ojo: la clase decorada
  **no** se conecta al Def — el pensamiento usa `ThoughtClass`
  (default `Thought_Memory`); pasa `ThoughtClass = typeof(TuClase)` si quieres
  tu lógica.
- **`[ReworkApparel]`** — el ThingDef se registra equipable, pero sin
  `ThingCategoryDef`, receta, `costList` o stock de trader **no aparece en el
  juego normal** (solo dev-mode/código). Se crea sin `statBases` y `TexPath`
  exige textura real.
- **`[ReworkRecipe]`** — la receta se enlaza a las mesas (`recipeUsers`), pero se
  crea **sin `products`**: es una receta de "worker total" (el patrón de
  cirugía); añade `products` con `[ReworkMutate]` o código si quieres fabricar.
- **`[ReworkJob]`** — el JobDef con su driverClass está listo; el trabajo solo
  existe cuando algo lo EMITE (`[ReworkWorkGiver]`, thinktree o
  `pawn.jobs.TryTakeOrderedJob`).
- **`[ReworkAIModifier]`** — tu método recibe `(Pawn real, prioridadActual)` y
  actúa como **multiplicador global** de prioridades de ThinkNodes. El parámetro
  `TargetJobDef` **NO está soportado** (el hook, `ThinkNode.GetPriority`, no
  conoce el trabajo): si lo pones, el escáner lo ignora con un error claro y
  registra el modificador como global.
- **`[ReworkSchedule]`** — el método debe ser `static void` **sin parámetros** y
  con intervalo (`EveryTicks`/`EveryDay`/`EverySeason`); si no, el escáner lo
  rechaza con error (antes fallaba en silencio).
- **`[ReworkWatch]`** — la auto-detección es **polling por tick** (no
  interceptación de setters) y solo muestrea instancias de `Pawn` y `Map`; para
  otros tipos notifica a mano con `ReworkWatch.NotifyChanged`.
- **`ReworkApi.IsPresent`** — sin el framework instalado, el acceso al tipo
  lanza antes de que `IsPresent` pueda devolver `false`: para degradación real,
  envuelve el acceso en `try/catch`.
- **Persistencia de servicio** — los almacenes (`ReworkColonySkill`,
  `ReworkPawnTimeline`, `ReworkWorldStore`, `ReworkStateSnapshot`, `ReworkRelation`)
  se guardan en `<save>.rwdat` y se restauran automáticamente al CARGAR la
  partida (desde la §48); `ReworkWorldStore` preserva el tipo de los valores
  (formato v2).

### Daño declarativo: absorción y daño periódico ([ReworkDamageSoak], [ReworkDamageOverTime])

Dos atributos sobre clases `: Hediff` que dan comportamiento de combate sin XML
ni `HediffComp` propio (equivalen a `HediffComp_DamageSoak` y
`HediffCompDamageOverTime` de JecsTools, reimplementados sobre el pipeline de
Rework):

```csharp
// Vampiro: ignora 5 puntos de cada golpe, salvo quemaduras.
[ReworkDamageSoak("MiMod_PielVampirica", DamageToSoak = 5f,
    DamageTypesToExclude = "Burn,Flame", Label = "piel vampírica")]
public class Hediff_PielVampirica : HediffWithComps
{
}

// Veneno: 2 de daño tóxico cada 2500 ticks (~1 hora de juego).
[ReworkDamageOverTime("MiMod_Envenenado", CycleDamage = "Toxic",
    CycleDamageAmt = 2f, CycleInTicks = 2500, Label = "envenenado")]
public class Hediff_Envenenado : HediffWithComps
{
}
```

- **`[ReworkDamageSoak]`** — `DamageToSoak` (plano por golpe), `DamageType`
  (defName o vacío = todos), `DamageTypesToExclude` (CSV), `ShowAbsorbedMote`.
  El framework resta la absorción en `Pawn.PreApplyDamage`, antes de culpas,
  pensamientos y escudos. Si el daño llega a 0, el golpe se absorbe del todo
  (vanilla no procesa nada más). Varios soaks en el mismo pawn se acumulan.
- **`[ReworkDamageOverTime]`** — `CycleDamage` (defName, default "Bite"),
  `CycleDamageAmt`, `CycleInTicks` (default 30000 = medio día),
  `ArmorPenetration`. Cada ciclo usa el camino vanilla completo
  (`Thing.TakeDamage`: armadura, absorción, notificaciones). La cuenta es por
  instancia de hediff: dos venenos distintos ciclan por separado.
- Ambos crean su `HediffDef` si no existe — pero **nada lo aplica solo**
  (misma regla que `[ReworkHediff]`): aplícalo con `pawn.health.AddHediff`,
  una receta con `addsHediff` o un `[ReworkStatusEffect]` con el mismo defName.
- Los defNames de DamageDef se validan al registrar: un tipo inexistente da
  error claro en el log (el soak/DoT queda registrado pero inerte hasta
  corregirlo), nunca un NRE en el golpe.

### Empuje de knockback ([ReworkKnockback])

```csharp
// Ogro: 1 de cada 3 golpes lanza a la víctima 3-5 celdas, con stun y daño al chocar.
[ReworkKnockback("MiMod_FuerzaOgroides", KnockbackChance = 0.33f,
    KnockDistanceMin = 3f, KnockDistanceMax = 5f,
    StunChance = 0.25f, StunTicks = 60, Label = "fuerza ogroide")]
public class Hediff_FuerzaOgroide : HediffWithComps
{
}
```

- Solo dispara en **melee** (el arma del golpe no es a distancia y el atacante
  es un pawn). Si vanilla absorbió el golpe entero, no hay empuje.
- Física: distancia = azar(min,max) × curva de daño absorbido × curva de masa
  (un warg sale volando, un thrumbo apenas se mueve). El destino se recorta
  por línea de visión (muros y puertas cerradas paran); al chocar, daño de
  impacto proporcional a lo recorrido. La víctima vuela de verdad (objeto
  volador propio) y aterriza con el daño.
- `KnockbackSpeed`, `KnockbackSound`, `KnockbackThought` (a humanlikes al
  chocar), `KnockImpactDamageMin/Max` + `KnockImpactDamageType` (default
  Blunt), `StunChance`/`StunTicks`, `ExplosiveRadius` + `ExplosiveDamageType`
  (default Flame, detona aunque no vuele).
- Anti-doble-empuje: los daños extra de un mismo golpe generan varios
  impactos; solo el primero del tick por pareja atacante-víctima empuja.

### Daño con salpicado ([ReworkCleave])

```csharp
// Espada sierra: cada golpe salpica a 2 enemigos adyacentes con el 70% del daño.
[ReworkCleave("MiMod_DanoSierra", CleaveTargets = 2, CleaveFactor = 0.7f, Label = "sierra")]
public static class DanoSierra { }
```

- Crea un `DamageDef` con worker propio: el golpe principal es un golpe
  normal (`DamageWorker_AddInjury`) y ANTES salpica a enemigos en las 8
  celdas alrededor de la víctima (a menos de 2 celdas del atacante).
- `CleaveTargets = 0` (default) = automático: masa del arma redondeada, o
  BodySize si va desarmado. `CleaveDamage` (vacío = el mismo daño),
  `ArmorPenetration`, `CleaveFactor` (0..1).
- Solo pawns de otra facción (aliados a salvo, el atacante nunca se golpea);
  el salpicado no vuelve a salpicar. Para usarlo, el arma o verbo debe infligir
  este DamageDef (vía `Tool` del arma o código).

### Daños extra de melee por hediff ([ReworkExtraMelee])

```csharp
// Colmillos vampíricos: cada mordisco suma 3 de toxina + 2 de corte al 50%.
[ReworkExtraMelee("MiMod_Colmillos", ExtraDamages = "Toxic:3,Bite:2:0:0.5", Label = "colmillos")]
public class Hediff_Colmillos : HediffWithComps
{
}
```

- Formato por daño: `Def:Cantidad[:Penetración][:Chance]`
  (`"Toxic:3"`, `"Burn:2:0.5"`, `"Cut:4:0:0.5"`). Entradas mal formadas o con
  DamageDef inexistente = error claro al registrar, nunca NRE en el golpe.
- Los daños extra pasan por `TakeDamage` igual que los del arma (armadura,
  absorción, notificaciones). Sin registros, el golpe itera lo mismo que
  vanilla: coste cero.

### Desvío de proyectiles ([ReworkDeflector])

```csharp
// Sable láser: 30% base + 1.5% por nivel de Melee, devuelve disparos.
[ReworkDeflector("MiMod_SableLaser", BaseDeflectChance = 0.3f,
    DeflectSkill = "Melee", DeflectRatePerSkillPoint = 0.015f,
    CanReflect = true, ReflectSkill = "Melee", Label = "sable láser")]
public static class SableLaser { }
```

- Se declara **sobre el arma** (defName existente, vanilla o de otro mod):
  mientras el pawn la empuñe, los proyectiles que le alcancen se desvían.
  Bombas, llamas y explosivos NO se desvían; el melee enemigo tampoco.
- `DeflectSkill` + tasa (chance = base + nivel×tasa, ×manipulación si se
  pide), XP por desvío, sonido al desviar.
- `CanReflect`: el disparo vuelve al tirador (tirada d100 + nivel×tasa vs 80;
  ≥90 crítico). El reflect es directo (el tirador recibe su daño): sin job de
  casteo — misma pegada, sin la parte frágil.

### Escudos físicos ([ReworkShield])

```csharp
// Antidisturbios: bloquea 60% melee y 40% ranged, se rompe y fatiga.
[ReworkShield("MiMod_EscudoAntidisturbios", BaseMeleeBlockChance = 0.6f,
    BaseRangedBlockChance = 0.4f, UseFatigue = true)]
public static class EscudoAntidisturbios { }
```

- Se declara **sobre el escudo** (defName existente, equipable): mientras el
  pawn lo lleve en el equipo, los golpes se bloquean. Ranged = instigador no
  adyacente o daño explosivo (misma regla que JecsTools).
- `BlockSkill` + tasa, `ShieldTakeDamage` + `ShieldDamageAbsorbed` (el escudo
  sufre con sus propios HP), `CanBeAutoDiscarded` +
  `HealthAutoDiscardThreshold` (se suelta solo al romperse), `UseFatigue` +
  `DamageToFatigueFactor` (Hediff `Rework_ShieldFatigue` de 4 stages, creado
  por el registrador; al máximo también se suelta).
- Al bloquear: chispas + sonido. Orden del golpe: desviar → bloquear →
  absorber → vanilla → knockback.

### Spawns con retardo ([ReworkDelayedSpawner])

```csharp
// El Vampiro Estelar cae a los 30s, enfurecido y con hambre.
[ReworkDelayedSpawner("MiMod_StarVampire", PawnKind = "MiMod_StarVampire",
    Faction = "Mechanoid", TicksUntilSpawn = 1800,
    MentalState = "Berserk", Hediffs = "MiMod_HambreVoraz")]
public static class SpawnerVampiro { }

// Colmena: 1-3 crías cada día, para siempre.
[ReworkDelayedSpawner("MiMod_Colmena", PawnKind = "MiMod_Cria",
    TicksUntilSpawn = 60000, Repeat = true, RepeatEveryTicks = 60000)]
public static class SpawnerColmena { }
```

- `PawnKind` + `Faction` + `PawnCount` (o `Thing` + `ThingCount`),
  `MentalState`, `Hediffs` (CSV), `TicksUntilSpawn`, `Repeat` +
  `RepeatEveryTicks` + `MaxRepeats` (-1 = infinito).
- Los declarativos spawnean en el mapa actual (celda transitable cerca del
  centro). Para posición exacta, desde código:
  `ReworkSpawnerRuntime.Queue(map, cell, spec)` (servicio en `Rework.Core`).
- Todo se valida al registrar (def inexistente = error claro, el spawner no
  se programa); el runtime re-valida barato antes de generar.

### Variantes intercambiables ([ReworkToggleDef])

```csharp
// Anillo que cambia de dedo a gusto del jugador.
[ReworkToggleDef("MiMod_Anillo", Variants = "MiMod_AnilloMenique,MiMod_AnilloAnular,MiMod_AnilloIndice")]
public static class GrupoAnillo { }
```

- Lista explícita de 2+ ThingDefs (no nomenclatura mágica: renombrar sin
  actualizar da error claro al registrar, no un grupo roto en silencio).
- El gizmo "Cambiar variante" aparece solo en variantes (o colonos con arma
  variante) y abre el menú. El cambio conserva HP proporcional, verbos
  re-inicializados y equipo/portador; si el respawn falla, revierte al def
  original antes de rendirse.

### Piezas instalables ([ReworkInstalledPart])

```csharp
// Cañón que se monta en mechas (y se desmonta de vuelta al suelo).
[ReworkInstalledPart("MiMod_CanonMecha", AllowedOn = "MiMod_Mecha",
    WorkToInstall = 500, WorkToUninstall = 500)]
public static class PiezaCanon { }
```

- El gizmo "Instalar pieza" sale en piezas sueltas y en objetivos: ordena al
  colono llevarla y atornillarla (Construction, con su velocidad y XP).
  `AllowedOn` vacío = cualquier objetivo.
- Al instalar: prenda → vestida, arma → equipada, resto → contenedor del
  objetivo (o al suelo al lado). Al desinstalar: la pieza cae al suelo.
  Misma facción exigida, como JecsTools.

### Slots en armas ([ReworkSlotLoadable])

```csharp
// Fusil con slot de munición: perforante suma daño, incendiaria quema.
[ReworkSlotLoadable("MiMod_FusilPeso",
    Slots = "Munición:BulletLight,BulletAP,BulletHE:color:stats",
    Bonuses = "BulletAP:Cut:2:0.3;BulletHE:Burn:4:0",
    Stats = "BulletAP:AccuracyTouch:0.1")]
public static class FusilPeso { }

// Sable con cristal kyber rojo vampírico.
[ReworkSlotLoadable("MiMod_Sable",
    Slots = "Cristal:KyberRojo,KyberAzul:color:stats",
    Heal = "KyberRojo:vampirica:0.3:2;KyberAzul:defensiva:0.2:1")]
public static class SableKyber { }
```

- `Slots`: `"Etiqueta:Def1,Def2[:color][:stats]"` por slot (`;`).
  `color` = el item tiñe el arma; `stats` no filtra (los bonus aplican igual).
- `Bonuses`: `"Item:Damage:Cant[:Pen]"` — daño extra al golpear con esa
  munición (vía el paso 18c, sin transpiler). `Stats`: `"Item:Stat:offset"`
  al portador (vía hediff oculto por stat, generado y cacheado).
  `Heal`: `"Item:defensiva|vampirica:chance:cantidad"` al golpear.
- El estado es un campo real serializado en cada arma (qué hay cargado); los
  items se consumen al cargar y se devuelven al descargar. Gizmos por slot +
  job de recogida (`Rework_GatherSlotItem`).

### Habilidades ([ReworkAbility] + [ReworkPassive])

```csharp
// Bola de fuego: 12 de quemadura a 24 celdas, 60s de recarga, a colonos.
[ReworkAbility("MiMod_BolaFuego", Label = "bola de fuego",
    Range = 24f, CooldownTime = 60f,
    Effects = "damage:Burn:12", GrantToColonists = true)]
public static class BolaFuego { }

// Piel de piedra pasiva: +0.2 armadura mientras tenga el hediff.
[ReworkPassive("MiMod_PielPiedra", RequiresHediff = "MiMod_PielPiedra",
    Stats = "ArmorRating_Blunt:0.2,ArmorRating_Sharp:0.2")]
public static class PielPiedra { }
```

- Puente sobre Abilities vanilla (no magia clonada): crea el `AbilityDef`
  con verbo + efectos simples (`hediff:Def:sev`, `damage:Def:cant`,
  `stun:ticks`, `spawn:PawnKind[:n]`, separados por `;`) + `EffectClasses`
  vanilla si quieres. Concesión a colonos, por PawnKinds o manual
  (`ReworkAbilityRuntime.Grant(pawn, def)`).
- `[ReworkPassive]`: statOffsets mientras tenga el hediff (o siempre).
- IA enemiga: `ReworkJobGiver_AICast` (cuélgalo de un ThinkTree: cura a
  aliados heridos, ataca enemigos a rango con LoS, con el job vanilla).

## Invenciones Únicas de Rework Reforjed

### 1. Bus de Eventos Reactivo (`ReworkBus` & `[ReworkOn]`)
Permite suscribir métodos estáticos o de instancia a eventos del juego sin hacer polling en `Tick` ni parchar manualmente:
```csharp
public static class MiSistema
{
    [ReworkOn]
    public static void AlOcurrir(ReworkLifecycleEvent e)
    {
        Log.Message($"Evento {e.Name} detectado reactivamente.");
    }
}

// Registro y publicación:
ReworkBus.Register(typeof(MiSistema));
ReworkBus.Publish(new ReworkLifecycleEvent("PartidaIniciada"));
```

### 2. Generación Procedimental de Mapas sin XML (`[ReworkGenStep]`)
Inyecta pasos de generación procedimental directo en la generación de mapas:
```csharp
[ReworkGenStep("MiMod_CovilDragon", Order = 650f)]
public class GenStep_CovilDragon : GenStep
{
    public override int SeedPart => 987654;
    public override void Generate(Map map, GenStepParams parms)
    {
        // Generación de estructuras o recursos en el mapa recién creado
    }
}
```

### 3. Mutaciones Declarativas de Defs Vanilla (`[ReworkMutate]`)
Modifica valores de Defs vanilla en tiempo de ejecución de manera registrada, reversible y visible en el diagnóstico:
```csharp
[ReworkMutate("CraftingSpot", "useHitPoints", false)]
[ReworkMutate("Human", "baseHealthScale", 1.25f, DefType = typeof(ThingDef))]
public static class MisBalanceos { }
```

### 4. Hot-Reload Seguro en Caliente (`ReworkLive`)
Permite actualizar ensamblados `.dll` de mods en tiempo de ejecución sin reiniciar el juego y sin corromper el estado en memoria:
- **Protocolo de limpieza estricto (`IReworkLiveReloadable`):**
  - `OnBeforeLiveReload()`: Cancela corrutinas, limpia callbacks y libera referencias viejas.
  - Limpieza automática de `GenTypes.ClearCache()` y `ReworkBus.Clear()`.
  - Recarga de bytes sin bloquear el archivo en disco.
  - `OnAfterLiveReload()`: Re-inicializa el estado fresco y reconecta suscripciones.
- **Uso:** Implementar `IReworkLiveReloadable` o suscribirse a los eventos `ReworkLive.BeforeReload` y `ReworkLive.AfterReload`.

### 5. Guardado Binario de Alta Velocidad (`ReworkBinaryScribe` & `.rwbin`)
Sustituye la lentitud y el tamaño masivo de los saves XML de RimWorld por un formato binario comprimido con protección total contra fallos:
- **Doble escritura y respaldo:** Mantiene intacto el archivo `.rws` clásico de RimWorld como respaldo seguro para que el jugador nunca dependa exclusivamente del mod.
- **Centinela atómico final (`#REFORJED`):**
  - Al guardar: Se comprime el payload y, como última instrucción física antes de cerrar el archivo, se escriben los bytes `#REFORJED`.
  - Al cargar: Comprueba el final del archivo. Si `#REFORJED` está presente, carga en ~1 segundo. Si hubo un corte de energía o crash a mitad del guardado, descarta el binario roto y **carga automáticamente el XML de respaldo**.
- **Ciclo completo de carga y guardado en memoria:**
  - **Guardado:** Enganchado en `Verse.SafeSaver.Save`, empaqueta el binario `.rwbin` atómicamente con `#REFORJED`.
  - **Carga:** Enganchado en `Verse.ScribeLoader.InitLoading`. Si el archivo `.rwbin` existe y su centinela `#REFORJED` es 100% íntegro, descomprime directamente el documento en memoria asignando `curXmlParent` y `Scribe.mode = LoadingVars`, omitiendo el costoso parseo de texto XML y acelerando la carga exponencialmente. Si el binario falta o está corrupto, continúa por el camino vanilla del `.rws`.
- **Único XML de Respaldo de Tiempo (`GameComponent_ReworkXmlBackup`):**
  - Mantiene exactamente **un archivo binario (`.rwbin`)** para todos los guardados comunes (instantáneo y sin lag) y **un único XML (`.rws`)** como respaldo temporal.
  - El XML no se reescribe en cada guardado rápido: se sincroniza silenciosamente cada X minutos (configurable de 5 a 120 min, 15m por defecto).
  - Cero duplicación de archivos en la carpeta de saves: solo existen `[Colonia].rwbin` y `[Colonia].rws`.

### 6. Campos Reactivos (`[ReworkWatch]`)
En lugar de hacer polling en cada tick, los callbacks marcados con `[ReworkWatch]` se invocan automáticamente cuando un campo inyectado ([ReworkField]) cambia:
- **Atributo:** `[ReworkWatch("NombreDelCampo")]` sobre un método estático. Parámetros opcionales: `(Pawn)` o `(Pawn, oldValue, newValue)`.
- **Fachada pública:** `ReworkWatch.NotifyChanged(target, fieldName, old, new)` + `Notifier` delegable, accesibles desde cualquier mod externo sin referenciar `Rework.dll`.
- **Registro:** `ReworkWatchRegistry` escanea los ensamblados activos en boot y conecta el notificador. Incluye `PipelineSelfTest()` de auto-verificación.
- **Ejemplo:**
  ```csharp
  [ReworkWatch("Rework_XpMultiplier")]
  public static void OnXpMultiplierChanged(Pawn pawn, object oldV, object newV) { /* lógica reactiva */ }
  ```
- **Verificado en el juego:** `[ReworkWatch] Auto-verificación del pipeline: selfTestFired=True — [ReworkWatch] verificado ✓`.

### 7. UI Inyectada (`[ReworkUIPanel]`)
Cecil inyecta un bloque de código dentro de `DoWindowContents(Rect)` de cualquier ventana del juego (Assembly-CSharp). El modder solo declara en qué ventana quiere su panel y qué dibuja (GUI estándar de Verse: `Widgets`/`Text`). Sin XML, sin Window propia, sin ThingComp y sin escribir un `[ReworkPatch]` manual:
- **Atributo:** `[ReworkUIPanel("FullNameDeLaVentana", AtStart = false, Priority = 100)]` sobre un método estático.
- **Firmas soportadas:** `static void MiPanel(Window window, Rect inRect)` (2 params) o `static void MiPanel(Rect inRect)` (1 param).
- **Posición:** `AtStart = true` → se inserta al inicio del método (el panel queda DEBAJO del contenido de la ventana); `AtStart = false` (default) → se inserta justo antes del último ret seguro (el panel queda ENCIMA, visible sobre la ventana). Si todos los ret están dentro de try/catch, se degrada a inicio con aviso.
- **Robustez (lección empírica):** si el método destino tiene ramas condicionales que saltan directamente al punto de inserción (p.ej. `brfalse → ret` de botones opcionales), el framework **re-apunta todas esas ramas al inicio del bloque inyectado** para que ningún camino omita el panel. Se loguea "N rama(s) re-apuntada(s)".
- **Ejemplo real (demo):**
  ```csharp
  [ReworkUIPanel("Verse.Dialog_MessageBox", AtStart = false, Priority = 100)]
  public static void PanelParaDialogo(Rect inRect)
  {
      Text.Font = GameFont.Small;
      Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "Mi panel en el diálogo");
  }
  ```
- **Verificado en el juego:** 3 paneles inyectados (`Dialog_MessageBox`, `MainTabWindow_Work`, `MainTabWindow_Research`), 3 ramas re-apuntadas, panel pintado 482x en la auto-verificación, boot 0 excepciones.

## Cirugía IL declarativa — Bloque 24

Cinco atributos que modifican el IL del juego **antes** de que arranque, algo que un
parche post-carga no puede hacer. Se declaran en tu mod (ensamblado que referencia
`0ReworkAPI`), validan su objetivo en la pasada 1 y loguean lo que hacen; todos
respetan `ReworkDevMode` (OnlyPatch/OnlyMod). El framework los consume en
`ReworkILSelfDemo` (auto-demo con impacto cero) y los verifica en el arranque
(`[ReworkIL] verificación: ... VERIFICADA ✓` en `Rework.log`); la telemetría
completa queda en el Inspector → pestaña de parches (`DataStore.AppliedPatches`).

> Regla común: los ensamblados que se reescriben son Assembly-CSharp y los mods
> activos. Los del motor (UnityEngine, mscorlib…) nunca se tocan: sus call-sites
> quedan como estaban.

### 1. `[ReworkRedirect]` — reescritura global de call-sites
Sustituye TODOS los puntos de llamada a un método del juego por tu método, en
todos los ensamblados reescribibles. El IL final llama directo: sin detours ni
trampolines, coste cero.
```csharp
[ReworkRedirect(Type = "Verse.Log", Method = "Message")]
public static void InterceptarMensaje(string text)
{
    // tu lógica (contador, filtro, enrutado...)
    Verse.Log.Message(text); // ← llamada al ORIGINAL: Rework NO la reescribe
                             //   (regla anti-recursión dentro del redirector)
}
```
- La firma debe coincidir EXACTAMENTE con la del destino (mismos parámetros por
  nombre completo y mismo retorno). Destino de instancia → tu primer parámetro
  recibe el `this` (el tipo declarante o una de sus bases).
- `Assembly = "Nombre"` para apuntar a otro ensamblado cargado (p. ej. otro mod).
- Cada overload se distingue por la firma: `Log.Message(string)` y
  `Log.Message(object)` son objetivos distintos.
- Aviso: redirigir un **virtual de instancia** reescribe también los `base.X()`
  inyectados por `[ReworkOverride]` — para colgarte del despacho virtual usa
  `[ReworkOverride]`.

### 2. `[ReworkOverride]` — overrides virtuales reales
Inyecta en una clase del juego un método que SOBRESCRIBE de verdad un virtual de
su base (ocupa el slot de la vtable antes de que el tipo cargue: despacho normal,
coste cero, como si lo hubiera escrito Ludeon).
```csharp
[ReworkOverride(Type = "Verse.Building", CallBase = true)]
public static string Rework_GetInspectString(Building b, string baseResult)
{
    return baseResult; // o baseResult + "\nMi línea extra"
}
```
- `CallBase = true` (default): el override llama primero al base y pasa su
  resultado como ÚLTIMO parámetro. `false`: tu método sustituye al base.
- Requisitos verificados en reescritura: el virtual existe en la cadena de bases,
  la clase no lo redeclara (para eso está `[ReworkPatch]`), ni genéricos ni
  `ref/out`. Si la clase está `sealed`, desellámala antes con `[ReworkUnlock]`.
- El nombre del método: `Name = "..."` o el del mod sin prefijo `Rework_`.

### 3. `[ReworkUnlock]` — visibilidad real (public / unsealed / virtual)
Convierte miembros del juego en API pública REAL: no reflexión, el propio IL
queda reescrito. Tu mod compila contra el miembro (`pawn.jobs.curJob` directo:
compilable, autocompletable, coste cero). Admite múltiples instancias sobre un
método, una clase o el ensamblado:
```csharp
[ReworkUnlock(Type = "Verse.AI.Group.Lord", Member = "curJob")]        // private → public
[ReworkUnlock(Type = "RimWorld.CompActivity", Unseal = true)]          // sealed → heredable
[ReworkUnlock(Type = "Verse.JobDriver", Member = "TryMakePreToilReservations", MakeVirtual = true)]
internal static class MisUnlocks { private static void Portador() { } } // (solo portador)
```
- `Member == null` → el tipo entero: `Public` (default true), `Unseal` (default true).
- `Member` → campo, método, propiedad o tipo anidado: `Public`, y en métodos
  `MakeVirtual` (de instancia, no virtual) para abrirlo a overrides.
- Cambios de metadatos puros: no alteran ningún comportamiento existente.

### 4. `[ReworkInline]` — inline perfecto de getters triviales
Sustituye todas las llamadas a un getter trivial (`ldarg.0; ldfld F; ret` /
`ldsfld F; ret`) por el acceso directo al campo. El JIT ya no ve una llamada.
```csharp
[ReworkInline(Type = "Verse.Pawn", Method = "get_Name")]     // 189 call-sites en 1.6
private static void Portador() { }
```
- Validación estricta: cuerpo EXACTAMENTE trivial, sin parámetros, no genérico,
  y NO virtual (o virtual+final) — inlinear un virtual rompería el polimorfismo.
  Si no cumple, error claro y no se toca nada.
- Si el campo de respaldo no es público, se hace público (metadata, seguro) para
  el acceso cruzado entre ensamblados.
- Semántica: con receptor null, la NullReferenceException pasa de la llamada al
  acceso al campo (mismo punto de fallo práctico).

### 5. `[ReworkConst]` — constantes que nacen con tu valor
Pliega TODOS los `ldsfld` de un `static readonly` del juego a un literal, en
todos los ensamblados reescribibles. Solo es posible aquí: tras el boot los
call-sites ya tienen el valor grabado. El `.cctor` se conserva (la reflexión
sigue viendo el valor original); lo que cambia es lo que el CÓDIGO lee.
```csharp
[ReworkConst(Type = "RimWorld.JobDriver_AcceptRole", Field = "FacingUpdateInterval", Value = 40)]
private static void Portador() { }
```
- Valores soportados: `bool`, enteros, `float`, `double`, `string` (null para
  referencias) y enums (valor entero).
- Un `const` de IL (literal) se detecta y se explica: su valor está QUEMADO en
  cada call-site desde que el juego se compiló (no hay lecturas que reescribir);
  para esos casos usa `[ReworkRedirect]`/`[ReworkPatch]` o elige un `static readonly`.
- Aviso: cambiar el valor de una constante del juego CAMBIA EL JUEGO — es
  exactamente su utilidad; verifícalo en partida.

### Auto-demo y verificación
El propio framework es consumidor real de las cinco (anti-fantasma §44):
`ReworkILSelfDemo` redirige `Log.Message` (contador), sobrescribe
`Building.GetInspectString` (CallBase, salida idéntica), desbloquea
`Lord.curJob` y `CompActivity`, inlinea `Pawn.Name` y `MapDrawLayer.Map`, y
pliega `FacingUpdateInterval` a su mismo valor. En el arranque, `ReworkILVerifier`
comprueba los efectos por reflexión sobre el Assembly-CSharp REESCRITO y vuelca
la telemetría de la pasada 1 al log: busca `[ReworkIL] ... VERIFICADA ✓`.

## Configuración y Opciones (Bloque 18)

Rework Reforjed incluye un sistema completo de configuración persistente accesible desde el menú de opciones de RimWorld (**Opciones > Configuración de mods > Rework Reforjed**) y por código a través de `0ReworkAPI` (`ReworkConfig`):

- **18.1 — Menú de opciones:** categoría "Rework Reforjed" con interfaz gráfica interactiva en RimWorld (`DoSettingsWindowContents`).
- **18.2 — Perfiles de rendimiento (`PerfProfile`):**
  - `Equilibrado`: configuración estándar equilibrada con multihilo activo.
  - `MaximoRendimiento`: multihilo activo y logs detallados desactivados.
  - `AhorroMemoria`: multihilo desactivado (hilo único para ahorro de memoria).
- **18.3 — Perfiles de compatibilidad (`CompatProfile`):**
  - `Estandar`: modo seguro proactivo activo.
  - `Estricto`: máxima verificación de seguridad.
  - `Permisivo`: modo seguro desactivado para entornos experimentales.
- **18.4 — Activar/desactivar multihilo:** controla `ReworkJobs.Pool`. Si se desactiva, el pool opera con 0 workers (fallback single-thread en línea).
- **18.5 — Backend de serialización automática:** activa o desactiva la inyección automática de llamadas Scribe por `FieldScribe` sobre campos con `[ReworkField(Serialize=true)]`.
- **18.6 — Modo seguro proactivo:** permite habilitar o deshabilitar la guarda que previene reescribir si la verificación de layout nativo falla.
- **18.7 — Logs detallados (Verbose):** habilita trazas detalladas de diagnóstico en `Rework.log`.
- **18.8 — Configuración por mod externo:** gestión interactiva y programática para excluir mods específicos del pipeline de parcheo (`ReworkConfig.ExcludedMods`).
- **18.9 — Guardar / Cargar configuración:** persistencia automática en XML mediante el subsistema de `ModSettings` de RimWorld (`Scribe_Values` y `Scribe_Collections`).
- **18.10 — Resetear configuración:** botón en UI y método `Reset()` para restablecer todos los valores por defecto.

## Herramientas de Diagnóstico y DevTools en Runtime (Bloque 19)

Rework Reforjed incluye una suite integrada de diagnóstico y herramientas accesible en tiempo real mediante la ventana flotante interactiva `Dialog_ReworkInspector` (disponible desde **Opciones > Configuración de mods > Rework Reforjed > 🔍 Abrir Rework DevTools & Inspector**):

- **19.1. Inspector de clases en runtime:** Explorador en vivo de todos los tipos cargados por ensamblado con filtro de búsqueda en tiempo real, recuento de campos y métodos.
- **19.2. Inspector de campos inyectados:** Lista completa de variables reales añadidas mediante `[ReworkField]` activas en el layout nativo de las clases del juego.
- **19.3. Inspector de métodos parcheados y hooks:** Visualizador de hooks de ciclo de vida (`[ReworkHook]`), parches de mono-cecil y desvíos de inicializadores activos.
- **19.4. Inspector de saves:** Explorador de archivos `.rws` y `.rwbin` en la carpeta de guardados con análisis de tamaño y fecha.
- **19.5. Editor/Visor de saves:** Análisis de integridad de partidas, verificación del centinela `#REFORJED` en binarios y previsualización de metadatos.
- **19.6. Consola de desarrollo propia:** Terminal interactiva con historial y cuadro de comandos ejecutable en partida.
- **19.7. Comandos de debug:** Soporte para comandos rápidos (`help`, `gc`, `clearbus`, `reloadlive`, `diag`, `fps`).
- **19.8. Perfilador integrado:** Telemetría en tiempo real de memoria gestionada (GC RAM en MB), frametime en milisegundos y estimación de FPS.
- **19.9. Visualizador de hilos:** Monitoreo del estado del pool de workers asíncronos (`Rework.Threading`), recuento de procesadores y modo de ejecución.
- **19.10. Visualizador de jobs y contenido:** Recuento y estado de Defs declarativos (`[ReworkJob]`, `[ReworkIncident]`, `[ReworkRecipe]`, etc.) y mutaciones aplicadas (`[ReworkMutate]`).
- **19.11. Visualizador de caché:** Control y purgado manual en caliente de `GenTypes.ClearCache()` y `ReworkBus`.

## Compatibilidad Externa y Resiliencia (Bloque 20)

Rework Reforjed implementa una arquitectura de compatibilidad pasiva y aislamiento estricto para garantizar coexistencia sin fallos con otros mods y frameworks del ecosistema:

- **20.1 — Aislamiento de librerías de parcheo en runtime (`0Harmony.dll`, `HarmonySharedState`):** Rework nunca intenta parchear ni duplicar librerías de parcheo dinámico externas. Se detectan y marcan automáticamente como ensamblados protegidos de solo-lectura (`AllowPatches = false`, `ProcessAttributes = false`), previniendo colisiones de runtime en la pasada 2.
- **20.2 — Blindaje del Motor y BCL:** Se excluyen del swap todos los ensamblados de Unity (`UnityEngine*`, `Unity*`), `Mono.CSharp`, `mscorlib`, `System*` y dependencias de bajo nivel, garantizando que el recolector de basura nativo y el layout de Mono no se alteren.
- **20.3 — Exclusión selectiva por Mod (`ReworkConfig.ExcludedMods`):** Soporte dinámico para excluir cualquier mod por ID o nombre de ensamblado desde la interfaz de opciones o mediante código.
- **20.4 — Modo Seguro Proactivo:** Si la verificación de layout nativo (`layoutOK`) detecta discrepancias en los offsets de Mono, el framework cancela automáticamente la reescritura de campos y permite que el juego arranque en modo vanilla sin provocar crashes.

## Compilar desde el código fuente

```powershell
dotnet build "Source\Rework.slnx" -c Release
dotnet build "Source\ReworkContent\ReworkContent.csproj" -c Release
```

Esperado: "0 Errores" (warnings preexistentes inofensivos). Salida en `..\..\Assemblies\`.
`ReworkContent.csproj` es el módulo de extensiones de contenido declarativas
(`[ReworkGene]`, `[ReworkResearch]`, `[ReworkRaid]`, `[ReworkThought]`, `[ReworkApparel]`,
`[ReworkZoneEffect]`); se compila aparte para no reescribir los binarios principales.

## Alcance actual

Implementado y verificado en su totalidad:
- **Bloques 1 al 20 del Roadmap Oficial:** 100% completados y verificados empíricamente en el juego real (RimWorld 1.6.4850 rev646).
- **Extensiones de Contenido Sin XML (11 características):** `[ReworkRecipe]`, `[ReworkIncident]`, `[ReworkJob]`, `[ReworkWorkGiver]`, `[ReworkGene]`, `[ReworkHediff]`, `[ReworkTrait]`, `[ReworkResearch]`, `[ReworkRaid]`, `[ReworkThought]`, `[ReworkApparel]`.
- **Invenciones Únicas de Rework:**
  - `ReworkBus` (bus de eventos desacoplado)
  - `[ReworkGenStep]` (generación de mundos/mapas)
  - `[ReworkMutate]` (mutaciones de Defs existentes en boot)
  - `ReworkLive` (hot-reload en caliente con recarga segura de DLLs)
  - `ReworkBinaryScribe` (guardado y carga binaria de alta velocidad con centinela `#REFORJED` y sincronización temporal de XML)
  - `[ReworkWatch]` (campos reactivos con callbacks automáticos ante mutaciones)
  - `[ReworkUIPanel]` (UI inyectada en ventanas nativas del juego)
  - `ReworkColonySkill` (Progresión de Colonia: habilidades a nivel asentamiento con XP, niveles y serialización binaria `.rwbin`)
  - `[ReworkAIModifier]` (Modificadores de IA declarativos para evaluar y ajustar prioridades de tareas cognitivas de colonos)
  - `ReworkPawnTimeline` (Timeline de Vida: historial persistente de hitos por colono serializado en `.rwbin`)
  - `[ReworkZoneEffect]` / `ReworkZoneManager` (Efectos de Zona geográficos periódicos sin ThingDef ni ThingComp en XML)
  - `ReworkDynamicMutate` (Mutaciones dinámicas en caliente de Defs con historial y capacidad de reversión)
  - `[ReworkDefBuilder]` (Creación de Defs 100% desde código puro inyectados en DefDatabase sin XML)
  - `[ReworkSchedule]` / `ReworkScheduler` (Scheduler declarativo de ticks del motor sin GameComponents)
  - `[ReworkStatusEffect]` (Buffs y debuffs declarativos configurables sin clases hediff en XML)
  - `ReworkRelation` (Relaciones personalizadas entre colonos sin tocar Defs de relaciones vanilla)
  - `ReworkWorldStore` (Key-Value Store arbitrario por mundo persistido en el save binario sin Scribe)
  - `ReworkStateSnapshot` (Captura y análisis de snapshots compactos de campos [ReworkField])
  - `[ReworkGizmo]` (Botones de acción declarativos para entidades seleccionadas sin subclasificar Gizmo)
  - `ReworkOverlay` (Renderizado directo de capas y semi-transparencias visuales sobre el mapa)
  - `[ReworkAlert]` (Alertas declarativas en pantalla sin AlertDef ni XML)
  - `[ReworkNeed]` / `ReworkNeedRegistry` (Barras de necesidad personalizadas sin NeedDef en XML)
  - `[ReworkTab]` / `ReworkTabRegistry` (Pestañas de inspección de colonos en el panel inferior sin ITab ni XML)
  - `[ReworkInspectString]` (Líneas dinámicas de información en la caja de inspección de entidades)
  - `[ReworkDamageSoak]` / `[ReworkDamageOverTime]` (Daño declarativo: absorción plana por golpe y daño periódico por hediff, sin XML ni HediffComp)
  - `[ReworkKnockback]` (Empuje melee con física de distancia, daño al chocar, stun y explosión opcional)
  - `[ReworkCleave]` (Daño con salpicado a enemigos adyacentes, sin XML)
  - `[ReworkExtraMelee]` (Daños extra de melee por hediff, formato CSV, sin XML)
  - `[ReworkDeflector]` (Desvío de proyectiles con skills y reflect, sobre el arma)
  - `[ReworkShield]` (Escudos físicos con bloqueo, daño, fatiga y descarte)
  - `[ReworkDelayedSpawner]` (Spawns con retardo y repetición, sin XML ni edificios)
  - `[ReworkToggleDef]` (Variantes intercambiables con gizmo, sin XML ni ITab)
  - `[ReworkInstalledPart]` (Piezas instalables con jobs de Construction, sin XML)
  - `[ReworkSlotLoadable]` (Slots de munición/cristales con color, stats, daño y curas)
  - `[ReworkAbility]` + `[ReworkPassive]` (Puente sobre Abilities vanilla + buffs pasivos + IA de casteo)
  - `[ReworkMigration]` / `ReworkMigration` (Sistema de migración segura de datos y campos entre versiones de mods)
  - `[ReworkRequires]` / `ReworkDepGraph` (Grafo y resolución automática del orden de carga entre mods del ecosistema)
  - `[ReworkCompatWith]` / `ReworkCompat` (Callbacks de compatibilidad condicional desacoplada según mods activos)
  - `ReworkParallel` (Motor de paralelismo seguro para cómputo en segundo plano con retorno al hilo principal)
  - `ReworkCache` (Capa de caché de alto rendimiento con TTL en ticks para bucles pesados)
  - `[ReworkQuest]` / `ReworkQuestRegistry` (Misiones y Quests declarativos 100% en C# sin QuestScript en XML)
  - `[ReworkLore]` / `ReworkLore` (Motor de interpolación y narrativa procedural para cartas, eventos y diálogos)
- **Herramientas de Diagnóstico (Bloque 19):** Suite `Dialog_ReworkInspector` accesible en tiempo de ejecución.
- **Compatibilidad y Resiliencia (Bloque 20):** Aislamiento pasivo de librerías de parcheo en runtime y blindaje del entorno nativo.