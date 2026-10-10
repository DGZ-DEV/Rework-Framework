# Rework Reforjed — ERRORS

What you must **NOT** use, **NOT** touch and **NOT** do. These are the hard rules of the
framework: every item here caused a real crash, a corrupt save, or a broken boot. Read this
before shipping any mod based on Rework.

---

## 1. Rework does not use Harmony (and does not integrate with it)

Rework rewrites the game assembly **in memory, before it is loaded**; it does not intercept
methods at runtime. They are two different models acting at different times, so a `[HarmonyPatch]`
is neither processed by Rework nor combined with its patches: do not expect them to interoperate.

- If a mod brings that kind of library (`0Harmony.dll`, `HarmonySharedState`), Rework marks it as
  a **read-only protected assembly** — the same treatment as `mscorlib` or `UnityEngine`: it is not
  rewritten, its attributes are not processed and it is not reloaded. That is a **defensive**
  measure so the game still boots when they are present, not an integration.

## 2. NEVER touch game DLLs on disk

The original RimWorld DLLs are **never modified**. Everything happens in memory with
Mono.Cecil. Do not write a launcher, a swap system or a manual step that patches
`Assembly-CSharp.dll` on disk — this was tried and discarded because it required the game to
be closed, broke portability, and generated distrust.

## 3. Do NOT add a mod interface to a game class

Adding an interface from your mod (which gets reloaded) to a game class
(e.g. `Verse.Pawn : IReworkContadorLogros`) breaks the class **vtable** at startup:

- `TypeLoadException: Could not load type 'Verse.Pawn[]'...`
- `TypeLoadException: VTable setup of type Verse.Pawn failed`

**This is structural, not a bug to work around.** Safe alternative: create a **new subclass**
that inherits from the game class (the `ReworkWorldCameraDriver` pattern) and substitute its
use — never change the existing class.

## 4. Do NOT change a game class base type

Changing the base class of a game type corrupts the vtable/layout and fails at startup. The
same safe alternative applies: subclass, don't modify.

## 5. Do NOT add fields inside structs

Adding a field **inside an existing struct** — or changing its size — breaks the memory
layout and value-copy semantics. Fields can only be added to **classes** (reference types).
A struct used as a field TYPE on a class does work (e.g. `Verse.IntVec3` on `Verse.Pawn`).

## 6. Do NOT reference declarative events/ctors that don't exist

Only these declarative additions are supported: methods, properties, attributes. **Events
and constructors have no declarative API.** If you need them, write pure Cecil via a
`[ReworkPatch]` — and only if you really understand the target method. Unsupported additions
are skipped with a report; they never silently half-apply.

## 7. Do NOT serialize arrays or object collections automatically

`[ReworkField(Serialize = true)]` supports: primitives, `string`, enums,
`List<primitive/string/enum>`, `Dictionary<K,V>` and `HashSet<T>` (of primitives/string/
enums). It does **NOT** support:

- arrays (`T[]`)
- collections of objects (references / Deep)
- other generics (`Stack`, `Queue`, …)

An unsupported type still works at runtime but does **not** travel in the save — Rework logs
a clear error and never breaks the saved game. Use a `List` or manual Scribe for those cases.

## 8. Do NOT delete exception-handler boundaries in a transpiler

The transpiler mode reuses the same MethodBody and **preserves exception handlers**. If you
delete the boundary instructions of an `try/catch` region, that handler is discarded with a
warning. Worst case this corrupts the target method and gives a black boot. Keep EH regions
intact; only touch the instructions you need.

## 9. IL helpers: do NOT use numeric `Starg`/`Ldarg` operands

`Starg`/`Ldarg` with a numeric `(byte)` operand throws
`ArgumentException: opcode` — the opcode expects a `ParameterReference`, not an int.

- Use `Starg(ParameterDefinition)` / `Ldarg` with the real `ParameterDefinition`
  (e.g. `targetMethod.Parameters[i-1]`).
- Short forms `Ldarg_0` … `Ldarg_3` are safe.

