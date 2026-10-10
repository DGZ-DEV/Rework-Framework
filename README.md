<p align="center">
  <img src="About/Preview.png" alt="Rework Reforjed" width="350"/>
</p>

<h1 align="center">Rework Reforjed</h1>

<p align="center">
  In-memory patching framework for RimWorld 1.6. It rewrites <code>Assembly-CSharp.dll</code>
  with Mono.Cecil before the game loads, so mods can add real fields, methods and content
  to existing classes without touching a single file on disk.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/RimWorld-1.6.4850_rev646-blue?style=for-the-badge" alt="RimWorld Version"/>
  <img src="https://img.shields.io/badge/No_Runtime_Trampolines-success?style=for-the-badge" alt="No runtime trampolines"/>
  <img src="https://img.shields.io/badge/Mono.Cecil-In_Memory-orange?style=for-the-badge" alt="Mono.Cecil"/>
</p>

---

## What it does

RimWorld mods usually work either through XML defs or by intercepting method calls at
runtime (Harmony detours). Rework takes a different route: during the initial load, it
rewrites the game assembly in memory and restarts the boot sequence with the rewritten
version. Patched code is then just code — no trampolines, no per-call interception
overhead, and the game's DLLs on disk never change.

Mods built against Rework declare what they want with plain C# attributes:

- **Structural injection:** `[ReworkField]` (real fields on game classes, with optional
  save/load and initializers), `[ReworkMethod]` / `[ReworkProperty]`, `[ReworkAnnotate]`.
- **Behavior:** `[ReworkHook]` (22 lifecycle points), `[ReworkSchedule]`,
  `ReworkBus` & `[ReworkOn]` (event bus), `[ReworkPatch]` (direct Mono.Cecil patches and
  friendly transpilers).
- **IL surgery:** `[ReworkRedirect]` (rewrite every call site of a game method to your
  code), `[ReworkOverride]` (real virtual overrides on game classes), `[ReworkUnlock]`
  (make private/sealed/non-virtual members public/unsealed/virtual), `[ReworkInline]`
  (inline trivial getters at the call sites), `[ReworkConst]` (fold `static readonly`
  reads into literals).
- **Content without XML:** jobs, work givers, incidents, recipes, needs, traits, genes,
  research, raids, thoughts, apparel, quests, alerts, gizmos, inspect tabs, overlays,
  UI panels, zone effects, map generation steps.
- **Saves:** `ReworkBinaryScribe` — compressed binary saves (`.rwbin`) with a periodic
  XML backup, plus automatic persistence for the framework's service stores (`.rwdat`).
- **Tools:** `ReworkParallel` (worker threads with safe main-thread delivery),
  `ReworkCache`, hot-reload of external mod DLLs, and a runtime inspector with a dev
  console (`Dialog_ReworkInspector`).

## Installation

1. Copy the `Rework Reforjed` folder into `RimWorld\Mods\`.
2. Enable it in the Mods menu and place it at the **top of the load order**, right
   below Core / expansions.
3. The first launch restarts the loading in place; that is normal and happens once.

Uninstalling is just deleting the mod folder. The game's DLLs were never touched.

## Repository layout

```
Rework Reforjed/
├── About/                    Mod metadata
├── Assemblies/               Compiled DLLs, ready to play
│   ├── 0ReworkData.dll       State that survives the in-place reload
│   ├── 0ReworkAPI.dll        Public API that mods reference
│   ├── ReworkCore.dll        Mono.Cecil engine, boot and hooks
│   ├── Rework.dll            The mod itself (settings, UI, inspector)
│   └── ReworkContent.dll     Declarative content extensions
└── Source/                   Five projects; see note below
```

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (compiles against .NET
Framework 4.7.2).

```powershell
dotnet build "Source\Rework.slnx" -c Release
dotnet build "Source\ReworkContent\ReworkContent.csproj" -c Release
```

Note the second command: `ReworkContent` is **not** part of the solution and must be
built on its own. Both commands write the DLLs directly into `Assemblies\`.

## Documentation

- [MANUAL.md](MANUAL.md) (English) / [MANUAL_ES.md](MANUAL_ES.md) (Spanish) — the full
  API guide with examples.
- [GETTING_STARTED.md](GETTING_STARTED.md) / [GETTING_STARTED_ES.md](GETTING_STARTED_ES.md) —
  a ten-minute tutorial from zero to a working mod.
- [ERRORS.md](ERRORS.md) (English) / [ERRORES.md](ERRORES.md) (Spanish) — hard rules,
  known pitfalls and the incident log.

## Credits

Developed by **DGZ** as part of the Reforjed series. Built and verified against
RimWorld 1.6.4850 rev646.
