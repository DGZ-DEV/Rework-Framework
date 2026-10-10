# Rework Reforjed — ERRORES

Lo que **NO** debes usar, **NO** tocar y **NO** hacer. Estas son las reglas duras del
framework: cada punto de aquí causó un crash real, un guardado corrupto o un arranque roto.
Léelo antes de publicar cualquier mod basado en Rework.

---

## 1. Rework no usa Harmony (ni se integra con él)

Rework reescribe el ensamblado del juego **en memoria, antes de que se cargue**; no intercepta
métodos en runtime. Son dos modelos que actúan en momentos distintos, así que un `[HarmonyPatch]`
no lo procesa Rework ni se combina con sus parches: no esperes que interoperen.

- Si un mod trae ese tipo de librerías (`0Harmony.dll`, `HarmonySharedState`), Rework las marca
  como **ensamblados protegidos de solo-lectura** — el mismo trato que `mscorlib` o `UnityEngine`:
  no los reescribe, no procesa sus atributos y no los recarga. Es una medida **defensiva** para
  que el juego arranque aunque estén presentes, no una integración.

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

**El framework ya no añade coste por su cuenta (§53).** Los despachadores de hooks aplican tres
reglas de bajo riesgo con impacto directo en TPS:

1. **Early-out:** si no hay ningún suscriptor registrado para ese punto (p. ej. ningún
   `[ReworkSchedule]`, ningún `[ReworkOn]` de tick, ningún efecto activo), el despachador retorna
   *antes* de entrar en `try/catch` o de alocar el evento. Comprobar `.Count == 0` es O(1).
2. **Delegados cacheados:** `ReworkBus` y `ReworkScheduler` guardan un `Delegate` compilado
   (`Action<T>` directo o `Delegate.CreateDelegate`) resuelto en el registro, **no** un `MethodInfo`
   invocado por reflexión en cada llamada. Se elimina así el `MethodInfo.Invoke` + la allocación de
   `object[]` por evento.
3. **`try/catch` fuera del camino caliente:** el dispatcher ya no envuelve el despacho completo.
   Cada subsistema (`ReworkScheduler`, `ReworkBus`, `ReworkInspectStringRegistry`, …) protege su
   propia invocación de usuario; el trabajo del framework no puede lanzar y por tanto no necesita
   protección externa.

Si tu hook es declarativo (`[ReworkHook]`), el `call` se inyecta directo en el método del juego: no
hay delegado ni `try/catch` tuyo. Mantén igualmente el cuerpo barato.

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

## 20. NO uses `MethodInfo.Invoke` en un camino caliente — cachea el delegado

`MethodInfo.Invoke` resuelve por reflexión y **aloca un `object[]` por llamada**. En un tick que
dispara 60/s (o por cada pawn/tick) eso es basura de GC y CPU medida en milisegundos acumulados.
El framework ya lo evita (§53): `ReworkBus.Subscribe<T>`, `ReworkBus.Register` (vía
`Delegate.CreateDelegate`), `ReworkScheduler.Register` y `ReworkWatchRegistry` guardan un
`Delegate` compilado resuelto **una vez** en el registro.

Si escribes un registry propio, haz lo mismo:

```csharp
// Bien: delegado cacheado en el registro
Action<object> handler = /* ... */;
// Mal: resolver y invocar en cada tick
methodInfo.Invoke(null, new object[] { arg });
```

`Delegate.CreateDelegate` devuelve `null` si la firma no encaja (p. ej. método con parámetros);
en ese caso el framework cae a `MethodInfo` como respaldo — nunca revienta.

## 21. NO asumas que "registrado" = "usable" (defs acompañantes)

Un Def añadido a `DefDatabase` en runtime **existe**, pero puede no ser *usable*: vanilla crea
Defs implícitos/acompañantes durante la carga de XML que no se reproducen al añadir un Def a mano.
El caso canónico es §41: un `WorkTypeDef` visible sin su `PawnColumnDef` aparece en la lógica pero
**sin casilla en la pestaña de Trabajo** — el jugador no puede verlo ni regularlo.

- **Regla:** tras añadir un Def en runtime, llama a
  `ReworkCompanionDefRegistry.GenerateFor(def)` (registra el generador con
  `ReworkCompanionDefGenerator.EnsureRegistered()` al arrancar). El framework genera así el
  `PawnColumnDef` `WorkPriority_<defName>` y lo inserta en `PawnTableDefOf.Work.columns`.
- **Verificación:** no te fíes del "registrado = 1". `ReworkContentSelfCheck` (§55) comprueba
  *registrado **y** usable* y escribe un informe; los fallos de usabilidad salen como error con
  el motivo concreto.
- Need/Gene/Research **no** tienen companion defs en vanilla (se descubren por
  `DefDatabase<T>.AllDefs`): el generador es genérico y extensible por si un tipo futuro los
  necesita.

## 22. NO asumas que una API declarada se activa sola («fantasmas»)

Que un sistema exista en `0ReworkAPI` (registro, evento, store persistente) **no** significa que
algo lo dispare. Se auditaron 13 «fantasmas»: features con registro pero **cero llamadores** —
existían en la API, se serializaban incluso, pero nunca se ejecutaban (el sistema de overlays
llegó a tener el despacho inyectado en el juego sin existir un atributo que llenara el
registro: `Count == 0` permanente → early-out → nunca dibujaba).