## 10. Do NOT rely on `InsertBefore` ordering blindly

When injecting a context-taking hook, the correct IL is `ldarg.0; call hook`. If you insert
`call` and then `ldarg.0`, you get `call; ldarg.0` → invalid stack →
`System.InvalidProgramException: Invalid IL code ... IL_0000: call` at the moment the method
runs (the boot will NOT detect it — only the log line when it executes). Insert `ldarg.0`
FIRST, then `call`. This is handled automatically by the framework; do not re-insert
manually.

## 11. Do NOT create duplicate assembly names

A mod assembly with the same simple name as another causes vanilla to deduplicate to a
single runtime identity; processing both against the same module duplicates effects and is
chaotic from the root. Rules:

- Keep assembly names **unique**.
- Never re-bundle `0ReworkAPI.dll` or `Mono.Cecil.dll` — reference the API with
  `<Private>false</Private>` / `ExcludeAssets="runtime"` so it resolves against Rework's copy
  (same version → same identity).

## 12. Do NOT register Defs during Pass 1 or early mod init

`DefDatabase` registration (Jobs, Incidents, WorkGivers, …) cannot happen during the first
pass or in early `LoadedModManager.InitializeMods` — vanilla Defs are not loaded yet. Always
use `[StaticConstructorOnStartup]` (the framework registries do this for you). Doing it early
silently produces empty/broken Defs.

## 13. Do NOT make hook bodies heavy (tick hooks fire 60/s)

`MapTick`, `PawnTick`, `GameComponentTick`, `MapComponentTick`, `WorldComponentTick` and
`StorytellerTick` fire 60 times per second. Keep the body extremely cheap or use a counter /
interval, or you will tank performance.

## 14. Do NOT assume `this` is the hook context

Some hooks pass a different subject:

- `CurrentMapChanged` → the NEW map (setter parameter).
- `BabyBorn` → the biological MOTHER (parameter 5; the baby is not yet a Pawn).
- `CaravanEnteredMap` → the destination `Verse.Map` (parameter 2).
- `PawnLeftToCaravan` → the leaving pawn (parameter 1).

The framework validates that your parameter type matches; if not, the hook is skipped with a
report.

## 15. Do NOT treat "clean boot" as proof the logic runs

A clean boot (0 NREs, `VERIFICACIÓN OK`) proves the rewrite applied, **not** that the logic
fires correctly. IL/patching errors that only appear when a method executes (like the
`InvalidProgramException` in §10) do not show at boot. Always validate behavior in a real
game session: save/load, kill a pawn, spawn a caravan, etc.

## 16. Do NOT use Defs inside `[ReworkInit]`

`[ReworkInit]` runs in the mod-loading phase, **before Defs are loaded**. Do not touch
`SkillDefOf`/`ThingDefOf`/etc. there. If you need Defs, combine with vanilla
`[StaticConstructorOnStartup]`.

## 17. Do NOT disable safety and ignore the log

- **Proactive safe mode:** if the environment check fails (runtime not Mono or native layout
  unexpected), Rework rewrites nothing and starts vanilla with a warning. Do not "fix" this
  by forcing a rewrite — it protects against corrupted memory.
- **`Rework.log`:** read it. Every trap below has a clear message there. Errors such as
  "No se puede serializar automáticamente" or "campo no soportado" are benign by design; do
  not silence or suppress them without reading the reason.
- **`ReworkConfig` exclusion:** excluded mods are not processed at all. If a mod "silently
  does nothing", check it is not in `ExcludedMods`.

## 18. Do NOT share a field name across mods with different types

The first accessor with a name creates the field. Later mods with the same name+type **link
to the shared field**; same name + different type → real conflict (the accessor is rewritten
to throw `InvalidOperationException`). You cannot "win" an override — the first mod in load
order owns the field.

## 19. Do NOT modify `0ReworkData.dll` / `0ReworkAPI.dll` identity

`0ReworkData` survives the reload barrier (persistent DataStore) and `0ReworkAPI` is the
public surface for modders. They are loaded before ReworkCore and must keep their identities
and paths stable; re-signing, renaming or re-bundling them breaks the reload and the
cross-mod API.

