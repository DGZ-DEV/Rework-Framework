# Rework Reforjed — Manual

Framework de parcheo para RimWorld que es un **reemplazo TOTAL de Harmony**: no usa
Harmony nunca (ni parchea IL de métodos del juego en runtime). Reescribe
`Assembly-CSharp.dll` **en memoria** con Mono.Cecil **antes** de que el juego la use
(modelo de Prepatcher). Portable (una carpeta en `Mods/`), autocontenido. Target
exclusivo: **RimWorld 1.6.4850 rev646**.

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

En Rework no hay Prefix/Postfix (mecánica de Harmony, inferior: solo "antes/después";
**descartada**). Los helpers cubren los casos comunes sin pelear con el IL a mano:
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

- **20.1 — Aislamiento de Harmony (`0Harmony.dll` y `HarmonySharedState`):** Rework nunca intenta parchear ni duplicar librerías de parcheo dinámico externas. Se detectan y marcan automáticamente como ensamblados protegidos de solo-lectura (`AllowPatches = false`, `ProcessAttributes = false`), previniendo colisiones de runtime en la pasada 2.
- **20.2 — Blindaje del Motor y BCL:** Se excluyen del swap todos los ensamblados de Unity (`UnityEngine*`, `Unity*`), `Mono.CSharp`, `mscorlib`, `System*` y dependencias de bajo nivel, garantizando que el recolector de basura nativo y el layout de Mono no se alteren.
- **20.3 — Exclusión selectiva por Mod (`ReworkConfig.ExcludedMods`):** Soporte dinámico para excluir cualquier mod por ID o nombre de ensamblado desde la interfaz de opciones o mediante código.
- **20.4 — Modo Seguro Proactivo:** Si la verificación de layout nativo (`layoutOK`) detecta discrepancias en los offsets de Mono, el framework cancela automáticamente la reescritura de campos y permite que el juego arranque en modo vanilla sin provocar crashes.

## Compilar desde el código fuente

```powershell
dotnet build "Source\Rework.slnx" -c Release
```

Esperado: "0 Errores" (warnings preexistentes inofensivos). Salida en `..\..\Assemblies\`.

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
  - `[ReworkUIPanel]` (UI Inyectada en ventanas nativas sin Harmony)
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
  - `[ReworkMigration]` / `ReworkMigration` (Sistema de migración segura de datos y campos entre versiones de mods)
  - `[ReworkRequires]` / `ReworkDepGraph` (Grafo y resolución automática del orden de carga entre mods del ecosistema)
  - `[ReworkCompatWith]` / `ReworkCompat` (Callbacks de compatibilidad condicional desacoplada según mods activos)
  - `ReworkParallel` (Motor de paralelismo seguro para cómputo en segundo plano con retorno al hilo principal)
  - `ReworkCache` (Capa de caché de alto rendimiento con TTL en ticks para bucles pesados)
  - `[ReworkQuest]` / `ReworkQuestRegistry` (Misiones y Quests declarativos 100% en C# sin QuestScript en XML)
  - `[ReworkLore]` / `ReworkLore` (Motor de interpolación y narrativa procedural para cartas, eventos y diálogos)
- **Herramientas de Diagnóstico (Bloque 19):** Suite `Dialog_ReworkInspector` accesible en tiempo de ejecución.
- **Compatibilidad y Resiliencia (Bloque 20):** Aislamiento pasivo de Harmony y blindaje del entorno nativo.