- **Regla:** cada API necesita un *consumidor real* en el runtime: un hook, un schedule, un
  init o un punto del pipeline que la invoque. Si añades una, conéctala en el mismo commit.
- **Chequeo rápido:** busca los llamadores del método de registro en el fuente. Si el único
  resultado es la definición, es un fantasma.
- **Verificación en log:** la línea `[ReworkAttributeScanner] Escaneo completo: … Overlay=N`
  muestra los contadores; un contador en `0` cuando esperabas contenido activo es la señal.
- Las APIs **utilitarias** diseñadas para que las llamen mods externos (caché, profiler,
  diálogos, pools de jobs, world store) no son fantasmas: su consumidor es el modder, y están
  documentadas en el manual.

## 23. NO dejes una API declarativa a medias: «registrado ≠ aplicado ≠ obtenible»

Registrar un Def en `DefDatabase` es solo el primer eslabón. El framework garantiza ese; la
**aplicación** o la **obtención** pueden ser tuyas. En la auditoría §48, `[ReworkHediff]`,
`[ReworkThought]` y `[ReworkApparel]` registraban correctamente y aun así **no se veían en juego**
por esto:

- `[ReworkHediff]` → necesita algo que lo aplique: receta con `addsHediff`, tu código
  (`pawn.health.AddHediff`) o un `[ReworkStatusEffect]` con el mismo defName.
- `[ReworkThought]` → necesita algo que lo conceda (`TryGainMemory`, un ThoughtWorker). Además la
  clase decorada **no** se conecta al Def: el pensamiento usa `ThoughtClass` (por defecto
  `Thought_Memory`), pásalo si quieres tu lógica.
- `[ReworkApparel]` → necesita `ThingCategoryDef`, receta, `costList` o stock de trader, o no
  aparece en el juego normal. Nace además sin `statBases` y `TexPath` exige una textura real.
- `[ReworkJob]` → necesita un emisor: `[ReworkWorkGiver]`, un thinktree o `TryTakeOrderedJob`.
- `[ReworkRecipe]` → nace sin `products`: es el patrón «worker total» (cirugía). Añádelos con
  `[ReworkMutate]` o código si quieres fabricar objetos.

Los que vanilla consume por su cuenta (Trait, Need, Gene, Research, Raid, Incident, Designator,
GenStep, Mutate) no tienen este problema. El detalle está en el manual.

## 24. NO dejes un fallo en silencio (catch vacío, `return false` sin log, flag sin reset)

Los «fantasmas» de la §48 no eran solo features sin llamador: eran **fallos mudos**. Un manejador
del bus que reventaba dentro de un `catch { }`, un `Apply` que devolvía `false` sin decir que
faltaba el HediffDef, un flag global de alertas que nunca se reseteaba (la segunda partida se
quedaba sin alertas), un `GenStepDef` sin enlazar a ningún `MapGeneratorDef` que jamás se
ejecutaría. Ninguno daba error: simplemente «no funcionaba».

- Todo camino de fallo escribe una línea (`Lg.Error`) con el motivo concreto.
- Nada de `catch { }` vacíos en el framework: el error va a un hook de log.
- Si un registro puede quedar en `0`, publica el contador (`Escaneo completo: …`) para que el cero
  sea visible.

## 25. NO añadas una subclase que vanilla instancie por reflexión sin constructor público sin parámetros

Vanilla crea objetos por reflexión con `Activator.CreateInstance(tipo)`. El caso que costó un
cuelgue (§39): `RimWorld.AlertsReadout..ctor()` sondea **todas** las subclases no abstractas de
`RimWorld.Alert` y las instancia, así que una alerta sin constructor público vacío lanza
`MissingMethodException`, `UIRoot_Play` no se construye, `Find.MapUI` queda null y generar el mapa
revienta (el juego parece congelarse). Vale igual para `GameComponent`/`MapComponent` y cualquier
tipo que vanilla recorra por reflexión.

- Si heredas de un tipo que vanilla instancia por reflexión, dale un ctor **público sin
  parámetros**, aunque esté vacío: es el que usan `Game.FillComponents` y el readout de alertas.

## 26. NO arregles un bug solo en el binario (el `.cs` manda)

Un parche aplicado solo en la DLL se pierde en el siguiente `dotnet build`. Ya pasó (§39.1): un fix
del cuelgue al generar el mapa vivía solo en el binario y desapareció al reconstruir. Todo arreglo
va al fuente, compila a **0 errores** y después se despliega: el binario es un producto, no la
fuente de verdad.

## 27. Contribuir al motor: NO importes referencias por adelantado ni toques ensamblados sin sitios reales

El bucle de arranque de la §47 salió de aquí: los procesadores de la cirugía IL llamaban a
`module.ImportReference` en **todos** los ensamblados reescribibles, incluidos los que no tenían ni
un call-site. Eso añade una `AssemblyReference` al ensamblado aunque no la necesite, la propagación
del reload lo marca como dependiente y acaba entrando al intercambio —incluido `0ReworkData`, que
**nunca** debe recargarse (barrera)— con lo que el arranque entra en bucle.

- Importa una referencia solo cuando hay un sitio real que la use (import perezoso).
- Respeta la barrera: `0ReworkData` no se intercambia jamás. El set de swap se loguea siempre
  (`Reload: intercambiando …`) y hay una guardia anti-bucle por si algo lo marca.

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