## 22. Do NOT assume a declared API activates itself ("ghosts")

The fact that a system exists in `0ReworkAPI` (registry, event, persistent store) does **not**
mean anything ever fires it. 13 "ghosts" were audited: features with registration but **zero
callers** — they existed in the API, some even serialized, but never ran (the overlay system
had its dispatch injected into the game while no attribute existed to fill the registry:
permanent `Count == 0` → early-out → nothing ever drew).

- **Rule:** every API needs a *real consumer* in the runtime: a hook, a schedule, an init or
  a pipeline point that invokes it. If you add one, wire it up in the same commit.
- **Quick check:** search the source for callers of the registration method. If the only hit
  is the definition itself, it is a ghost.
- **Log verification:** the `[ReworkAttributeScanner] Escaneo completo: … Overlay=N` line
  shows the counters; a counter stuck at `0` when you expected active content is the signal.
- **Utility** APIs designed for external mods to call (cache, profiler, dialogs, job pools,
  world store) are not ghosts: the modder is their consumer, and they are documented in the
  manual.

---

## 20. Do NOT use `MethodInfo.Invoke` on a hot path — cache the delegate

`MethodInfo.Invoke` resolves by reflection and **allocates an `object[]` per call**. On a tick that
fires 60/s (or per pawn/tick) that is GC and CPU garbage measured in accumulated milliseconds. The
framework already avoids it: `ReworkBus.Subscribe<T>`, `ReworkBus.Register` (via
`Delegate.CreateDelegate`), `ReworkScheduler.Register` and `ReworkWatchRegistry` store a compiled
`Delegate` resolved **once** at registration.

If you write your own registry, do the same:

```csharp
// Good: delegate cached at registration
Action<object> handler = /* ... */;
// Bad: resolve and invoke every tick
methodInfo.Invoke(null, new object[] { arg });
```

`Delegate.CreateDelegate` returns `null` when the signature does not fit (e.g. a method with
parameters); in that case the framework falls back to `MethodInfo` — it never crashes.

## 21. Do NOT assume "registered" = "usable" (companion defs)

A Def added to `DefDatabase` at runtime **exists**, but it may not be *usable*: vanilla creates
implied/companion Defs during XML load, and those are not reproduced when you add a Def by hand.
The canonical case (§41): a visible `WorkTypeDef` without its `PawnColumnDef` shows up in the logic
but **has no column in the Work tab** — the player cannot see or tune it.

- **Rule:** after adding a Def at runtime, call
  `ReworkCompanionDefRegistry.GenerateFor(def)` (register the generator with
  `ReworkCompanionDefGenerator.EnsureRegistered()` at startup). The framework then creates the
  `PawnColumnDef` `WorkPriority_<defName>` and inserts it into `PawnTableDefOf.Work.columns`.
- **Verification:** do not trust "registered = 1". `ReworkContentSelfCheck` (§55) checks
  *registered **and** usable* and writes a report; usability failures come out as an error with the
  concrete reason.
- Need/Gene/Research have **no** companion defs in vanilla (they are discovered through
  `DefDatabase<T>.AllDefs`): the generator is generic and extensible in case a future type needs
  them.

## 23. Do NOT leave a declarative API half-wired: "registered ≠ applied ≠ obtainable"

Adding a Def to `DefDatabase` is only the first link. The framework guarantees that one; the
**application** or the **obtainment** may be yours. In the §48 audit, `[ReworkHediff]`,
`[ReworkThought]` and `[ReworkApparel]` registered correctly and still **never showed up in game**
because of this:

- `[ReworkHediff]` → needs something to apply it: a recipe with `addsHediff`, your code
  (`pawn.health.AddHediff`) or a `[ReworkStatusEffect]` with the same defName.
- `[ReworkThought]` → needs something to grant it (`TryGainMemory`, a ThoughtWorker). Also, the
  decorated class is **not** wired to the Def: the thought uses `ThoughtClass` (default
  `Thought_Memory`); pass it if you want your logic.
