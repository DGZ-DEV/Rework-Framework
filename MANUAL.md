# Rework Reforjed — Manual

A patching framework for RimWorld that is a **complete replacement for Harmony**: it never
uses Harmony and re-writes `Assembly-CSharp.dll` **in memory** with Mono.Cecil **before**
the game uses it. Portable (one folder in `Mods/`), self-contained. Exclusive target:
**RimWorld 1.6.4850 rev646**.

---

## What it does

```
Game startup
  └─ Pass 1 (mod constructor):
       • collects Assembly-CSharp + your assemblies
       • rewrites everything in memory with Mono.Cecil (fields, methods, hooks, patches…)
       • hides the originals and loads the patched copies
  └─ In-place internal restart: the game reboots its sequence with the new types
       └─ Pass 2 (verifies; nothing is re-written)
```

The game process **never closes**: you only see the loading screen restart once.
The `Assembly-CSharp.dll` on disk stays **untouched**.

## Safe mode

Before rewriting anything, Rework checks the environment (Mono runtime and native layout).
If anything is unexpected, it **does not rewrite anything**: the game starts vanilla and
Rework only warns.

## Installation

1. Copy the `Rework Reforjed` folder into `RimWorld\Mods\`.
2. Enable it in the RimWorld **Mods** menu.
3. **Load order:** drag `Rework Reforjed` **to the very top**, right below `Core`/expansions.
4. First launch restarts the loading in place automatically.

## Uninstall

Just disable the mod in the Mods menu (the game DLL was never touched). The `Rework.log`
file in LocalLow can be deleted; it is regenerated automatically.

---

## Getting started

Integrate an existing mod with the Rework ecosystem by referencing **`0ReworkAPI.dll`**
(and Mono.Cecil only if you write transpilers), without bundling it:

```xml
<PackageReference Include="Mono.Cecil" Version="0.11.6" ExcludeAssets="runtime" />
```

Requirements: reference the API without packaging it (`<Private>false</Private>` /
`ExcludeAssets="runtime"`), ship your `About/About.xml`, and make sure Rework loads before
your mod.

### 1. Adding real fields — `[ReworkField]`

Adds **real** fields to **any game class** (primitives, `string`, classes, generics,
collections):

```csharp
[ReworkField]
public static extern ref int MiCampo(this Verse.Pawn pawn);       // field on Pawn

[ReworkField]
public static extern ref List<string> Notas(this Verse.MapComponent mc); // field on every MapComponent
```

Usage from a mod:

```csharp
using Rework;
pawn.MiCampo() = 5;               // int setter
int v = pawn.MiCampo();           // int getter
pawn.MiCadena() = "texto";        // string setter
string s = pawn.MiCadena();       // string getter
```

**Collection types:** `List<T>`, `Dictionary<K,V>`, `HashSet<T>` and arrays (`T[]`) work
at runtime (Add/Remove/Sort/full replacement/…).

#### Default value — `[ReworkField(Default = ...)]`

Without an initializer the field starts at the CLR default (0 / null). `Default` injects
`this.field = value` at the start of **all constructors** of the target type:

```csharp
[ReworkField(Default = 1.0f)]
public static extern ref float MiModificador(this Verse.Pawn pawn);

[ReworkField(Default = 42)]
public static extern ref int MiContador(this Verse.Pawn pawn);
```

Supports primitives, `string` and enums.

#### Per-method initializer — `[ReworkField(Initializer = "...")]`

For complex types (`List<>`, state) the initial value cannot be a constant; name a public
static function **in the same static class as the accessor**:

```csharp
[ReworkField(Initializer = nameof(CrearInventario))]
public static extern ref List<ThingDef> Inventario(this Verse.Pawn pawn);
public static List<ThingDef> CrearInventario() => new();
```

- 0 parameters → `campo = Metodo();`; 1 parameter → receives `this`.
- Requirements: public method (cross-assembly access), non-void, 0 or 1 parameter.
- **Takes priority over `Default`** if both are set.

### 2. Adding methods — `[ReworkMethod]`

Adds a REAL method to a game type: Rework injects a **forwarder** that calls your static
method (the logic lives in your mod; the member is real and visible via reflection):

```csharp
[ReworkMethod(Type = "Verse.Pawn", Name = "Rework_Saludar")]
public static void Saludar(this Verse.Pawn pawn) => Log.Message($"Saludo de {pawn.NameShortColored}");

