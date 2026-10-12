# Getting started with Rework

From zero to your first working mod in ~10 minutes. This guide uses only APIs **verified in
a real playthrough**. When you finish it, the [full manual](MANUAL.md) covers the remaining
~20 attributes and systems.

---

## 1. Prerequisites

- RimWorld 1.6 with the **Rework Reforjed** mod enabled (players load it before your mod).
- .NET SDK (any recent `dotnet`) to compile against **.NET Framework 4.7.2**.
- RimWorld compile-time references: the `Krafs.Rimworld.Ref` NuGet package.

## 2. Minimal mod structure

```
MyMod/
├── About/
│   └── About.xml
└── Assemblies/
    └── MyMod.dll        ← your build output
```

Minimal `About.xml` (declares the dependency so Rework loads first and the game warns if
it is missing):

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>MyMod</name>
  <author>your name</author>
  <packageId>yourname.mymod</packageId>
  <supportedVersions><li>1.6</li></supportedVersions>
  <modDependencies>
    <li>
      <packageId>dgz.rework</packageId>
      <displayName>Rework Reforjed</displayName>
      <steamWorkshopUrl></steamWorkshopUrl>
    </li>
  </modDependencies>
</ModMetaData>
```

> **Rework detects your mod through the `0ReworkAPI` reference**, not through `About.xml`.
> The XML only orders loading and shows the dependency to the player.

## 3. The minimal `.csproj`

Copy this to `Source/MyMod.csproj` and adjust the reference path to your install:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>MyMod</AssemblyName>
    <OutputPath>..\Assemblies\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- RimWorld compile-time references (not copied to the output). -->
    <PackageReference Include="Krafs.Rimworld.Ref" Version="1.6.4633" />

    <!-- THE reference that turns your mod into a "Rework mod". -->
    <Reference Include="0ReworkAPI">
      <HintPath>..\..\_Path\to\Rework Reforjed\Assemblies\0ReworkAPI.dll</HintPath>
      <Private>false</Private>  <!-- Important! Never re-bundle this DLL (ERRORS §11/§19). -->
    </Reference>
  </ItemGroup>
</Project>
```

Build from `Source/`:

```
dotnet build -c Release
```

Enable both mods (Rework first), launch, and check `Player.log`: the line
`[ReworkAttributeScanner] Escaneo completo: …` must show up.

## 4. Your first mod (hello world)

```csharp
using Rework;
using Verse;

namespace MyMod;

public static class Startup
{
    // Runs while mods load, with configurable priority.
    // Do NOT touch Defs here (they are not loaded yet — ERRORS §16).
    [ReworkInit(Priority = 0)]
    public static void Hello()
        => Log.Message("MyMod booted with Rework.");
}
```

That alone is a working Rework mod. Now something the player can actually **see**.

## 5. Three quick hits

### 5.1 Add a persistent field to pawns — `[ReworkField]`

A real new field on `Verse.Pawn`, saved/loaded with the game **without writing any Scribe**:

```csharp
using System.Collections.Generic;
using Rework;

namespace MyMod;

public static class Fields
{
    // Every pawn is born with a fresh list; it travels in the save.
    [ReworkField(Initializer = nameof(Create), Serialize = true)]
    public static extern ref List<string> MyMod_Notes(this Verse.Pawn pawn);

    private static List<string> Create() => new List<string>();
}
```

From anywhere: `pawn.MyMod_Notes().Add("met a thrumbo");`

### 5.2 A button when selecting a pawn — `[ReworkGizmo]`

```csharp
using Rework;
using RimWorld;
using Verse;

namespace MyMod;

public static class NoteButton
{
    // Shows up in the command bar whenever any Pawn is selected.
    [ReworkGizmo("Add note", TargetType = "Pawn",
        Description = "Stores a note in the pawn's injected field.")]
    public static void OnClick(Verse.Pawn pawn)
    {
        if (pawn == null) return;
        pawn.MyMod_Notes().Add($"note #{pawn.MyMod_Notes().Count + 1}");
        Messages.Message($"{pawn.LabelShort}'s notes: {pawn.MyMod_Notes().Count}",
            MessageTypeDefOf.PositiveEvent, false);
    }
}
```

### 5.3 React to a game event — `[ReworkHook]`

```csharp
using Rework;
using Verse;

namespace MyMod;

public static class Events
{
    // Injected straight into the game method (no runtime patching).
    // With 1 parameter you receive the event subject: here, the dying Pawn.
    [ReworkHook(ReworkHookPoint.PawnDied)]
    public static void OnDeath(Verse.Pawn pawn)
    {
        if (pawn == null) return;
        pawn.MyMod_Notes().Add("died");
    }
}
```

## 6. Typical first-day mistakes

These come from the project's real incident log ([ERRORS.md](ERRORS.md)):

1. **`Field not found` when using the field** → you compiled without Rework processing the
   accessor. Usual cause: the `0ReworkAPI` reference has `<Private>true</Private>` (it must be
   `false`!), or your assembly name collides with another mod's (§11/§19).
2. **Nothing runs at startup** → check `Player.log`: if your mod is missing from the scan
   line, Rework never saw the `0ReworkAPI` reference.
3. **Broken Defs when used inside `[ReworkInit]`** → Defs do not exist at that stage yet
   (§16). Combine with vanilla `[StaticConstructorOnStartup]`.
4. **Adding a field to a struct or an interface to a game class** → not possible: it breaks
   the vtable (§3/§5). Add fields only to classes (`Pawn`, `MapComponent`, …).
5. **A hook that never fires** → check the parameter type: some events pass a different
   subject (`BabyBorn` receives the mother, not the baby — §14).

## 7. Next steps

- **Full manual:** [MANUAL.md](MANUAL.md) — declarative jobs, alerts, overlays, inspect tabs,
  migrations, hot-reload, Cecil transpilers, binary serialization…
- **Hard rules:** [ERRORS.md](ERRORS.md) — what you must never do, and why.
- **Boot regression:** `Tools/BootRegression.ps1` — verifies a healthy boot from the log.
- **Full example:** the framework exercises its own API — see
  `Source/ReworkMod/ReworkILSelfDemo.cs` (a self-demo with zero gameplay impact) and
  the `ReworkRuntimeActivators.cs` schedulers/hooks.