- `[ReworkApparel]` → needs `ThingCategoryDef`, a recipe, `costList` or trader stock, or it never
  appears in normal play. It is also created without `statBases`, and `TexPath` needs real art.
- `[ReworkJob]` → needs an emitter: `[ReworkWorkGiver]`, a think tree or `TryTakeOrderedJob`.
- `[ReworkRecipe]` → created without `products`: it is the "worker total" pattern (surgery). Add
  them with `[ReworkMutate]` or code if you want to craft items.

The ones vanilla consumes on its own (Trait, Need, Gene, Research, Raid, Incident, Designator,
GenStep, Mutate) do not have this problem. Details in the manual.

## 24. Do NOT leave a failure silent (empty catch, `return false` without a log, flag never reset)

The §48 "ghosts" were not only features with no caller: they were **silent failures**. A bus
handler blowing up inside a `catch { }`, an `Apply` returning `false` without saying the HediffDef
was missing, a global alert flag that was never reset (the second game of the session had no
alerts), a `GenStepDef` linked to no `MapGeneratorDef` that would never run. None of them raised an
error: they simply "did not work".

- Every failure path writes a line (`Lg.Error`) with the concrete reason.
- No empty `catch { }` in framework code: the error goes to a log hook.
- If a registry can sit at `0`, publish the counter (`Escaneo completo: …`) so the zero is visible.

## 25. Do NOT add a subclass that vanilla instantiates by reflection without a public parameterless ctor

Vanilla creates objects by reflection with `Activator.CreateInstance(type)`. The case that cost a
freeze (§39): `RimWorld.AlertsReadout..ctor()` probes **every** non-abstract subclass of
`RimWorld.Alert` and instantiates them, so an alert without a public empty constructor throws
`MissingMethodException`, `UIRoot_Play` is not built, `Find.MapUI` stays null and generating the map
blows up (the game looks frozen). The same applies to `GameComponent`/`MapComponent` and any type
vanilla walks by reflection.

- If you inherit from a type vanilla instantiates by reflection, give it a **public parameterless
  ctor**, even an empty one: that is what `Game.FillComponents` and the alert readout use.

## 26. Do NOT fix a bug only in the binary (the `.cs` wins)

A patch applied only to the DLL is lost on the next `dotnet build`. It already happened (§39.1): a
fix for the map-generation freeze lived only in the binary and vanished when the project was
rebuilt. Every fix goes into the source, compiles with **0 errors** and is then deployed: the
binary is a product, not the source of truth.

## 27. Contributing to the engine: do NOT import references eagerly or touch assemblies with no real sites

The §47 boot loop came from here: the IL surgery processors called `module.ImportReference` on
**every** rewritable assembly, including those with no call site at all. That adds an
`AssemblyReference` to an assembly that does not need it, the reload propagation marks it as a
dependent, and it ends up in the swap — including `0ReworkData`, which must **never** be reloaded
(the barrier) — so the boot loops.

- Import a reference only when there is a real site that uses it (lazy import).
- Respect the barrier: `0ReworkData` is never swapped. The swap set is always logged
  (`Reload: intercambiando …`) and there is an anti-loop guard in case something marks it.

## Verified reference lines (normal, healthy boot)

These lines are EXPECTED in a healthy startup — not errors:

```
Entorno: Runtime=Mono, Mono=6.13.0 (Visual Studio built mono), layoutOK=True → seguro para reescribir
VERIFICACIÓN OK: Assembly-CSharp activa es la NUEVA (rewrite en efecto) y ReworkWorldCameraDriver enganchado.
Versión del juego soportada: 1.6.4850
```

And these are the classic **boot-breaking** ones to grep for and fix:

```
NullReferenceException … WorldCameraDriver / ExpandableWorldObjects
TypeLoadException: VTable setup of type Verse.Pawn failed
System.InvalidProgramException: Invalid IL code … IL_0000: call
ThreadAbortException: Thread was being aborted   (pass-1 transition noise, filtered by design)
```