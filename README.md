<p align="center">
  <img src="About/Preview.png" alt="Rework Reforjed Logo" width="350"/>
</p>

<h1 align="center">Rework Reforjed</h1>

<p align="center">
  <strong>El framework de parcheo pre-carga definitivo para RimWorld que reemplaza por completo a Harmony.</strong><br>
  Reescribe <code>Assembly-CSharp.dll</code> en memoria utilizando Mono.Cecil antes de que el motor del juego comience su ejecución.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/RimWorld-1.6.4850_rev646-blue?style=for-the-badge" alt="RimWorld Version"/>
  <img src="https://img.shields.io/badge/Harmony-0%25_Dependency-success?style=for-the-badge" alt="Zero Harmony"/>
  <img src="https://img.shields.io/badge/Mono.Cecil-In_Memory-orange?style=for-the-badge" alt="Mono.Cecil"/>
</p>

---

## ⚡ ¿Qué es Rework Reforjed?

**Rework Reforjed** es una solución de arquitectura e inyección en memoria sin precedentes para RimWorld. A diferencia de las librerías de parcheo dinámico tradicionales (como Harmony) que interceptan métodos mediante trampolines de código máquina en tiempo de ejecución, Rework realiza un **re-escaneo y reescritura de bajo nivel en memoria** durante la fase inicial de carga:

- ❌ **Sin Harmony:** No hay trampolines JIT ni desvíos lentos en runtime.
- ❌ **Sin modificar archivos en disco:** Tus DLLs originales de RimWorld permanecen completamente intactas.
- ❌ **Sin XML obligatorio (Zero-XML):** Crea contenido completo (recetas, incidentes, trabajos, necesidades, misiones, etc.) puramente desde código C#.
- 🚀 **Rendimiento NATIVO:** El código modificado se ejecuta a velocidad de bytecode puro de CLR.

---

## 🌟 Características Principales

### 1. Inyección Estructural Real
- `[ReworkField]`: Añade campos reales a clases del juego (con serialización automática `Scribe` y soporte para inicializadores).
- `[ReworkMethod]` / `[ReworkProperty]`: Forwarders reales expuestos para el juego y otros mods.
- `[ReworkWatch]`: Callbacks reactivos automáticos ante la mutación de campos inyectados.

### 2. Guardado y Carga Binaria (`ReworkBinaryScribe`)
- Guarda y carga partidas en milisegundos mediante un formato binario Deflate compacto (`.rwbin`) con centinela `#REFORJED`.
- Mantiene sincronización periódica de respaldo XML (`.rws`) para máxima seguridad de tus partidas.

### 3. Suite Completa Zero-XML
- Crea `ThingDef`, `NeedDef`, `HediffDef`, recetas, incidentes, mutaciones en caliente, misiones (`[ReworkQuest]`), tabs de colonos (`[ReworkTab]`), alertas (`[ReworkAlert]`) y efectos de zona sin tocar un solo archivo XML.

### 4. Rendimiento & Concurrencia
- `ReworkParallel`: Despacho de simulaciones intensivas a hilos secundarios con entrega segura al hilo principal de RimWorld.
- `ReworkCache`: Capa de caché con TTL en ticks para optimizar bucles pesados.

### 5. Suite de Diagnóstico Integrada
- Abre el **Inspector de Runtime** (`Dialog_ReworkInspector`) directamente en el juego: perfilador de memoria, explorador de clases, telemetría de hilos, consola de desarrollo y visor de parches aplicados.

---

## 📖 Documentación

- Consulta [`MANUAL.md`](MANUAL.md) para la guía completa de la API, ejemplos de uso y especificaciones técnicas.
- Consulta [`ERRORES.md`](ERRORES.md) para la bitácora de diseño, lecciones aprendidas y arquitectura interna.

---

## 📜 Licencia y Créditos

Desarrollado por **DGZ** como parte de la serie **Reforjed**.
Target exclusivo: RimWorld 1.6.4850 rev646.