[ReworkMethod(Type = "Verse.Game", Instance = false, Name = "Rework_TotalMods")]
public static int TotalMods() => LoadedModManager.RunningModsListForReading.Count;
```

Rules: static and public method; `Name` optional; if the member already exists it is skipped.

### 3. Adding properties — `[ReworkProperty]`

Adds a REAL property with getter/setter forwarders (paired by `Get_`/`Set_` or `Name`):

```csharp
[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static int Get_Rework_SaludoCount(this Verse.Pawn pawn) => pawn.Rework_Logros().Count;

[ReworkProperty(Type = "Verse.Pawn", Name = "Rework_SaludoCount")]
public static void Set_Rework_SaludoCount(this Verse.Pawn pawn, int value) { /* ... */ }
```

### 4. Adding metadata — `[ReworkAnnotate]`

Adds an attribute (metadata) to a game class, method or field:

```csharp
[ReworkAnnotate(Type = "Verse.Pawn")]                              // class
[ReworkAnnotate(Type = "Verse.Pawn", Member = "Kill")]             // method
[ReworkAnnotate(Type = "Verse.Pawn", Member = "health")]           // field
```

Without a specific `Attribute` the generic marker `Rework.Core.ReworkMarkAttribute` is used.

### 5. Free patches — `[ReworkPatch]` and transpilers

Two modes detected by signature (they coexist and respect `Priority`):

```csharp
// Direct (pure Cecil): modifies the ModuleDefinition of Assembly-CSharp.
[ReworkPatch]
public static void MiParche(ModuleDefinition module) { /* pure Cecil */ }

// Friendly transpiler: receives the instructions of the target method and returns the new ones.
[ReworkPatch(Type = "RimWorld.SkillRecord", Method = "Learn")]
public static IEnumerable<Instruction> MiTranspiler(IEnumerable<Instruction> instrs, ModuleDefinition module)
{ ... }
```

The transpiler **preserves exception handlers** (it reuses the same MethodBody). Do not
delete EH boundary instructions. Application order: Priority desc → mod load order → name.

#### IL helpers — TranspilerHelpers

Rework has no Prefix/Postfix (Harmony's "before/after" mechanic, which is discarded here).
The helpers cover common cases without hand-fighting IL: `Ldc`/`Ldstr`/`Ldnull`,
`Ldarg`/`Starg`, `Call`, `Ldfld`, `Ret`, `ImportMethod`/`ImportType`, `List`/`Prepend`.

### 6. Init patches — run code once at startup

```csharp
[ReworkInit]            // priority 0
public static void Preparar() { ... }

[ReworkInit(100)]       // explicit priority (higher → first)
public static void Inicializar() { ... }
```

- **Order (cross-mod):** Priority desc → mod load order → name.
- **Error containment:** if an init throws, it is reported and execution continues (boot does not break).
- Runs in the mod-loading phase; if you need Defs loaded, combine with vanilla
  `[StaticConstructorOnStartup]`.

### 7. Declarative lifecycle — `[ReworkHook]`

`[ReworkHook(ReworkHookPoint.X)]` hooks your static method **at the start** of the game
method that represents the event:

| Hook | Game method | Context (optional parameter) |
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
| `CurrentMapChanged` | `Verse.Game.set_CurrentMap` | the NEW `Verse.Map` (setter parameter) |
| `CaravanSpawned` | `RimWorld.Planet.Caravan` (ctor) | `RimWorld.Planet.Caravan` (under construction) |
| `NewGameStarted` | `Verse.Game.InitNewGame` | `Verse.Game` |
| `PawnCreated` | `Verse.Pawn.PostMake` | `Verse.Pawn` |
| `StorytellerTick` | `RimWorld.Storyteller.StorytellerTick` | `RimWorld.Storyteller` |
| `GameEnded` | `Verse.Game.Dispose` | `Verse.Game` (exiting) |
| `GameSaving` | `Verse.GameDataSaveLoader.SaveGame` | — (no context) |
| `BabyBorn` | `RimWorld.PregnancyUtility.ApplyBirthOutcome` | the biological MOTHER (the baby is not a Pawn yet) |
| `CaravanEnteredMap` | `RimWorld.Planet.CaravanEnterMapUtility.Enter` | the destination `Verse.Map` |
| `PawnLeftToCaravan` | `RimWorld.Planet.CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan` | the leaving `Verse.Pawn` |

> ⚠️ **Performance:** `MapTick`, `PawnTick`, `GameComponentTick`, `MapComponentTick`,
> `WorldComponentTick` and `StorytellerTick` fire 60/s → keep the body extremely cheap or use
> a counter/interval.
>
> **Context by parameter:** most pass the `this` object; special cases:
> `CurrentMapChanged` → the new map; `BabyBorn` → the biological mother;
> `CaravanEnteredMap` → the destination map; `PawnLeftToCaravan` → the leaving pawn.
>
> **CaravanSpawned:** runs while the ctor is still in progress; only register things.
>
> **NewGameStarted ≠ GameStart:** `GameStart` is any game (new or loaded);
> `NewGameStarted` is new games only.
>
> **PawnCreated** = pawn created by code (`PostMake`), not a biologically born baby (that is
> `BabyBorn`).

Contract: static, public, `void`; 0 or 1 parameter (the context). If it does not comply, it
is reported and skipped (boot does not break).

### 8. Reactive fields — `[ReworkWatch]`

Instead of polling every tick, callbacks marked with `[ReworkWatch]` are invoked
automatically when an injected field (`[ReworkField]`) changes:

```csharp
[ReworkWatch("Rework_XpMultiplier")]
public static void OnXpMultiplierChanged(Pawn pawn, object oldV, object newV) { /* reactive logic */ }
```

- Attribute on a static method; optional parameters `(Pawn)` or `(Pawn, oldValue, newValue)`.
- Public facade: `ReworkWatch.NotifyChanged(target, fieldName, old, new)` + delegable
  `Notifier`, reachable from any external mod without referencing `Rework.dll`.

### 9. Injected UI — `[ReworkUIPanel]`

Cecil injects a code block inside `DoWindowContents(Rect)` of any game window. You only
declare which window gets your panel and what it draws (standard Verse GUI: `Widgets`/`Text`):

```csharp
[ReworkUIPanel("Verse.Dialog_MessageBox", AtStart = false, Priority = 100)]
public static void PanelParaDialogo(Rect inRect)
{
    Text.Font = GameFont.Small;
    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "My panel on the dialog");
}
```

- Supported signatures: `static void MiPanel(Window window, Rect inRect)` (2 params) or
  `static void MiPanel(Rect inRect)` (1 param).
- `AtStart = true` → panel drawn BELOW the window content; `AtStart = false` (default) →
  drawn ABOVE, visible on top of the window.

### 10. Event bus — `ReworkBus` & `[ReworkOn]`

Subscribe static or instance methods to game events without polling in `Tick` or patching
manually:

```csharp
public static class MiSistema
{
    [ReworkOn]
    public static void AlOcurrir(ReworkLifecycleEvent e)
    {
        Log.Message($"Event {e.Name} detected reactively.");
    }
}

