<p align="center">
  <img src="About/Preview.png" alt="Rework Reforjed Logo" width="350"/>
</p>

<h1 align="center">Rework Reforjed</h1>

<p align="center">
  <strong>The definitive pre-load patching framework for RimWorld — a complete replacement for Harmony.</strong><br>
  Rewrites <code>Assembly-CSharp.dll</code> in memory using Mono.Cecil before the game engine starts, without touching any DLL on disk.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/RimWorld-1.6.4850_rev646-blue?style=for-the-badge" alt="RimWorld Version"/>
  <img src="https://img.shields.io/badge/Harmony-0%25_Dependency-success?style=for-the-badge" alt="Zero Harmony"/>
  <img src="https://img.shields.io/badge/Mono.Cecil-In_Memory-orange?style=for-the-badge" alt="Mono.Cecil"/>
</p>

---

## ⚡ What is Rework Reforjed?

**Rework Reforjed** is a unique in-memory injection architecture for RimWorld. Unlike traditional dynamic patching libraries (like Harmony) that intercept methods with machine-code trampolines at runtime, Rework performs a **low-level in-memory rewrite** during the initial load phase:

- ❌ **No Harmony:** No JIT trampolines or slow runtime detours.
- ❌ **No disk changes:** Your original RimWorld DLLs stay completely untouched.
- ❌ **No mandatory XML (Zero-XML):** Create full content (recipes, incidents, jobs, needs, quests, etc.) purely from C# code.
- 🚀 **Native performance:** Modified code runs at pure CLR bytecode speed.

---

## 🌟 Main Features

### 1. Real Structural Injection
- `[ReworkField]`: Adds real fields to game classes (with automatic `Scribe` serialization and initializer support).
- `[ReworkMethod]` / `[ReworkProperty]`: Real forwarders exposed to the game and other mods.
- `[ReworkWatch]`: Automatic reactive callbacks when injected fields change.

### 2. Binary Save/Load (`ReworkBinaryScribe`)
- Saves and loads games in milliseconds using a compact Deflate binary format (`.rwbin`) with `#REFORJED` sentinel.
- Keeps periodic XML backup sync (`.rws`) for maximum save safety.

### 3. Complete Zero-XML Suite
- Create `ThingDef`, `NeedDef`, `HediffDef`, recipes, incidents, hot mutations, quests (`[ReworkQuest]`), colonist tabs (`[ReworkTab]`), alerts (`[ReworkAlert]`) and zone effects without a single XML file.

### 4. Performance & Concurrency
- `ReworkParallel`: Dispatch intensive simulations to worker threads with safe delivery back to the RimWorld main thread.
- `ReworkCache`: TTL-based cache layer in ticks to optimize heavy loops.

### 5. Integrated Diagnostics Suite
- Open the **Runtime Inspector** (`Dialog_ReworkInspector`) directly in-game: memory profiler, class explorer, thread telemetry, developer console and applied-patch viewer.

---

## 📖 Documentation

- See [`MANUAL.md`](MANUAL.md) for the complete API guide, usage examples and technical specifications.
- See [`ERRORES.md`](ERRORES.md) for known pitfalls, limitations and what you should NOT use or touch.

---

## 📜 License & Credits

Developed by **DGZ** as part of the **Reforjed** series.
Exclusive target: RimWorld 1.6.4850 rev646.