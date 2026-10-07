<p align="center">
  <img src="About/Preview.png" alt="Rework Reforjed Logo" width="350"/>
</p>

<h1 align="center">Rework Reforjed</h1>

<p align="center">
  <strong>El framework de parcheo pre-carga definitivo para RimWorld — un reemplazo completo de Harmony.</strong><br>
  Reescribe <code>Assembly-CSharp.dll</code> en memoria con Mono.Cecil antes de que el motor del juego arranque, sin tocar ningún DLL en disco.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/RimWorld-1.6.4850_rev646-blue?style=for-the-badge" alt="RimWorld Version"/>
  <img src="https://img.shields.io/badge/Harmony-0%25_Dependency-success?style=for-the-badge" alt="Zero Harmony"/>
  <img src="https://img.shields.io/badge/Mono.Cecil-In_Memory-orange?style=for-the-badge" alt="Mono.Cecil"/>
</p>

---

## ⚡ ¿Qué es Rework Reforjed?

**Rework Reforjed** es una arquitectura única de inyección en memoria para RimWorld. A diferencia de las librerías de parcheo dinámico tradicionales (como Harmony) que interceptan métodos con trampolines de código máquina en runtime, Rework realiza una **reescritura de bajo nivel en memoria** durante la fase inicial de carga:

- ❌ **Sin Harmony:** Sin trampolines JIT ni desvíos lentos en runtime.
- ❌ **Sin cambios en disco:** Tus DLL originales de RimWorld quedan completamente intactos.
- ❌ **Sin XML obligatorio (Zero-XML):** Crea contenido completo (recetas, incidentes, trabajos, necesidades, misiones, etc.) puramente desde código C#.
- 🚀 **Rendimiento nativo:** El código modificado corre a velocidad de bytecode puro de CLR.

---

## 📥 Instalación

1. Copia la carpeta `Rework Reforjed` a `RimWorld\Mods\`.
2. Actívala en el menú **Mods** de RimWorld.
3. **Orden de carga:** arrastra `Rework Reforjed` **hasta arriba del todo**, justo debajo de `Core`/expansiones.
4. La primera apertura reinicia la carga en el sitio automáticamente — el proceso del juego nunca se cierra.

Desinstalación: solo desactiva el mod en el menú Mods. Tus DLL del juego nunca se tocaron.

---

## 🌟 Características principales

### 1. Inyección estructural real
- `[ReworkField]`: Añade campos reales a clases del juego (con serialización automática `Scribe` y soporte de inicializadores).
- `[ReworkMethod]` / `[ReworkProperty]`: Forwarders reales expuestos al juego y a otros mods.
- `[ReworkWatch]`: Callbacks reactivos automáticos cuando cambian los campos inyectados.

### 2. Guardado/Carga binaria (`ReworkBinaryScribe`)
- Guarda y carga partidas en milisegundos con un formato binario Deflate compacto (`.rwbin`) con centinela `#REFORJED`.
- Mantiene sincronización periódica de respaldo XML (`.rws`) para máxima seguridad de tus partidas.

### 3. Suite Zero-XML completa
- Crea `ThingDef`, `NeedDef`, `HediffDef`, recetas, incidentes, mutaciones en caliente, misiones (`[ReworkQuest]`), pestañas de colonos (`[ReworkTab]`), alertas (`[ReworkAlert]`) y efectos de zona sin un solo archivo XML.

### 4. Rendimiento y concurrencia
- `ReworkParallel`: Despacha simulaciones intensivas a hilos workers con entrega segura de vuelta al hilo principal de RimWorld.
- `ReworkCache`: Capa de caché con TTL en ticks para optimizar bucles pesados.

### 5. Suite integrada de diagnóstico
- Abre el **Inspector de Runtime** (`Dialog_ReworkInspector`) directamente en el juego: perfilador de memoria, explorador de clases, telemetría de hilos, consola de desarrollo y visor de parches aplicados.

---

## 🗂️ Estructura del repositorio

```
Rework Reforjed/
├── About/                    Metadatos del mod (About.xml, imagen de preview)
├── Assemblies/               DLL compilados listos para jugar
│   ├── 0ReworkData.dll       Almacén de datos persistente (sobrevive al reload en el sitio)
│   ├── 0ReworkAPI.dll        Superficie pública de API para modders externos
│   ├── ReworkCore.dll        Motor de reescritura Mono.Cecil y orquestador del boot
│   └── Rework.dll            Ensamblado del mod para RimWorld (settings, UI, devtools)
└── Source/                   Solución con los 4 proyectos (ReworkData, ReworkAPI, ReworkCore, ReworkMod)
```

---

## 🛠️ Compilar desde el código fuente (SI VAS A CREAR UN MOD PARA ESTE FRAMEWORK)

Requisitos: [.NET SDK 8.0+](https://dotnet.microsoft.com/download)

```powershell
dotnet build "Source\Rework.slnx" -c Release
```

Las librerías compiladas se generan automáticamente en `Assemblies/`.

---

## 📖 Documentación

- Consulta [`MANUAL.md`](MANUAL.md) (inglés) / [`MANUAL_ES.md`](MANUAL_ES.md) (español) para la guía completa de la API, ejemplos de uso y especificaciones técnicas.
- Consulta [`ERRORS.md`](ERRORS.md) (inglés) / [`ERRORES.md`](ERRORES.md) (español) para trampas conocidas, limitaciones y lo que NO debes usar o tocar.

---

## 📜 Licencia y créditos

Desarrollado por **DGZ** como parte de la serie **Reforjed**.
Target exclusivo: RimWorld 1.6.4850 rev646.
