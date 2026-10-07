# Rework Reforjed — ERRORES

Lo que **NO** debes usar, **NO** tocar y **NO** hacer. Estas son las reglas duras del
framework: cada punto de aquí causó un crash real, un guardado corrupto o un arranque roto.
Léelo antes de publicar cualquier mod basado en Rework.

---

## 1. NUNCA uses Harmony

Rework es un **reemplazo TOTAL de Harmony**. Nunca uses Harmony para parchear IL en runtime,
nunca uses trampolines de código máquina y nunca envuelvas métodos del juego con prefixes o
postfixes de Harmony junto a los parches de Rework.

- Si un mod trae ensamblados de Harmony (`0Harmony.dll`, `HarmonySharedState`), Rework los
  detecta y los trata como ensamblados protegidos de solo-lectura (sin parches, sin
  procesamiento de atributos) para evitar colisiones de runtime. La coexistencia es pasiva:
  no esperes que interoperen.

## 2. NUNCA toques los DLL del juego en disco

Los DLL originales de RimWorld **nunca se modifican**. Todo ocurre en memoria con
Mono.Cecil. No escribas un launcher, un sistema de swap ni un paso manual que parchee
`Assembly-CSharp.dll` en disco — se probó y se descartó porque requería el juego cerrado,
rompía la portabilidad y generaba desconfianza.

## 3. NO añadas una interfaz de tu mod a una clase del juego

Añadir una interfaz de tu mod (que se recarga) a una clase del juego
(p. ej. `Verse.Pawn : IReworkContadorLogros`) rompe la **vtable** de la clase al arrancar:

- `TypeLoadException: Could not load type 'Verse.Pawn[]'...`
- `TypeLoadException: VTable setup of type Verse.Pawn failed`

**Es estructural, no un bug que se pueda esquivar.** Alternativa segura: crea una **subclase
NUEVA** que herede de la clase del juego (el patrón `ReworkWorldCameraDriver`) y sustituye su
uso — nunca modifiques la clase existente.

## 4. NO cambies la clase base de una clase del juego

Cambiar la clase base de un tipo del juego corrompe la vtable/layout y falla al arrancar.
Aplica la misma alternativa segura: subclasea, no modifiques.

## 5. NO añadas campos dentro de structs

Añadir un campo **dentro de un struct existente** — o cambiar su tamaño — rompe el layout de
memoria y la semántica de copia por valor. Los campos solo pueden añadirse a **clases**
(tipos de referencia). Un struct usado como TIPO de campo en una clase sí funciona
(p. ej. `Verse.IntVec3` en `Verse.Pawn`).

## 6. NO uses `[ReworkInterface]` ni eventos/constructores declarativos que no existan

Solo estas adiciones declarativas están soportadas: métodos, propiedades y atributos.
**Los eventos y constructores no tienen API declarativa.** Si los necesitas, escribe Cecil
puro vía `[ReworkPatch]` — y solo si entiendes bien el método destino. Las adiciones no
soportadas se omiten con un aviso; nunca se aplican a medias en silencio.

## 7. NO serialices arrays ni colecciones de objetos automáticamente

`[ReworkField(Serialize = true)]` soporta: primitivas, `string`, enums,
`List<primitiva/string/enum>`, `Dictionary<K,V>` y `HashSet<T>` (de primitivas/string/
enums). **NO** soporta:

- arrays (`T[]`)
- colecciones de objetos (referencias / Deep)
- otros genéricos (`Stack`, `Queue`, …)

Un tipo no soportado funciona en runtime pero **no viaja en el guardado** — Rework loguea un
error claro y nunca rompe la partida guardada. Usa un `List` o Scribe manual en esos casos.

## 8. NO borres los límites de los manejadores de excepción en un transpiler

El modo transpiler reutiliza el mismo MethodBody y **preserva los manejadores de excepción**.
Si borras las instrucciones límite de una región `try/catch`, ese manejador se descarta con
un aviso. En el peor caso corrompes el método destino y obtienes un arranque en negro.
Mantén intactas las regiones EH; toca solo las instrucciones que necesites.

## 9. Helpers de IL: NO uses operandos numéricos en `Starg`/`Ldarg`

`Starg`/`Ldarg` con un operando numérico `(byte)` lanza `ArgumentException: opcode` — el
opcode espera un `ParameterReference`, no un int.

- Usa `Starg(ParameterDefinition)` / `Ldarg` con el `ParameterDefinition` real
  (p. ej. `targetMethod.Parameters[i-1]`).
- Las formas cortas `Ldarg_0` … `Ldarg_3` son seguras.

## 10. NO confíes a ciegas en el orden de `InsertBefore`

Al inyectar un hook con contexto, el IL correcto es `ldarg.0; call hook`. Si insertas `call`
y luego `ldarg.0`, obtienes `call; ldarg.0` → pila inválida →
`System.InvalidProgramException: Invalid IL code ... IL_0000: call` en el momento en que el
método se ejecuta (el arranque NO lo detectará — solo la línea del log al ejecutarse).
Inserta `ldarg.0` PRIMERO y luego `call`. El framework ya lo maneja automáticamente; no
re-insiertes manualmente.

## 11. NO crees nombres de ensamblado duplicados