// Register and publish:
ReworkBus.Register(typeof(MiSistema));
ReworkBus.Publish(new ReworkLifecycleEvent("PartidaIniciada"));
```

---

## Declarative content without XML (Zero-XML)

Everything below registers its `Def` automatically in `DefDatabase` at startup
(`[StaticConstructorOnStartup]`) — no XML files needed.

### Jobs — `[ReworkJob]`

```csharp
[ReworkJob("MiMod_RepararArmadura", ReportString = "Repairing armor.")]
public class JobDriver_RepararArmadura : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    protected override IEnumerable<Toil> MakeNewToils()
    {
        yield return Toils_Goto.Goto(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(120);
        yield return Toils_General.Do(() => { /* repair logic */ });
    }
}
```

- Configurable: `ReportString`, `PlayerInterruptible`, `CasualInterruptible`, `Suspendable`.
- Lookup: `ReworkJobRegistry.Get("MiMod_RepararArmadura")`.
- Optional base class: `ReworkJobDriver` provides helpers (`ToilGoto`, `ToilWait`, `ToilDo`).

### Incidents — `[ReworkIncident]`

```csharp
[ReworkIncident("MiMod_OlaDeCalorEspiritual", Category = "Misc", BaseChance = 1.2f,
                 LetterLabel = "Spiritual heat", LetterText = "The air vibrates with psychic energy.")]
