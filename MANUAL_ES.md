# Rework Reforjed — Manual

Un framework de parcheo para RimWorld que es un **reemplazo TOTAL de Harmony**: nunca usa
Harmony y reescribe `Assembly-CSharp.dll` **en memoria** con Mono.Cecil **antes** de que el
juego la use. Portable (una carpeta en `Mods/`), autocontenido. Target exclusivo:
**RimWorld 1.6.4850 rev646**.

---

## Qué hace

```
Arranque del juego
  └─ Pasada 1 (constructor del mod):
       • recoge Assembly-CSharp + tus ensamblados
       • reescribe todo en memoria con Mono.Cecil (campos, métodos, hooks, parches…)
       • esconde los originales y carga las copias parcheadas
  └─ Reinicio interno en el sitio: el juego re-arranca su secuencia con los tipos nuevos
       └─ Pasada 2 (verifica; no se reescribe nada)
```

El proceso del juego **nunca se cierra**: solo ves la pantalla de carga reiniciarse una vez.
El `Assembly-CSharp.dll` del disco queda **intacto**.

## Modo seguro

Antes de reescribir nada, Rework comprueba el entorno (runtime Mono y layout nativo).
Si algo es inesperado, **no reescribe nada**: el juego arranca vanilla y Rework solo avisa.

## Instalación

1. Copia la carpeta `Rework Reforjed` a `RimWorld\Mods\`.
2. Actívala en el menú **Mods** de RimWorld.
3. **Orden de carga:** arrastra `Rework Reforjed` **hasta arriba del todo**, justo debajo de
   `Core`/expansiones.
4. La primera apertura reinicia la carga en el sitio automáticamente.

## Desinstalar

Solo desactiva el mod en el menú Mods (el DLL del juego nunca se tocó). El archivo
`Rework.log` en LocalLow puede eliminarse; se regenera automáticamente.

---

## Primeros pasos

Integra un mod existente con el ecosistema Rework referenciando **`0ReworkAPI.dll`**
(y Mono.Cecil solo si escribes transpilers), sin empaquetarla:

```xml
<PackageReference Include="Mono.Cecil" Version="0.11.6" ExcludeAssets="runtime" />
```

Requisitos: referenciar la API sin empaquetarla (`<Private>false</Private>` /
`ExcludeAssets="runtime"`), incluir tu `About/About.xml` y asegurarte de que Rework cargue
antes que tu mod.

### 1. Añadir campos reales — `[ReworkField]`

Añade campos **reales** a **cualquier clase del juego** (primitivas, `string`, clases,
genéricos, colecciones):

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

**Tipos de colección:** `List<T>`, `Dictionary<K,V>`, `HashSet<T>` y arrays (`T[]`)
funcionan en runtime (Add/Remove/Sort/reemplazo entero/…).

#### Valor por defecto — `[ReworkField(Default = ...)]`

Sin inicializador, el campo nace en el default de CLR (0 / null). `Default` inyecta
`this.campo = valor` al inicio de **todos los constructores** del tipo destino:

```csharp
[ReworkField(Default = 1.0f)]
public static extern ref float MiModificador(this Verse.Pawn pawn);

[ReworkField(Default = 42)]
public static extern ref int MiContador(this Verse.Pawn pawn);
```

Soporta primitivas, `string` y enums.

#### Inicializador por método — `[ReworkField(Initializer = "...")]`

Para tipos complejos (`List<>`, estado) el valor inicial no puede ser una constante; nombra
una función estática pública **en la misma clase estática que el accessor**:

```csharp
[ReworkField(Initializer = nameof(CrearInventario))]
public static extern ref List<ThingDef> Inventario(this Verse.Pawn pawn);
public static List<ThingDef> CrearInventario() => new();
```

- 0 parámetros → `campo = Metodo();`; 1 parámetro → recibe `this`.
- Requisitos: método público (acceso entre ensamblados), no void, 0 o 1 parámetro.
- **Tiene prioridad sobre `Default`** si pones ambos.

### 2. Añadir métodos — `[ReworkMethod]`

Añade un método REAL a un tipo del juego: Rework inyecta un **forwarder** que llama a tu
método estático (la lógica vive en tu mod; el miembro es real y visible por reflexión):

```csharp
[ReworkMethod(Type = "Verse.Pawn", Name = "Rework_Saludar")]
public static void Saludar(this Verse.Pawn pawn) => Log.Message($"Saludo de {pawn.NameShortColored}");

