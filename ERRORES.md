# Rework Reforjed — ERRORES

What you must **NOT** use, **NOT** touch and **NOT** do. These are the hard rules of the
framework: every item here caused a real crash, a corrupt save, or a broken boot. Read this
before shipping any mod based on Rework.

---

## 1. NEVER use Harmony

Rework is a **complete replacement for Harmony**. Never use Harmony to patch IL at runtime,
never use machine-code trampolines, and never wrap game methods with Harmony prefixes or
postfixes alongside Rework patches.

- If a mod brings Harmony assemblies (`0Harmony.dll`, `HarmonySharedState`), Rework detects
  them and treats them as read-only protected assemblies (no patches, no attribute
  processing) to avoid runtime collisions. Coexistence is passive: do not expect them to
  interoperate.

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

## 6. Do NOT reference `[ReworkInterface]` / `[ReworkMethod]`-style events/ctors you don't need

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

---

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