public class IncidentWorker_CalorEspiritual : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms) => true;

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        // Event logic
        return true;
    }
}
```

- Configurable: `Category` ("Misc", "ThreatSmall", "ThreatBig", …), `BaseChance`,
  `TargetTag` ("Map_PlayerHome", "World", …), `LetterLabel`, `LetterText`.
- Lookup: `ReworkIncidentRegistry.Get("MiMod_OlaDeCalorEspiritual")`.
- Optional base class: `ReworkIncidentWorker` provides `SendStandardLetter`.

### Work givers — `[ReworkWorkGiver]`

```csharp
[ReworkWorkGiver("MiMod_ReparadorArmaduraGiver", WorkType = "Crafting", PriorityInType = 70, Verb = "repair", Gerund = "repairing")]
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
- The registry inserts the `WorkGiverDef` ordered by `PriorityInType` inside the
  `workGiversByPriority` of the matching `WorkTypeDef`.

### Recipes — `[ReworkRecipe]`

```csharp
[ReworkRecipe("MiMod_FabricarVenda", Label = "craft bandage", WorkAmount = 300f, RecipeUsers = "CraftingSpot")]
public class Recipe_FabricarVenda : RecipeWorker
{
    public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
    {
        // Application logic
    }
}
```

Registers the `RecipeDef` and links it automatically to the tables named in `RecipeUsers`.

### Health conditions — `[ReworkHediff]`

```csharp
[ReworkHediff("MiMod_BendicionSolar", Label = "solar blessing", InitialSeverity = 1.0f, IsBad = false)]
public class Hediff_BendicionSolar : HediffWithComps
{
}
```

Registers a `HediffDef` with initial severity and medical classification.

### Traits — `[ReworkTrait]`

```csharp
[ReworkTrait("MiMod_AlmaReforjada", Label = "reforged soul", Degree = 0, Commonality = 1.0f)]
public class Trait_AlmaReforjada
{
}
```

Registers a `TraitDef`, structuring its `degreeDatas` automatically.

### Needs — `[ReworkNeed]`

```csharp
[ReworkNeed("MiMod_Entusiasmo", Label = "enthusiasm", BaseLevel = 0.8f, FallPerDay = 0.3f, ColonistsOnly = true)]
public class Need_Entusiasmo : Need
{
    public Need_Entusiasmo(Pawn pawn) : base(pawn) { }
    public override void NeedInterval() => CurLevel -= 0.001f;
}
```

Registers a `NeedDef` with fall rate and colonist restrictions.

### Designators — `[ReworkDesignator]`

```csharp
[ReworkDesignator("Orders")]
public class Designator_Inspeccionar : Designator
{
    public Designator_Inspeccionar() { defaultLabel = "Inspect"; }
    public override AcceptanceReport CanDesignateCell(IntVec3 loc) => true;
    public override void DesignateSingleCell(IntVec3 c) { /* action */ }
}
```

Links the `Designator` directly in the `specialDesignatorClasses` of the given category
("Orders", "Zone", "Production", …).

### Procedural map generation — `[ReworkGenStep]`

```csharp
[ReworkGenStep("MiMod_CovilDragon", Order = 650f)]
public class GenStep_CovilDragon : GenStep
{
    public override int SeedPart => 987654;
    public override void Generate(Map map, GenStepParams parms)
    {
        // Generate structures or resources on the new map
    }
}
```

### Vanilla Def mutations — `[ReworkMutate]`

```csharp
[ReworkMutate("CraftingSpot", "useHitPoints", false)]
[ReworkMutate("Human", "baseHealthScale", 1.25f, DefType = typeof(ThingDef))]
public static class MisBalanceos { }
```

Modifies vanilla Def values at startup in a registered, reversible, diagnostic-visible way.

### Quests — `[ReworkQuest]`

Declarative missions fully in C# (no QuestScript, no XML). Register and manage via
`ReworkQuestRegistry`.

### Genes, researches, raids, thoughts, apparel, tabs, inspect strings…