Un ensamblado de mod con el mismo nombre simple que otro hace que vanilla deduplique a una
única identidad de runtime; procesar ambos contra el mismo módulo duplica efectos y es
caótico de raíz. Reglas:

- Mantén los nombres de ensamblado **únicos**.
- Nunca re-empaques `0ReworkAPI.dll` ni `Mono.Cecil.dll` — referencia la API con
  `<Private>false</Private>` / `ExcludeAssets="runtime"` para que resuelva contra la copia de
  Rework (misma versión → misma identidad).

## 12. NO registres Defs durante la Pasada 1 ni en la init temprana de mods

El registro en `DefDatabase` (Jobs, Incidentes, WorkGivers, …) no puede ocurrir durante la
primera pasada ni en `LoadedModManager.InitializeMods` temprano — los Defs vanilla aún no
están cargados. Usa siempre `[StaticConstructorOnStartup]` (los registradores del framework
lo hacen por ti). Hacerlo temprano produce Defs vacíos/rotos en silencio.

## 13. NO hagas cuerpos de hooks pesados (los hooks de tick disparan 60/s)

`MapTick`, `PawnTick`, `GameComponentTick`, `MapComponentTick`, `WorldComponentTick` y
`StorytellerTick` se disparan 60 veces por segundo. Mantén el cuerpo baratísimo o usa un
contador/intervalo, o hundirás el rendimiento.

## 14. NO asumas que `this` es el contexto del hook

Algunos hooks pasan otro sujeto:

- `CurrentMapChanged` → el mapa NUEVO (parámetro del setter).
- `BabyBorn` → la MADRE biológica (parámetro 5; el bebé aún no es un Pawn).
- `CaravanEnteredMap` → el `Verse.Map` destino (parámetro 2).
- `PawnLeftToCaravan` → el pawn que sale (parámetro 1).

El framework valida que tu tipo de parámetro coincida; si no, el hook se omite con un aviso.

## 15. NO trates el "arranque limpio" como prueba de que la lógica funciona

Un arranque limpio (0 NREs, `VERIFICACIÓN OK`) prueba que la reescritura se aplicó,
**no** que la lógica se dispare correctamente. Los errores de IL/parcheo que solo aparecen al
ejecutar un método (como el `InvalidProgramException` del §10) no salen en el arranque.
Valida siempre el comportamiento en una sesión real: guardar/cargar, matar un pawn, crear una
caravana, etc.

## 16. NO uses Defs dentro de `[ReworkInit]`

`[ReworkInit]` corre en la fase de carga de mods, **antes de que los Defs estén cargados**.
No toques `SkillDefOf`/`ThingDefOf`/etc. ahí. Si necesitas Defs, combina con el vanilla
`[StaticConstructorOnStartup]`.

## 17. NO desactives la seguridad ni ignores el log

- **Modo seguro proactivo:** si la verificación del entorno falla (runtime no Mono o layout
  nativo inesperado), Rework no reescribe nada y arranca vanilla con un aviso. No lo
  "arregles" forzando una reescritura — protege contra memoria corrupta.
- **`Rework.log`:** léelo. Cada trampa de aquí tiene un mensaje claro allí. Errores como
  "No se puede serializar automáticamente" o "campo no soportado" son benignos por diseño; no
  los silencies sin leer la razón.
- **Exclusión de `ReworkConfig`:** los mods excluidos no se procesan en absoluto. Si un mod
  "no hace nada en silencio", comprueba que no esté en `ExcludedMods`.

## 18. NO compartas un nombre de campo entre mods con tipos distintos

El primer accessor con un nombre crea el campo. Los mods posteriores con el mismo
nombre+tipo **se enlazan al campo compartido**; mismo nombre + tipo distinto → conflicto real
(el accessor se reescribe para lanzar `InvalidOperationException`). No puedes "ganar" un
override — el primer mod en orden de carga es dueño del campo.

## 19. NO modifiques la identidad de `0ReworkData.dll` / `0ReworkAPI.dll`

`0ReworkData` sobrevive la barrera del reload (DataStore persistente) y `0ReworkAPI` es la
superficie pública para modders. Se cargan antes que ReworkCore y deben mantener sus
identidades y rutas estables; re-firmar, renombrar o re-empaquetarlos rompe el reload y la
API cross-mod.

---

## Líneas de referencia verificadas (arranque normal y sano)

Estas líneas son ESPERADAS en un arranque sano — no son errores:

```
Entorno: Runtime=Mono, Mono=6.13.0 (Visual Studio built mono), layoutOK=True → seguro para reescribir
VERIFICACIÓN OK: Assembly-CSharp activa es la NUEVA (rewrite en efecto) y ReworkWorldCameraDriver enganchado.
Versión del juego soportada: 1.6.4850
```

Y estas son las clásicas que **rompen el arranque** — búscalas y corrígelas:

```
NullReferenceException … WorldCameraDriver / ExpandableWorldObjects
TypeLoadException: VTable setup of type Verse.Pawn failed
System.InvalidProgramException: Invalid IL code … IL_0000: call
ThreadAbortException: Thread was being aborted   (ruido de la transición de la pasada 1, filtrado por diseño)
```