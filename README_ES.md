<p align="center">
  <img src="About/Preview.png" alt="Rework Reforjed" width="350"/>
</p>

<h1 align="center">Rework Reforjed</h1>

<p align="center">
  Framework de parcheo en memoria para RimWorld 1.6. Reescribe <code>Assembly-CSharp.dll</code>
  con Mono.Cecil antes de que el juego cargue, así los mods pueden añadir campos, métodos
  y contenido reales a clases existentes sin tocar ningún archivo en disco.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/RimWorld-1.6.4850_rev646-blue?style=for-the-badge" alt="Versión de RimWorld"/>
  <img src="https://img.shields.io/badge/Sin_Trampolines_Runtime-success?style=for-the-badge" alt="Sin trampolines en runtime"/>
  <img src="https://img.shields.io/badge/Mono.Cecil-In_Memory-orange?style=for-the-badge" alt="Mono.Cecil"/>
</p>

---

## Qué hace

Los mods de RimWorld suelen funcionar o con Defs XML o interceptando llamadas a métodos
en runtime (detours de Harmony). Rework va por otro camino: durante la carga inicial
reescribe el ensamblado del juego en memoria y reinicia la secuencia de arranque con la
versión reescrita. El código parcheado queda siendo simplemente código — sin trampolines,
sin coste por llamada, y con las DLLs del juego en disco intactas.

Los mods construidos sobre Rework declaran lo que quieren con atributos normales de C#:

- **Inyección estructural:** `[ReworkField]` (campos reales en clases del juego, con
  guardado/carga e inicializadores opcionales), `[ReworkMethod]` / `[ReworkProperty]`,
  `[ReworkAnnotate]`.
- **Comportamiento:** `[ReworkHook]` (22 puntos de ciclo de vida), `[ReworkSchedule]`,
  `ReworkBus` y `[ReworkOn]` (bus de eventos), `[ReworkPatch]` (parches directos con
  Mono.Cecil y transpilers amigables).
- **Cirugía IL:** `[ReworkRedirect]` (reescribe todos los puntos de llamada de un método
  del juego hacia tu código), `[ReworkOverride]` (overrides virtuales reales en clases del
  juego), `[ReworkUnlock]` (miembros private/sealed/no-virtuales → public/heredable/virtual),
  `[ReworkInline]` (inline de getters triviales en sus puntos de llamada), `[ReworkConst]`
  (pliega lecturas de `static readonly` a literales).
- **Contenido sin XML:** trabajos, work givers, incidentes, recetas, necesidades, rasgos,
  genes, investigaciones, incursiones, pensamientos, prendas, misiones, alertas, gizmos,
  pestañas de inspección, overlays, paneles de UI, efectos de zona, pasos de generación
  de mapas.
- **Guardados:** `ReworkBinaryScribe` — guardados binarios comprimidos (`.rwbin`) con
  respaldo XML periódico, y persistencia automática de los almacenes de servicio del
  framework (`.rwdat`).
- **Herramientas:** `ReworkParallel` (hilos de trabajo con entrega segura al hilo
  principal), `ReworkCache`, hot-reload de DLLs externas y un inspector de runtime con
  consola de desarrollo (`Dialog_ReworkInspector`).

## Instalación

1. Copia la carpeta `Rework Reforjed` en `RimWorld\Mods\`.
2. Actívala en el menú de Mods y colócala **al principio del orden de carga**, justo
   después de Core y las expansiones.
3. El primer arranque reinicia la carga en el sitio; es normal y ocurre una sola vez.

Desinstalar es borrar la carpeta del mod. Las DLLs del juego nunca se tocan.

## Estructura del repositorio

```
Rework Reforjed/
├── About/                    Metadatos del mod
├── Assemblies/               DLLs compiladas, listas para jugar
│   ├── 0ReworkData.dll       Estado que sobrevive a la recarga in-situ
│   ├── 0ReworkAPI.dll        API pública que referencian los mods
│   ├── ReworkCore.dll        Motor Mono.Cecil, arranque y hooks
│   ├── Rework.dll            El mod en sí (ajustes, UI, inspector)
│   └── ReworkContent.dll     Extensiones de contenido declarativo
└── Source/                   Cinco proyectos; ver nota abajo
```

## Compilar desde el código fuente

Necesitas el [SDK de .NET](https://dotnet.microsoft.com/download) (compila contra
.NET Framework 4.7.2).

```powershell
dotnet build "Source\Rework.slnx" -c Release
dotnet build "Source\ReworkContent\ReworkContent.csproj" -c Release
```

Fíjate en el segundo comando: `ReworkContent` **no** forma parte de la solución y se
compila por separado. Ambos comandos escriben las DLLs directamente en `Assemblies\`.

## Documentación

- [MANUAL_ES.md](MANUAL_ES.md) (español) / [MANUAL.md](MANUAL.md) (inglés) — la guía
  completa de la API con ejemplos.
- [GETTING_STARTED_ES.md](GETTING_STARTED_ES.md) / [GETTING_STARTED.md](GETTING_STARTED.md) —
  un tutorial de diez minutos, de cero a un mod funcionando.
- [ERRORES.md](ERRORES.md) (español) / [ERRORS.md](ERRORS.md) (inglés) — reglas duras,
  trampas conocidas y la bitácora de incidentes.

## Créditos

Desarrollado por **DGZ** como parte de la serie Reforjed. Diseñado y verificado contra
RimWorld 1.6.4850 rev646.
