# Primeros pasos con Rework

De cero a tu primer mod funcionando en ~10 minutos. Esta guía usa solo APIs **verificadas en
partida real**. Cuando la termines, el [manual completo](MANUAL_ES.md) cubre los ~20
atributos y sistemas restantes.

---

## 1. Prerrequisitos

- RimWorld 1.6 con el mod **Rework Reforjed** activado (el jugador lo pone antes que tu mod).
- SDK de .NET (cualquier `dotnet` reciente) para compilar contra **.NET Framework 4.7.2**.
- Referencias de compilación de RimWorld: el paquete NuGet `Krafs.Rimworld.Ref`.

## 2. Estructura mínima del mod

```
MiMod/
├── About/
│   └── About.xml
└── Assemblies/
    └── MiMod.dll        ← la salida de tu build
```

`About.xml` mínimo (declara la dependencia para que Rework cargue antes y el juego avise si falta):

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>MiMod</name>
  <author>tu nombre</author>
  <packageId>tunombre.mimod</packageId>
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

> **Rework detecta tu mod por la referencia a `0ReworkAPI`**, no por el `About.xml`. El
> `About.xml` solo ordena la carga y muestra la dependencia al jugador.

## 3. El `.csproj` mínimo

Copia esto a `Source/MiMod.csproj` y ajusta la ruta de la referencia a tu instalación:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>MiMod</AssemblyName>
    <OutputPath>..\Assemblies\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- Referencias de compilación de RimWorld (no se copian al output). -->
    <PackageReference Include="Krafs.Rimworld.Ref" Version="1.6.4633" />

    <!-- LA referencia que convierte a tu mod en "mod Rework". -->
    <Reference Include="0ReworkAPI">
      <HintPath>..\..\_Ruta\a\Rework Reforjed\Assemblies\0ReworkAPI.dll</HintPath>
      <Private>false</Private>  <!-- ¡Importante! Nunca re-empaquetes la DLL (ERRORES §11/§19). -->
    </Reference>
  </ItemGroup>
</Project>
```

Compila desde `Source/`:

```
dotnet build -c Release
```

Activa ambos mods (Rework primero), arranca y mira `Player.log`: debe aparecer
`[ReworkAttributeScanner] Escaneo completo: …`.

## 4. Tu primer mod (hola mundo)

```csharp
using Rework;
using Verse;

namespace MiMod;

public static class Arranque
{
    // Se ejecuta al cargar los mods, con prioridad configurable.
    // NO uses Defs aquí dentro (aún no están cargados — ERRORES §16).
    [ReworkInit(Priority = 0)]
    public static void Hola()
        => Log.Message("MiMod arrancó con Rework.");
}
```

Con eso ya tienes un mod Rework funcional. Ahora, algo que el jugador pueda **ver**.

## 5. Tres hits rápidos

### 5.1 Añadir un campo persistente a los pawns — `[ReworkField]`

Un campo nuevo real en `Verse.Pawn`, guardado/cargado en la partida **sin escribir Scribe**:

```csharp
using System.Collections.Generic;
using Rework;

namespace MiMod;

public static class Campos
{
    // Cada pawn nace con una lista nueva; viaja en el save.
    [ReworkField(Initializer = nameof(Crear), Serialize = true)]
    public static extern ref List<string> MiMod_Notas(this Verse.Pawn pawn);

    private static List<string> Crear() => new List<string>();
}
```

Desde cualquier sitio: `pawn.MiMod_Notas().Add("conoció a un thrumbo");`

### 5.2 Un botón al seleccionar un pawn — `[ReworkGizmo]`

```csharp
using Rework;
using RimWorld;
using Verse;

namespace MiMod;

public static class BotonNota
{
    // Aparece en la barra de comandos al seleccionar cualquier Pawn.
    [ReworkGizmo("Añadir nota", TargetType = "Pawn",
        Description = "Guarda una nota en el campo inyectado del pawn.")]
    public static void AlPulsar(Verse.Pawn pawn)
    {
        if (pawn == null) return;
        pawn.MiMod_Notas().Add($"nota #{pawn.MiMod_Notas().Count + 1}");
        Messages.Message($"Notas de {pawn.LabelShort}: {pawn.MiMod_Notas().Count}",
            MessageTypeDefOf.PositiveEvent, false);
    }
}
```

### 5.3 Reaccionar a un evento del juego — `[ReworkHook]`

```csharp
using Rework;
using Verse;

namespace MiMod;

public static class Eventos
{
    // Se inyecta directamente en el método del juego (sin parches en runtime).
    // Con 1 parámetro recibes el sujeto del evento: aquí, el Pawn que muere.
    [ReworkHook(ReworkHookPoint.PawnDied)]
    public static void AlMorir(Verse.Pawn pawn)
    {
        if (pawn == null) return;
        pawn.MiMod_Notas().Add("murió");
    }
}
```

## 6. Errores típicos del primer día

Estos salen de la bitácora real del proyecto ([ERRORES.md](ERRORES.md)):

1. **`Field not found` al usar el campo** → compilaste sin que Rework procese el accessor.
   Causa típica: la referencia a `0ReworkAPI` con `<Private>true</Private>` (¡debe ser
   `false`!) o el ensamblado con un nombre duplicado de otro mod (§11/§19).
2. **Nada se ejecuta en el arranque** → revisa `Player.log`: si tu mod no aparece en el
   escaneo, Rework no vio la referencia a `0ReworkAPI`.
3. **Defs rotos al usarlos en `[ReworkInit]`** → los Defs aún no existen en esa fase (§16).
   Combínalo con `[StaticConstructorOnStartup]` de vanilla.
4. **Quieres añadir un campo a un struct o una interfaz a una clase del juego** → no se puede:
   rompe la vtable (§3/§5). Añade campos solo a clases (`Pawn`, `MapComponent`, …).
5. **Hook que no hace nada** → comprueba el tipo del parámetro: algunos eventos pasan otro
   sujeto (`BabyBorn` recibe a la madre, no al bebé — §14).

## 7. Siguiente paso

- **Manual completo:** [MANUAL_ES.md](MANUAL_ES.md) — jobs declarativos, alertas, overlays,
  pestañas de inspección, migraciones, hot-reload, transpilers Cecil, serialización binaria…
- **Reglas duras:** [ERRORES.md](ERRORES.md) — lo que nunca debes hacer y por qué.
- **Regresión de arranque:** `Tools/BootRegression.ps1` — verifica el boot sano desde el log.
- **Ejemplo grande:** el propio framework consume su API — mira
  `Source/ReworkMod/ReworkILSelfDemo.cs` (auto-demo con impacto cero en el juego) y los
  schedulers/hooks de `ReworkRuntimeActivators.cs`.