[ReworkMethod(Type = "Verse.Game", Instance = false, Name = "Rework_TotalMods")]
public static int TotalMods() => LoadedModManager.RunningModsListForReading.Count;
```

Reglas: método estático y público; `Name` opcional; si el miembro ya existe se omite.

### 3. Añadir propiedades — `[ReworkProperty]`

Añade una propiedad REAL con getter/setter forwarders (emparejados por `Get_`/`Set_` o `Name`):

```csharp
[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static int Get_Rework_SaludoCount(this Verse.Pawn pawn) => pawn.Rework_Logros().Count;

[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static void Set_Rework_SaludoCount(this Verse.Pawn pawn, int value) { /* ... */ }
```

### 4. Añadir metadatos — `[ReworkAnnotate]`

Añade un atributo (metadata) a una clase, método o campo del juego:

```csharp
[ReworkAnnotate(Type = "Verse.Pawn")]                              // clase
[ReworkAnnotate(Type = "Verse.Pawn", Member = "Kill")]             // método
[ReworkAnnotate(Type = "Verse.Pawn", Member = "health")]           // campo
```

Sin un `Attribute` específico se usa el marcador genérico `Rework.Core.ReworkMarkAttribute`.

### 5. Parches libres — `[ReworkPatch]` y transpilers

Dos modos detectados por la firma (conviven y respetan `Priority`):

```csharp
// Directo (Cecil puro): modifica el ModuleDefinition de Assembly-CSharp.
[ReworkPatch]
public static void MiParche(ModuleDefinition module) { /* Cecil puro */ }

// Transpiler amigable: recibe las instrucciones del método destino y devuelve las nuevas.
[ReworkPatch(Type = "RimWorld.SkillRecord", Method = "Learn")]
public static IEnumerable<Instruction> MiTranspiler(IEnumerable<Instruction> instrs, ModuleDefinition module)
{ ... }
```

El transpiler **preserva los manejadores de excepción** (reutiliza el mismo MethodBody). No
borres las instrucciones límite de las regiones EH. Orden de aplicación: Priority desc →
orden de carga del mod → nombre.

#### Helpers de IL — TranspilerHelpers

Rework no tiene Prefix/Postfix (la mecánica "antes/después" de Harmony, descartada aquí).
Los helpers cubren los casos comunes sin pelear con el IL a mano: `Ldc`/`Ldstr`/`Ldnull`,
`Ldarg`/`Starg`, `Call`, `Ldfld`, `Ret`, `ImportMethod`/`ImportType`, `List`/`Prepend`.

### 6. Init patches — correr código una vez al arrancar

```csharp
[ReworkInit]            // prioridad 0
public static void Preparar() { ... }

[ReworkInit(100)]       // prioridad explícita (mayor → primero)
public static void Inicializar() { ... }
```

- **Orden (cross-mod):** Priority desc → orden de carga del mod → nombre.
- **Contención de errores:** si un init lanza, se reporta y se sigue (no rompe el boot).
- Corre en la fase de carga de mods; si necesitas Defs cargados, combina con el vanilla
  `[StaticConstructorOnStartup]`.

### 7. Ciclo de vida declarativo — `[ReworkHook]`

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
| `BabyBorn` | `RimWorld.PregnancyUtility.ApplyBirthOutcome` | la MADRE biológica (el bebé aún no es un Pawn) |
| `CaravanEnteredMap` | `RimWorld.Planet.CaravanEnterMapUtility.Enter` | el `Verse.Map` destino |
| `PawnLeftToCaravan` | `RimWorld.Planet.CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan` | el `Verse.Pawn` que sale |

> ⚠️ **Rendimiento:** `MapTick`, `PawnTick`, `GameComponentTick`, `MapComponentTick`,
> `WorldComponentTick` y `StorytellerTick` se disparan 60/s → haz el cuerpo baratísimo o usa
> un contador/intervalo.
>
> **Contexto por parámetro:** la mayoría pasan `this`; casos especiales:
> `CurrentMapChanged` → el mapa nuevo; `BabyBorn` → la madre biológica;
> `CaravanEnteredMap` → el mapa destino; `PawnLeftToCaravan` → el pawn que sale.
>
> **CaravanSpawned:** corre mientras el ctor aún está en progreso; solo registra.
>
> **NewGameStarted ≠ GameStart:** `GameStart` es cualquier partida (nueva o cargada);
> `NewGameStarted` solo partida nueva.
>
> **PawnCreated** = pawn creado por código (`PostMake`), no un bebé nacido biológicamente
> (eso es `BabyBorn`).

Contrato: estático, público, `void`; 0 o 1 parámetro (el contexto). Si no cumple, se reporta
y se omite (no rompe el boot).

### 8. Campos reactivos — `[ReworkWatch]`

En lugar de hacer polling en cada tick, los callbacks marcados con `[ReworkWatch]` se
invocan automáticamente cuando un campo inyectado (`[ReworkField]`) cambia:

```csharp
[ReworkWatch("Rework_XpMultiplier")]
public static void OnXpMultiplierChanged(Pawn pawn, object oldV, object newV) { /* lógica reactiva */ }
```

- Atributo sobre un método estático; parámetros opcionales `(Pawn)` o `(Pawn, oldValue, newValue)`.
- Fachada pública: `ReworkWatch.NotifyChanged(target, fieldName, old, new)` + `Notifier`
  delegable, accesibles desde cualquier mod externo sin referenciar `Rework.dll`.

### 9. UI inyectada — `[ReworkUIPanel]`

Cecil inyecta un bloque de código dentro de `DoWindowContents(Rect)` de cualquier ventana
del juego. Solo declaras en qué ventana va tu panel y qué dibuja (GUI estándar de Verse:
`Widgets`/`Text`):

```csharp
[ReworkUIPanel("Verse.Dialog_MessageBox", AtStart = false, Priority = 100)]
public static void PanelParaDialogo(Rect inRect)
{
    Text.Font = GameFont.Small;
    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "Mi panel en el diálogo");
}
```

- Firmas soportadas: `static void MiPanel(Window window, Rect inRect)` (2 params) o
  `static void MiPanel(Rect inRect)` (1 param).
- `AtStart = true` → el panel se dibuja DEBAJO del contenido de la ventana; `AtStart = false`
  (default) → se dibuja ENCIMA, visible sobre la ventana.

### 10. Bus de eventos — `ReworkBus` & `[ReworkOn]`

Suscribe métodos estáticos o de instancia a eventos del juego sin polling en `Tick` ni
parchear manualmente:

```csharp
public static class MiSistema
{
    [ReworkOn]
    public static void AlOcurrir(ReworkLifecycleEvent e)
    {
        Log.Message($"Evento {e.Name} detectado reactivamente.");
    }
}

// Registrar y publicar:
ReworkBus.Register(typeof(MiSistema));
ReworkBus.Publish(new ReworkLifecycleEvent("PartidaIniciada"));
```

---

## Contenido declarativo sin XML (Zero-XML)

Todo lo de abajo registra su `Def` automáticamente en `DefDatabase` al arrancar
(`[StaticConstructorOnStartup]`) — no se necesitan archivos XML.

### Trabajos — `[ReworkJob]`

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

- Configurable: `ReportString`, `PlayerInterruptible`, `CasualInterruptible`, `Suspendable`.
- Consulta: `ReworkJobRegistry.Get("MiMod_RepararArmadura")`.
- Clase base opcional: `ReworkJobDriver` provee helpers (`ToilGoto`, `ToilWait`, `ToilDo`).

### Incidentes — `[ReworkIncident]`

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

- Configurable: `Category` ("Misc", "ThreatSmall", "ThreatBig", …), `BaseChance`,
  `TargetTag` ("Map_PlayerHome", "World", …), `LetterLabel`, `LetterText`.
- Consulta: `ReworkIncidentRegistry.Get("MiMod_OlaDeCalorEspiritual")`.
- Clase base opcional: `ReworkIncidentWorker` provee `SendStandardLetter`.

### Asignadores de trabajo — `[ReworkWorkGiver]`

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

- Configurable: `WorkType` ("Hauling", "Cleaning", "Crafting", "Doctor", …), `PriorityInType`,
  `Verb`, `Gerund`, `DirectOrderable`, `ScanThings`, `ScanCells`, `Emergency`.
- El registrador inserta el `WorkGiverDef` ordenado por `PriorityInType` dentro del
  `workGiversByPriority` del `WorkTypeDef` correspondiente.

### Recetas — `[ReworkRecipe]`

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

Registra el `RecipeDef` y lo enlaza automáticamente a las mesas nombradas en `RecipeUsers`.

### Condiciones de salud — `[ReworkHediff]`

```csharp
[ReworkHediff("MiMod_BendicionSolar", Label = "bendición solar", InitialSeverity = 1.0f, IsBad = false)]
public class Hediff_BendicionSolar : HediffWithComps
{
}
```

Registra un `HediffDef` con severidad inicial y clasificación médica.

### Rasgos — `[ReworkTrait]`

```csharp
[ReworkTrait("MiMod_AlmaReforjada", Label = "alma reforjada", Degree = 0, Commonality = 1.0f)]
public class Trait_AlmaReforjada
{
}
```

Registra un `TraitDef`, estructurando sus `degreeDatas` automáticamente.

### Necesidades — `[ReworkNeed]`

```csharp
[ReworkNeed("MiMod_Entusiasmo", Label = "entusiasmo", BaseLevel = 0.8f, FallPerDay = 0.3f, ColonistsOnly = true)]
public class Need_Entusiasmo : Need
{
    public Need_Entusiasmo(Pawn pawn) : base(pawn) { }
    public override void NeedInterval() => CurLevel -= 0.001f;
}
```

Registra un `NeedDef` con tasa de caída y restricciones de colonos.

### Designadores — `[ReworkDesignator]`

```csharp
[ReworkDesignator("Orders")]
public class Designator_Inspeccionar : Designator
{
    public Designator_Inspeccionar() { defaultLabel = "Inspeccionar"; }
    public override AcceptanceReport CanDesignateCell(IntVec3 loc) => true;
    public override void DesignateSingleCell(IntVec3 c) { /* acción */ }
}
```

Enlaza el `Designator` directamente en los `specialDesignatorClasses` de la categoría dada
("Orders", "Zone", "Production", …).

### Generación procedural de mapas — `[ReworkGenStep]`

```csharp
[ReworkGenStep("MiMod_CovilDragon", Order = 650f)]
public class GenStep_CovilDragon : GenStep
{
    public override int SeedPart => 987654;
    public override void Generate(Map map, GenStepParams parms)
    {
        // Generar estructuras o recursos en el mapa nuevo
    }
}
```

### Mutaciones de Defs vanilla — `[ReworkMutate]`

```csharp
[ReworkMutate("CraftingSpot", "useHitPoints", false)]
[ReworkMutate("Human", "baseHealthScale", 1.25f, DefType = typeof(ThingDef))]
public static class MisBalanceos { }
```

Modifica valores de Defs vanilla al arrancar de forma registrada, reversible y visible en el
diagnóstico.

### Misiones — `[ReworkQuest]`

Misiones declarativas 100% en C# (sin QuestScript, sin XML). Regístralas y adminístralas vía
`ReworkQuestRegistry`.

### Genes, investigaciones, raids, pensamientos, prendas, pestañas, líneas de inspección…

El mismo patrón declarativo se extiende a `[ReworkGene]`, `[ReworkResearch]`, `[ReworkRaid]`,
`[ReworkThought]`, `[ReworkApparel]` (contenido), `[ReworkTab]` (pestañas de inspección),
`[ReworkInspectString]` (líneas dinámicas en la caja de inspección), `[ReworkAlert]`
(alertas en pantalla), `[ReworkGizmo]` (botones de acción), `[ReworkSchedule]` (scheduler de
ticks del motor), `[ReworkStatusEffect]` (buffs/debuffs declarativos), `[ReworkAIModifier]`
(modificadores de cognición de IA), `[ReworkZoneEffect]` (efectos de zona geográficos),
`[ReworkLore]` (narrativa procedural) y defs genéricos vía `[ReworkDefBuilder]`
(constructor universal de Defs desde C#).

---

## Serialización automática de campos

`[ReworkField(Serialize = true)]` hace que el campo **viaje en la partida guardada** sin
escribir Scribe a mano:

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
  → `Scribe_Collections.Look`. Etiqueta = nombre del accessor, con `forceSave` y default.
- **Solo el mod que CREA el campo** decide su serialización.
- Tipo no soportado → error claro y sin serialización (no rompe el guardado).
- Los campos ausentes en saves viejos se re-crean al cargar con `Initializer`/`Default`.

---

## Infraestructura avanzada

- **Hot-reload (`ReworkLive`):** actualiza los `.dll` de mods en runtime sin reiniciar el
  juego. Implementa `IReworkLiveReloadable` (`OnBeforeLiveReload` / `OnAfterLiveReload`) o
  suscríbete a `ReworkLive.BeforeReload` / `ReworkLive.AfterReload`.
- **Saves binarios (`ReworkBinaryScribe`):** saves `.rwbin` de alta velocidad con centinela
  atómico `#REFORJED`; al cargar omite el parseo XML (carga en ~1s). Si el binario falta o
  está corrupto, hace fallback al `.rws` clásico. Se mantiene un único respaldo XML periódico
  (`GameComponent_ReworkXmlBackup`, intervalo configurable 5–120 min, 15 min por defecto).
- **Progresión de colonia (`ReworkColonySkill`):** habilidades a nivel asentamiento con
  XP/niveles, persistidas en `.rwbin`.
- **Timeline de pawns (`ReworkPawnTimeline`):** historial persistente de hitos por colono en
  `.rwbin`.
- **Mutaciones dinámicas (`ReworkDynamicMutate`):** mutaciones de Defs en caliente,
  reversibles, con historial.
- **Relaciones (`ReworkRelation`):** relaciones personalizadas entre pawns sin tocar Defs
  vanilla.
- **Key-value store por mundo (`ReworkWorldStore`):** almacenamiento arbitrario por mundo
  persistido en el save binario.
- **Snapshots de estado (`ReworkStateSnapshot`):** captura y análisis de snapshots compactos
  de valores `[ReworkField]`.
- **Overlays (`ReworkOverlay`):** renderizado directo de capas y semitransparencias sobre el
  mapa.
- **Migraciones (`[ReworkMigration]`):** migración segura de datos/campos entre versiones de
  mods.
- **Grafo de dependencias (`ReworkDepGraph` / `[ReworkRequires]`):** resolución automática
  del orden de carga entre mods del ecosistema.
- **Compatibilidad condicional (`[ReworkCompatWith]` / `ReworkCompat`):** callbacks
  desacoplados según mods activos.
- **Paralelismo (`ReworkParallel`):** cómputo seguro en segundo plano con retorno al hilo
  principal.
- **Caché (`ReworkCache`):** capa de caché de alto rendimiento con TTL en ticks para bucles
  pesados.
- **Logging (`Rework.log`):** toda la actividad de Rework se espeja a un archivo dedicado en
  LocalLow, junto a `Player.log`, UTF-8 con BOM. Volca excepciones con contexto vía
  `Rework.Core.ErrorPrinter.Print("contexto", e)` / `Try("contexto", () => ...)`.

---

## Configuración

La configuración persistente está disponible desde el menú de opciones de RimWorld
(**Opciones > Configuración de mods > Rework Reforjed**) y por código vía `0ReworkAPI`
(`ReworkConfig`):

- **Perfiles de rendimiento (`PerfProfile`):** `Equilibrado` (equilibrado, multihilo on),
  `MaximoRendimiento` (multihilo, logs detallados off), `AhorroMemoria` (un solo hilo).
- **Perfiles de compatibilidad (`CompatProfile`):** `Estandar` (modo seguro proactivo on),
  `Estricto` (máximas verificaciones), `Permisivo` (modo seguro off, experimental).
- **Toggle de multihilo:** controla `ReworkJobs.Pool`; off → 0 workers (fallback single-thread
  en línea).
- **Backend de auto-serialización:** activa/desactiva la inyección automática de Scribe para
  `[ReworkField(Serialize=true)]`.
- **Modo seguro proactivo:** activa/desactiva la guarda que evita reescribir si la verificación
  del layout nativo falla.
- **Logs detallados:** habilita trazas de diagnóstico en `Rework.log`.
- **Exclusión por mod (`ReworkConfig.ExcludedMods`):** excluye mods específicos del pipeline
  de parcheo.
- **Guardar/Cargar y Reset:** persistencia XML automática vía los `ModSettings` de RimWorld;
  `Reset()` restaura los defaults.

---

## Diagnóstico y DevTools

Abre el **Inspector de Runtime** (`Dialog_ReworkInspector`) desde
**Opciones > Configuración de mods > Rework Reforjed > 🔍 Abrir Rework DevTools & Inspector**:

- Explorador de clases en vivo por ensamblado con filtro de búsqueda, recuento de
  campos/métodos.
- Inspector de campos inyectados.
- Visor de métodos parcheados y hooks.
- Explorador de saves (`.rws` / `.rwbin`) con análisis de tamaño/fecha; verificación de
  integridad del centinela `#REFORJED`.
- Consola de desarrollo propia con historial (`help`, `gc`, `clearbus`, `reloadlive`,
  `diag`, `fps`).
- Perfilador integrado (RAM de GC en MB, frametime en ms, estimación de FPS).
- Visor de hilos (workers de `Rework.Threading`, nº de núcleos, modo de ejecución).
- Contador de jobs/contenido (Defs declarativos y mutaciones aplicadas).
- Visor de caché con purga en caliente de `GenTypes.ClearCache()` y `ReworkBus`.

---

## Orden entre mods (cross-mod)

- **Campos:** el primer accessor con un nombre crea el campo (y su inicializador); los
  siguientes con el mismo nombre+tipo **se enlazan a él** (campo compartido); mismo nombre con
  otro tipo → **conflicto** (accessor reescrito para lanzar). El primer mod en orden de carga
  gana.
- **Parches:** orden = Priority desc → orden de carga del mod → nombre (estable). Los
  transpilers sobre el mismo método se componen en ese orden.

## Nombres de ensamblado duplicados

Si dos mods traen un ensamblado con el mismo nombre, Rework admite ambos (el primero es el
principal para el resolver; el resto se procesan y recargan desde sus propias rutas). Buena
práctica: mantén nombres de ensamblado únicos y referencia la API con
`<Private>false</Private>` (nunca re-empaques `0ReworkAPI.dll`/`Mono.Cecil.dll`).

---

## Compilar desde el código fuente

```powershell
dotnet build "Source\Rework.slnx" -c Release
```

Esperado: "0 Errores" (warnings preexistentes inofensivos). La salida va a `..\..\Assemblies\`.

---

## Lista de características

- Inyección estructural: `[ReworkField]`, `[ReworkMethod]`, `[ReworkProperty]`,
  `[ReworkAnnotate]`.
- Parcheo: `[ReworkPatch]` (Cecil directo + transpilers), `TranspilerHelpers`, `[ReworkInit]`,
  `[ReworkHook]` (21 puntos de ciclo de vida).
- Runtime: `ReworkBus` / `[ReworkOn]`, `[ReworkWatch]`, `[ReworkUIPanel]`, `[ReworkGenStep]`,
  `[ReworkMutate]`, `ReworkLive`, `ReworkDynamicMutate`, `ReworkBinaryScribe`,
  `ReworkColonySkill`, `ReworkPawnTimeline`, `ReworkRelation`, `ReworkWorldStore`,
  `ReworkStateSnapshot`, `ReworkOverlay`, `[ReworkGizmo]`, `[ReworkAlert]`, `[ReworkSchedule]`,
  `[ReworkStatusEffect]`, `[ReworkAIModifier]`, `[ReworkZoneEffect]`, `[ReworkMigration]`,
  `ReworkDepGraph`, `[ReworkCompatWith]`, `ReworkParallel`, `ReworkCache`, `[ReworkQuest]`,
  `[ReworkLore]`.
- Contenido Zero-XML: `[ReworkRecipe]`, `[ReworkIncident]`, `[ReworkJob]`,
  `[ReworkWorkGiver]`, `[ReworkGene]`, `[ReworkHediff]`, `[ReworkTrait]`, `[ReworkResearch]`,
  `[ReworkRaid]`, `[ReworkThought]`, `[ReworkApparel]`, `[ReworkNeed]`, `[ReworkTab]`,
  `[ReworkInspectString]`, `[ReworkDefBuilder]`.
- Diagnóstico: suite de runtime `Dialog_ReworkInspector`.
- Compatibilidad: aislamiento pasivo de Harmony y blindaje del entorno nativo.