The same declarative pattern extends to `[ReworkGene]`, `[ReworkResearch]`, `[ReworkRaid]`,
`[ReworkThought]`, `[ReworkApparel]` (content), `[ReworkTab]` (inspection tabs),
`[ReworkInspectString]` (dynamic lines in the inspect box), `[ReworkAlert]` (on-screen
alerts), `[ReworkGizmo]` (action buttons), `[ReworkSchedule]` (engine tick scheduler),
`[ReworkStatusEffect]` (declarative buffs/debuffs), `[ReworkAIModifier]` (AI cognition
modifiers), `[ReworkZoneEffect]` (geographic zone effects), `[ReworkLore]` (procedural
narrative), and generic defs via `[ReworkDefBuilder]` (universal Def constructor from C#).

---

## Automatic field serialization

`[ReworkField(Serialize = true)]` makes the field **travel in the saved game** without
writing Scribe by hand:

```csharp
[ReworkField(Default = 1.0f, Serialize = true)]
public static extern ref float Rework_Reputacion(this Pawn p);

[ReworkField(Initializer = nameof(CrearHistorial), Serialize = true)]
public static extern ref List<string> Rework_Historial(this Pawn p);
```

- **Supported types:** primitives, `string`, enums, `List<primitive/string/enum>`,
  `Dictionary<K,V>` and `HashSet<T>` (of primitives/string/enums). **NOT:** arrays and
  object collections.
- **Mechanics:** primitive/string/enum → `Scribe_Values.Look`; `List`/`Dictionary`/`HashSet`
  → `Scribe_Collections.Look`. Label = accessor name, with `forceSave` and default.
- **Only the mod that CREATES the field** decides its serialization.
- Unsupported type → clear error and no serialization (does not break the save).
- Missing fields in old saves are re-created on load by `Initializer`/`Default`.

---

## Advanced infrastructure

- **Hot-reload (`ReworkLive`):** update mod `.dll` files at runtime without restarting the
  game. Implement `IReworkLiveReloadable` (`OnBeforeLiveReload` / `OnAfterLiveReload`) or
  subscribe to `ReworkLive.BeforeReload` / `ReworkLive.AfterReload`.
- **Binary saves (`ReworkBinaryScribe`):** high-speed `.rwbin` saves with atomic `#REFORJED`
  sentinel; on load it skips XML parsing (loads in ~1s). If the binary is missing/corrupt it
  falls back to the classic `.rws`. A single periodic XML backup is kept
  (`GameComponent_ReworkXmlBackup`, interval configurable 5–120 min, 15 min default).
- **Colony progression (`ReworkColonySkill`):** settlement-level skills with XP/levels,
  persisted in `.rwbin`.
- **Pawn timeline (`ReworkPawnTimeline`):** persistent milestone history per colonist in `.rwbin`.
- **Dynamic mutations (`ReworkDynamicMutate`):** hot, reversible Def mutations with history.
- **Relations (`ReworkRelation`):** custom pawn relationships without touching vanilla Defs.
- **World key-value store (`ReworkWorldStore`):** arbitrary per-world storage persisted in the
  binary save.
- **State snapshots (`ReworkStateSnapshot`):** capture and analyze compact snapshots of
  `[ReworkField]` values.
- **Overlays (`ReworkOverlay`):** direct rendering of layers and semi-transparencies over the
  map.
- **Migrations (`[ReworkMigration]`):** safe data/field migration between mod versions.
- **Dependency graph (`ReworkDepGraph` / `[ReworkRequires]`):** automatic load-order
  resolution between ecosystem mods.
- **Conditional compatibility (`[ReworkCompatWith]` / `ReworkCompat`):** decoupled callbacks
  based on active mods.
- **Parallelism (`ReworkParallel`):** safe background computation with return to the main
  thread.
- **Cache (`ReworkCache`):** high-performance TTL cache layer in ticks for heavy loops.
- **Logging (`Rework.log`):** all Rework activity is mirrored to a dedicated file in LocalLow
  next to `Player.log`, UTF-8 with BOM. Dump exceptions with context via
  `Rework.Core.ErrorPrinter.Print("context", e)` / `Try("context", () => ...)`.

---

## Configuration

Persistent configuration is available from the RimWorld options menu
(**Options > Mod settings > Rework Reforjed**) and via code through `0ReworkAPI`
(`ReworkConfig`):

- **Performance profiles (`PerfProfile`):** `Equilibrado` (balanced, multithread on),
  `MaximoRendimiento` (multithread, verbose logs off), `AhorroMemoria` (single-thread).
- **Compatibility profiles (`CompatProfile`):** `Estandar` (proactive safe mode on),
  `Estricto` (maximum safety checks), `Permisivo` (safe mode off, experimental).
- **Multithread toggle:** controls `ReworkJobs.Pool`; off → 0 workers (inline single-thread fallback).
- **Auto-serialize backend:** toggles automatic Scribe injection for
  `[ReworkField(Serialize=true)]`.
- **Proactive safe mode:** toggle the guard that prevents rewriting if the native layout
  check fails.
- **Verbose logs:** enable detailed diagnostic traces in `Rework.log`.
- **Per-mod exclusion (`ReworkConfig.ExcludedMods`):** exclude specific mods from the patch
  pipeline.
- **Save/Load & Reset:** automatic XML persistence via RimWorld `ModSettings`; `Reset()`
  restores defaults.

---

## Diagnostics & DevTools

Open the floating **Runtime Inspector** (`Dialog_ReworkInspector`) from
**Options > Mod settings > Rework Reforjed > 🔍 Open Rework DevTools & Inspector**:

- Live class explorer by assembly with search filter, field/method counts.
- Injected-field inspector.
- Patched methods and hooks viewer.
- Save explorer (`.rws` / `.rwbin`) with size/date analysis; integrity check of the
  `#REFORJED` sentinel.
- Own developer console with history (`help`, `gc`, `clearbus`, `reloadlive`, `diag`, `fps`).
- Integrated profiler (GC RAM in MB, frametime in ms, FPS estimate).
- Thread viewer (`Rework.Threading` workers, core count, execution mode).
- Jobs/content counter (declarative Defs and applied mutations).
- Cache viewer with hot purge of `GenTypes.ClearCache()` and `ReworkBus`.

---

## Cross-mod ordering

- **Fields:** the first accessor with a name creates the field (and its initializer); later
  ones with the same name+type **link to it** (shared field); same name with another type →
  **conflict** (accessor rewritten to throw). The first mod in load order wins.
- **Patches:** order = Priority desc → mod load order → name (stable). Transpilers over the
  same method compose in that order.

## Duplicate assembly names

If two mods ship an assembly with the same name, Rework accepts both (the first is the main
one for the resolver; the rest are processed and reloaded from their own paths). Best
practice: keep assembly names unique and reference the API with `<Private>false</Private>`
(never re-bundle `0ReworkAPI.dll`/`Mono.Cecil.dll`).

---

## Compiling from source

```powershell
dotnet build "Source\Rework.slnx" -c Release
```

Expected: "0 Errors" (pre-existing harmless warnings). Output goes to `..\..\Assemblies\`.

---

## Feature list

- Structural injection: `[ReworkField]`, `[ReworkMethod]`, `[ReworkProperty]`,
  `[ReworkAnnotate]`.
- Patching: `[ReworkPatch]` (direct Cecil + transpilers), `TranspilerHelpers`,
  `[ReworkInit]`, `[ReworkHook]` (21 lifecycle points).
- Runtime: `ReworkBus` / `[ReworkOn]`, `[ReworkWatch]`, `[ReworkUIPanel]`,
  `[ReworkGenStep]`, `[ReworkMutate]`, `ReworkLive`, `ReworkDynamicMutate`,
  `ReworkBinaryScribe`, `ReworkColonySkill`, `ReworkPawnTimeline`, `ReworkRelation`,
  `ReworkWorldStore`, `ReworkStateSnapshot`, `ReworkOverlay`, `[ReworkGizmo]`,
  `[ReworkAlert]`, `[ReworkSchedule]`, `[ReworkStatusEffect]`, `[ReworkAIModifier]`,
  `[ReworkZoneEffect]`, `[ReworkMigration]`, `ReworkDepGraph`, `[ReworkCompatWith]`,
  `ReworkParallel`, `ReworkCache`, `[ReworkQuest]`, `[ReworkLore]`.
- Zero-XML content: `[ReworkRecipe]`, `[ReworkIncident]`, `[ReworkJob]`,
  `[ReworkWorkGiver]`, `[ReworkGene]`, `[ReworkHediff]`, `[ReworkTrait]`,
  `[ReworkResearch]`, `[ReworkRaid]`, `[ReworkThought]`, `[ReworkApparel]`,
  `[ReworkNeed]`, `[ReworkTab]`, `[ReworkInspectString]`, `[ReworkDefBuilder]`.
- Diagnostics: `Dialog_ReworkInspector` runtime suite.
- Compatibility: passive Harmony isolation and native environment shielding.