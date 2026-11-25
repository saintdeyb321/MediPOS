# MediPOS — FRONTEND FOUNDATION + ONBOARDING 2.0

**Especificación de producto, diseño e implementación para Codex**  
**Fecha:** 25 de noviembre de 2026 · **Ejecución:** F1–F5 en una sesión, con checkpoints internos  
**Repositorio:** `C:\Proyectos\MediPOS` · **Rama:** `ci/postgres-diagnostics-8bc6b4c`  
**Base remota revisada:** `d3e4fd45177ae244e930e37a666029b4fdd85559` (Superadmin S1–S5).

Este encargo sustituye la instrucción de hacer únicamente ACCESS-LEAN/O1–O5. Integra sus garantías de seguridad y recuperación, pero fija un diseño obligatorio, textos de producto, actualización automática y una base responsiva común para todo React. Si hay correctivos O1–O5 locales, conservarlos y mejorar únicamente lo necesario. No repetir lo ya implementado. No basta con presentar un informe o maquetas: entregar código funcionando y evidencia. El objetivo comercial es una aplicación que una persona pueda utilizar sin capacitación técnica.

## 1. Alcance, decisiones y restricciones

- **Rediseño completo:** entrada comercial, selección de botica/sede, primera configuración, gestión posterior de datos fiscales y sucursales, navegación de contexto y estados de licencia relacionados.
- **Fundación transversal:** mejorar los componentes y estilos compartidos, eliminar botones de refresco rutinario y comprobar la responsividad de **todos** los módulos (incluido Superadmin). Corregir defectos visuales y de interacción derivados de esta fundación. No reescribir la lógica comercial de cada módulo ni abrir otra auditoría backend general.
- Mantener React 19, TypeScript, Vite, React Router, TanStack Query v5, Tailwind/CSS existentes, Lucide y tokens de MediPOS. Reutilizar antes de crear. No instalar un framework UI ni un segundo sistema de estilos, estado o cache. No cambiar API/backend, migraciones, esquema, OAuth, RLS, GRANT, SUNAT, CI o S4 sin autorización separada. Si un requisito no es realizable con el contrato actual, implementar lo independiente y explicar el límite exacto; no añadir un endpoint por conveniencia cosmética.
- No convertir operaciones independientes en una supuesta transacción atómica, no automatizar escrituras desde efectos, no repetir POST ante incertidumbre, no inventar idempotencia. No abrir nuevas capacidades de edición/eliminación de entidad o sede inexistentes en API.
- No tocar bases DEV ni escribir datos reales durante pruebas automatizadas o visuales. Usar fixtures o entorno aislado autorizado; mantener `ProductImportRecovery__EnabledAfterWriterDrain=false` mientras corresponda. CPE permanece desactivada. No borrar cachés PWA/IndexedDB, registros ni marcadores pendientes de manera global. No commit, push, merge, deploy ni dispatch automático.
- El éxito tiene tres niveles separados: código y pruebas locales; aceptación visual/funcional en navegador con fixture; aceptación real autenticada con PostgreSQL y Google. Ninguno sustituye a otro. El CI integral del SHA nuevo y los gates operativos siguen pendientes.

## 2. Evidencia de partida y mapa de impacto

Las capturas muestran el mensaje vacío de sede, un enlace administrativo sin jerarquía, la barra de cuatro pasos, pestañas de gestión en el onboarding, listados planos y botones de actualización/paginación aun sin filas. La revisión remota confirma que el último SHA publicado no contiene el nuevo onboarding; comprobar si el árbol local sí lo contiene antes de editar. No concluir que una captura reciente prueba un fallo del nuevo código sin identificar el build servido.

| Área (rutas bajo `frontend/src/`) | Hallazgo y cambio esperado |
|---|---|
| `app/router/EntryRouter.tsx`, `routes.tsx`, SessionGate/LoginPage | Entrada comercial siempre pasa al selector; conservar admisión administrativa independiente, deep links y contingencia. Resolver decisión real, no hacer otro asistente. |
| `features/tenants/TenantSelection.tsx`, `useTenants.ts` | Selección obligatoria y directorio paginado. Una fila con cursor siguiente no prueba unicidad. Caché actual `['tenants']`: comprobar su invalidación y alcance de sesión. |
| `features/branches/BranchSelection.tsx`, `useBranches.ts` | Estado vacío genérico y enlace visible por UUID válido, no por rol. Distinguir Owner, empleado, error y restricción. |
| `features/settings/TenantOnboardingPage.tsx` | Ya valida Owner sin sede. Reutiliza `TenantSettings` completo. Separar presentación de bienvenida y gestión, no duplicar reglas/formularios. |
| `features/settings/SettingsPage.tsx` | Monta licencia, entidades y sucursales simultáneamente; muestra tabs, listados simples, recuperación duplicada en modal y panel. Reemplazar por composición clara de secciones y datos bajo demanda. |
| `features/settings/useSettingsCreation.ts`, `api.ts` | Acuse válido conserva `settingsPending` y `settingsCreated` y obliga a conciliación manual. Validar más que tenant/ID y cerrar el camino confirmado automáticamente. No eliminar el circuito incierto. |
| `features/access/useOperationalWorkspace.ts`, `contracts.ts`, `app/layouts/WorkspaceLayout.tsx` | El acceso operacional se revalida por ruta, focus, remount y cada minuto. Preservar esas garantías y el control real del servidor, aunque se reutilicen lecturas compatibles. |
| `app/layouts/SelectionLayout.tsx`, `WorkspaceShell.tsx`, `WorkspaceHeader.tsx`, `WorkspaceNavigation.tsx`, `app/navigation.ts` | Unificar entrada, ubicación actual, cambio explícito, gestión y salida. Respetar navegación protegida. |
| `shared/ui/PageControls.tsx` | Inserta obligatoriamente «Actualizar consulta» en todos los consumidores; corregir API compartida y migrar usos con análisis de impacto. |
| `shared/ui/Modal.tsx`, `Dialog.tsx`, `ConfirmDialog.tsx`, `FormLayout.tsx`, `TextField.tsx`, `Button.tsx`, `PageHeader.tsx`, `PageToolbar.tsx`, `Panel.tsx`, `States.tsx`, `Badge.tsx`, `Notice.tsx`, `ActionMenu.tsx` | Ya existen primitivas; mejorar aquí para propagar diseño y accesibilidad. Modal mantiene hijos montados para proteger operaciones; no romper esta propiedad sin alternativa verificada. |
| `shared/styles/tokens.css`, `base.css`, `operations.css`, `shared/ui/ui.css`, `app/layouts/{selection,workspace,platform}.css` | Existe sistema de tokens y reglas responsivas. Consolidar duplicaciones comprobadas, no reemplazar todo CSS ni agregar !important indiscriminadamente. |
| `shared/ui/licenseLabel.ts`, `features/platform/{PlatformAreas,PlatformTenantDirectory,PlatformTenantDetails,presentation}.tsx/.ts` | Tipo/estado Trial puede mostrarse aunque vencimiento ya pasó. Distinguir vigencia de estado persistido; no mutar licencias desde UI. |

Backend de referencia: `Modules/Branches/CreateLegalEntity/CreateLegalEntityHandler.cs`, `CreateBranch/CreateBranchHandler.cs`, `BranchDirectoryQueries.cs` (Application) mantienen autorización, auditoría, aprovisionamiento y cupo; las altas son independientes. El directorio comercial no garantiza horario operativo. Revisar contratos reales, no cambiar sus nombres internos para traducir la UI.

## F1 — Fundaciones de diseño y análisis de dependencias

### F1.1 Arranque focalizado

1. `git status --short`, `git rev-parse HEAD`, diff contra base. Leer `AGENTS.md`, `frontend/AGENTS.md`, secciones vigentes de `CURRENT_STATE.md` y únicamente los contratos/SPEC que gobiernan las rutas afectadas. Inventariar consumidores de primitivas, estilos, refresh y selectores con `rg`; excluir tests solo para inventario visual, luego localizar sus regresiones.
2. GitNexus: usar `context`, `trace`, `cypher` y `detect-changes` si está instalado, disponible y autorizado bajo su licencia para este trabajo comercial. Contrastar índice con HEAD y comprobar referencias reales mediante git/rg. Si MCP/FTS falla o índice está obsoleto, usar análisis nativo. No instalar/reindexar indiscriminadamente ni cambiar configuración global. No enviar código, datos o secretos a servicios externos no autorizados.
3. Registrar un mapa breve: símbolo → consumidores → responsabilidad → prueba. Priorizar `PageControls`, `Modal`, `Button`, `TextField`, `TenantSettings`, `useSettingsCreation`, `useTenants`, `useBranches`, acceso y layout. No leer cada archivo del repo.

### F1.2 Skill de diseño (opcional, no bloqueante)

OpenAI documenta la skill `frontend-skill` y su comando en Codex: `$skill-installer frontend-skill`. Fuente: https://developers.openai.com/blog/designing-delightful-frontends-with-gpt-5-4. Comprobar si ya está instalada; invocarla por el nombre descubierto. Si no existe, puede pedirse al operador que ejecute ese comando dentro de Codex o utilizar el instalador integrado si el entorno y la aprobación lo permiten. Revisar procedencia, licencia y `SKILL.md` antes de usar scripts o conceder permisos. No ejecutar instaladores arbitrarios descargados de Internet, no introducir paquetes npm por una skill, no instalar globalmente sin autorización. Si la sesión no expone el instalador, continuar con esta especificación. La skill orienta el diseño; no sustituye AGENTS, contratos ni tests.

### F1.3 Sistema visual obligatorio para MediPOS

**Objetivo:** interfaz operacional tranquila, compacta, clara y reconocible. Mantener marca, tipografía existente y morado de MediPOS. No landing page, héroes de marketing, ilustraciones ornamentales, gradientes, glassmorphism, tarjetas anidadas ni iconos por decoración. Las tarjetas se usan para elegir negocios/sedes y mostrar agrupaciones con sentido; tablas para comparar registros; formularios con secciones. Una acción dominante por tarea. Acciones secundarias discretas. No todos los botones primarios ni todos los estados coloreados.

| Primitiva | Contrato visual y de comportamiento |
|---|---|
| Tokens | Reutilizar `--canvas`, `--surface`, `--text`, `--text-muted`, `--primary`, estados, bordes, radios, control-height y espacios; unificar escala de espaciado (4/8/12/16/24/32 px como referencia), títulos fluidos y densidad consistente. Verificar contraste real. |
| PageHeader / PageToolbar | Título corto, descripción útil opcional de una frase, acción contextual alineada; en móvil acción apilada sin recorte. No botón global que pertenece a otra sección. |
| Panel / tarjetas | Bordes sutiles, padding consistente, min-width:0. Tarjeta de sede: nombre destacado, local/plan/estado solo si real, acción «Entrar». No tarjetas clicables con botones anidados; controles semánticos separados. |
| Button / IconButton | Variantes primaria, secundaria, discreta y destructiva. Pendiente visible, disabled real, etiqueta verbal de efecto, nombre accesible en icon-only. No cambiar ancho bruscamente con spinner ni ejecutar acción doble. Objetivo táctil 44×44 CSS px. |
| TextField / select / formularios | Label visible, ayuda breve asociada, required coherente con contrato, error junto al campo, aria-invalid y aria-describedby, autocomplete/inputmode apropiados; preservar texto ingresado al fallar. No placeholder como único label. En desktop dos columnas solo para campos relacionados; móvil una. |
| Modal / Drawer | Reutilizar dialog nativo, un solo diálogo por tarea, título/objetivo claro, formulario y pie con acción visible. En móvil adaptar a ancho disponible o superficie de pantalla completa según tarea, scroll interno, safe areas, foco no oculto por pie/teclado. Conservar dirty/busy/recovery, Escape permitido solo según seguridad y retorno de foco. No desmantelar hooks/operaciones al ocultar. |
| Tablas / listas | Encabezados semánticos, alineación numérica a derecha, filas legibles, acciones compactas. En móvil tarjetas/lista si ayuda a leer; tablas densas con scroll horizontal contenido, etiqueta accesible y foco, sin scroll horizontal de toda página. Una misma fuente de datos y acciones; no duplicar consultas ni árboles interactivos activos. |
| PageControls | Eliminar refresco obligatorio; navegación solo cuando aporta. Ocultar en colección sin páginas adicionales cuando el contrato lo permite; no inferir última página por datos filtrados o error. Mostrar página actual si útil, anterior/siguiente sin repetir controles. No ocultar posibilidad de explorar registros reales. |
| Loading / Empty / Error / Notice | Skeleton o carga breve; vacío con siguiente acción específica; error contextual y reintento de lectura; éxito breve anunciado sin robar foco. Diferenciar primera carga, actualización de fondo, desconexión y dato antiguo. Nunca mostrar vacío por fallo ni error de escritura como mensaje de otra ruta. |

Composición en vez de herencia de clases: `app → features → shared`; primitiva genérica en shared, semántica de negocio en feature. Reutilizar props/slots y CSS tokens. No mega-componente configurable para todos los módulos ni wrappers por moda. Extraer solo repetición real (dos o más consumidores o necesidad transversal). Conservar API pública de features; si cambia API de una primitiva, migrar todos los consumidores y probar. No usar `memo`, `useMemo`, `useCallback` o efectos por reflejo; estado derivado durante render, medición cuando haya un problema. Un único dueño para cada estado de formulario y cada operación.

## F2 — Entrada comercial y primera configuración

### F2.1 Matriz de navegación (criterios obligatorios)

| Estado demostrado | Vista/acción |
|---|---|
| Superadmin admitido | Plataforma, como ahora. No se concede acceso comercial por ello. |
| 403 explícito de admisión comercial según contrato | Resolver entrada comercial. 429/404/5xx de plataforma no son 403 ni permiso; mantener recuperación/fallback explícito actual. |
| Ninguna botica asignada (lectura completa exitosa) | «Aún no tienes una botica asignada»; indicar que propietario/administrador debe conceder acceso. Sin botón de actualizar permanente; recuperación de error separada. |
| Una botica completa + una sede disponible | Navegar con replace al espacio autorizado tras comprobar acceso real. No renderizar dos selectores durante el camino feliz. |
| Varias boticas o varias sedes | Selector compacto de opciones reales, sin Steps. Mostrar nombre, rol y ubicación actual. Opción «Continuar en [sede]» si hay preferencia válida. No entrar silenciosamente a otra caja/sede. |
| Owner activo, acceso sin sede autorizado y directorio completo vacío | Pantalla de primera configuración. En selector explícito vacío, CTA primaria **«Crear mi primera sucursal»**. No mostrar «contacta al propietario». |
| Trabajador sin sede asignada | «Todavía no tienes una sucursal asignada. Pide al propietario que te asigne una.» Sin controles administrativos. |
| Licencia/horario/asignación restringidos | Explicar motivo según código de API. Ofrecer volver a opciones o ayuda según rol; nunca sustituir por cero sedes/crear otra. |
| Fallo de red/lectura | Error de esa consulta, reintento seguro. Sin auto-redirección ni inferencia de permisos. |

Reglas: no contar únicamente primera página; si cursor presente, mostrar más opciones o consultar de forma acotada. Elegir no equivale a autorización; comprobar sesión, actor, membresía, tenant, branch, rol, licencia, horario y asignación mediante servidor. Usar `WorkspaceLayout` y contratos actuales. Evitar doble fetch secuencial si el resultado exacto ya está en caché y aún puede reutilizarse sin perder comprobación vigente. No disminuir controles por ruta/foco/intervalo de acceso. Mantener aislamiento, cancelación al cambiar identidad, protección de operaciones pendientes, URL directas, atrás/adelante, login invitación, navegación de plataforma a comercial y PWA. Si se guarda última sede: solo IDs mínimos, por identidad y membresía, tras acceso comprobado; preferencia no autoridad, revalidar y descartar si inválida. No forzar persistencia si complica seguridad. El selector explícito abierto por «Cambiar botica» o «Cambiar sucursal» debe permanecer abierto incluso con una opción; ofrecer volver a la actual sin loop. No saltar URL legítima ni marcador pendiente.

### F2.2 Wireframe obligatorio de entrada vacía y selección

- Encabezado compacto: marca, cuenta y salir. Sin barras de pasos, texto sobre autorización o grandes áreas vacías. Centrar el contenido en ancho útil, no en una caja gigante.
- Owner sin sede: título «Tu botica está casi lista» y texto «Crea tu primera sucursal para empezar a vender y controlar tu inventario.» Botón primario con icono de añadir: «Crear mi primera sucursal». Alternativa «Cambiar botica» solo cuando corresponde. Si el flujo normal detecta inequívocamente la primera configuración, puede abrirla directamente.
- Owner con sedes en selector explícito: título «Mis sucursales», tarjetas de sedes, acción «Entrar» por sede, botón visible «Gestionar sucursales». Empleado ve solo sedes asignadas y ninguna acción de gestión. Boticas múltiples: tarjetas con nombre comercial y rol comprensible (Propietario, Cajero, Químico farmacéutico); buscador solo cuando aporta y respeta el alcance. Evitar repetición de «seleccionar» dentro de cada tarjeta.

### F2.3 Primera configuración: una sola vista y un compromiso

```text
MediPOS                                               Mi cuenta · Salir
← Cambiar botica (si corresponde)

Configura tu botica
Completa estos datos para comenzar a trabajar.

[ Datos fiscales de tu negocio ]
RUC                                  Razón social
[                              ]     [                                 ]
Nombre con el que estás registrado en SUNAT (ayuda breve, según campo).

[ Tu primera sucursal ]
Nombre de la sucursal
[ Sede principal (propuesta editable)                                ]
Cada local tiene su propio inventario y caja.

Plan actual · [dato real] · [cupo real si disponible]
                                      [ Crear sucursal y comenzar → ]
```

En desktop secciones fluidas con ancho máximo razonable (~720–900 px para formulario); en móvil campos apilados, acción primaria accesible al final. No tabs «Datos del negocio/Entidades legales/Sucursales/Licencia» en primera configuración. No `Steps` de 4 etapas, no abrir modal por cada campo, no repetir selección después de éxito. Una acción primaria cuando los datos estén válidos. No exigir datos no previstos por contrato. No inventar dirección, teléfono, RUC ni razón social. Usar «RUC» y «Razón social» (explicación: nombre registrado en SUNAT). Mantener identificador sintético solo en QA autorizado y explícitamente no fiscal; no afirmar verificación SUNAT automática ni activar CPE.

Variantes según persistencia:
- Cero registros fiscales: mostrar RUC, razón social y nombre de sede.
- Un registro fiscal existente comprobado: mostrar resumen y reutilizar su ID; no exigir repetir campos. Si hay varios o catálogo incompleto, permitir selección clara, explorar páginas, o registrar otros datos fiscalmente válidos de manera deliberada. No asumir unicidad por primera página o nombre.
- Existe sede: onboarding terminado; abrir entrada/gestión o la sede autorizada. No reiniciar formulario.
- Alta parcial anterior: mostrar «Datos del negocio guardados. Falta crear tu sucursal.» Reutilizar ID confirmado; no repetir primera escritura.

### F2.4 Orquestación segura del formulario

1. Validar ambos grupos antes del clic. Bloquear doble clic mediante guardia sincrónica y estado de mutación; no mutar al montar, en efecto, al seleccionar o al cambiar texto. Sin reintento automático de POST. Usar contratos existentes de `settingsApi`. Cada alta mantiene su propia transacción.
2. Si falta registro fiscal: POST legal-entities, validar acuse definitivo (ID, tenant y atributos según normalización real del backend). Solo entonces POST branches con ID exacto. Si ya existía registro, solo POST branches. Validar ID, tenant, legalEntityId y nombre del acuse de sede. No exigir campos de actor que el contrato no devuelve ni fabricar evidencia. Mantener una operación de alta por vez.
3. Diferenciar fases internas (`idle`, `saving-legal`, `saving-branch`, `confirmed`, `failed-definitive`, `uncertain`) sin exponer una barra de pasos. Mostrar «Guardando los datos…» / «Creando tu sucursal…». Ante éxito de entidad y rechazo definitivo de sede: conservar entidad, campos y contexto; permitir corregir solo sede. Ante GET posterior fallido: no convertir un POST ya confirmado en fallo de escritura. Actualizar cache con acuse cuando es suficiente, invalidar solo lo necesario.
4. Si acuse incompleto, 5xx, desconexión, aborto tras envío o conflicto ambiguo: conservar marcador/intención original por identidad/tenant/membresía, bloquear reenvío y segundo POST, consultar resultado sin escribir. No inferir rollback por 404, lista vacía o nombre/RUC coincidente. Los contratos de estas altas no acreditan idempotencia universal; conservar revisión explícita únicamente si realmente falta evidencia. Proteger cambio de cuenta, recarga, cierre y navegación. No borrar entidades como compensación. No simular transacción atómica en UI. Reutilizar `useSettingsCreation` y protección existente o simplificación demostrablemente equivalente; no duplicar controlador entre onboarding y configuración. Si no puede reconstruirse una intención tras pérdida de respuesta, mantener bloqueo y soporte, no inventar salida automática.
5. Acuse válido de sede: actualizar directorio y verificar acceso operacional actual a esa sede antes de navegar. Si se deniega, informar «Sucursal creada. Aún no puedes ingresar…» con motivo real y retorno seguro, sin repetir alta. En camino feliz, entrar directamente al espacio de trabajo. Una sola acción del usuario y cero conciliaciones manuales cuando todos los acuses son válidos.

## F3 — Configuración posterior, gestión unificada y redacción

### F3.1 Navegación y pantallas

Mantener una sola experiencia de gestión de estructura, accesible desde `Configuración` en el espacio y «Gestionar sucursales» desde selector. Conservar rutas existentes/deep links; la ruta sin sede debe distinguir primera configuración de gestión existente y comprobar Owner sin sede. No crear dos CRUD duplicados. Reutilizar formularios/controlador, con presentaciones específicas de bienvenida y administración. No auto-redirigir al espacio cuando se pidió gestionar.

**Configuración**: navegación local accesible y responsive, sin cuatro botones enormes por toda la página. Secciones: «Mi negocio», «Sucursales», «Plan y licencia», «Historial de actividad» (enlace existente). Importación Excel permanece accesible en su lugar coherente sin duplicar destino. Mostrar solo información/capacidades reales; no presentar nombre comercial editable si no existe contrato.

**Mi negocio**: nombre comercial y datos fiscales; filas/tarjetas con razón social y RUC, «Registrar otros datos fiscales» solo si es válido; fecha de registro opcional en detalle. No «Nueva entidad» ni UUID visible en flujo normal. Si hay varias razones sociales, mostrar relación con sucursales cuando esté disponible; no fingir que una sola razón social representa todo el tenant.

**Sucursales**: título, descripción breve, cupo real (p. ej. «2 de 3 sucursales» solo si conteo completo), CTA «Nueva sucursal» cuando esté permitido. Tarjetas de locales, nombre destacado, razón social vinculada, indicador de sede principal solo cuando `isMainHub` real, acción «Entrar» sometida a acceso; tabla si cantidad/densidad lo justifica. No mostrar acciones no soportadas (eliminar, renombrar, desactivar). Si cupo completo: explicar cómo gestionar plan, no botón muerto. Alta en un único modal, con nombre y selección de datos fiscales únicamente si hay varias opciones; si no hay ninguno, reutilizar formulario de datos fiscales y continuar como F2. Acuse válido cierra modal y muestra nuevo registro automáticamente. Si sección se abre desde espacio, mantener sede actual y permitir cambiar explícitamente, sin trasladar caja/datos.

**Plan y licencia**: tipo/estado persistido, fechas y cupo, vigencia calculada solo como presentación basada en tiempo confiable/lectura disponible y reglas del servidor. Ejemplo: «Prueba gratuita · Vencimiento superado» cuando corresponda, no alterar `trial` ni afirmar suspensión/denegación si existe gracia o regla distinta. Usar formato es-PE y zona Lima coherente con `DateText`; fechas exactas disponibles. Para Owner, indicar contacto/gestión autorizada si no tiene operaciones de licencias. En Superadmin, aplicar el mismo patrón de lectura sin modificar transiciones ni ampliar permisos. Nada de estado activo basado exclusivamente en reloj del navegador.

### F3.2 Diccionario de redacción obligatorio

| Actual/técnico | Texto de producto |
|---|---|
| Configurar entidades y sucursales (solo Owner) | Crear mi primera sucursal / Gestionar sucursales (según estado y permiso) |
| Entidades legales | Datos fiscales |
| Nueva entidad / Crear entidad legal | Registrar datos del negocio / Guardar datos fiscales |
| Identificador de la entidad | RUC (en QA permitido, «Identificador de prueba no fiscal») |
| Entidad legal existente | Datos fiscales de esta sucursal |
| Selecciona en la página de entidades consultada | Elige la razón social de esta sucursal |
| Registrar entidad → Crear sucursal → Seleccionar sucursal → Comenzar | Configura tu botica + botón «Crear sucursal y comenzar» |
| No hay entidades en esta página | «Aún no has registrado los datos fiscales de tu negocio» (solo si vacío completo) |
| No hay sucursales disponibles (Owner) | «Crea tu primera sucursal para comenzar» |
| Cada petición vuelve a autorizarse en el servidor | Quitar de la interfaz habitual; conservar verificación en código |
| Actualizar consulta / Actualizar sucursales | Quitar del recorrido normal |
| Creación reconocida por el servidor… concilia | «Datos guardados» / «Sucursal creada» (solo acuse válido) |
| Alta pendiente de conciliación | «No pudimos confirmar si se guardó. Revisa el resultado antes de volver a intentar.» + «Consultar resultado» |
| Tenant, Owner, branch, receipt, UTC, scope | Botica, propietario, sucursal, resultado, fecha local; detalles técnicos opcionales para soporte |

Mensajes: español natural del Perú, una frase orientativa y una acción específica. No esconder consecuencias financieras, fiscales, de custodia o permisos. No prometer funcionalidad inexistente. Los términos de dominio/contrato se mantienen en tipos, endpoints y pruebas técnicas. Errores mapeados por código con referencia de soporte opcional; sin stack/SQL, secretos ni información privada. Avisos de éxito vinculados a su recurso/área, no persistirlos al navegar a otra pantalla. No ocultar operación pendiente al navegar.

## F4 — Datos reactivos y responsividad transversal (todos los módulos)

### F4.1 Política «sin botones de actualizar»

Eliminar los botones rutinarios «Actualizar», «Actualizar consulta», «Actualizar boticas/sucursales», «Refrescar», «Consultar [recurso]» cuando solo sirven para cargar datos que la vista necesita automáticamente. Inventariar `PageControls`, `refetch`, `refresh`, `invalidateQueries`, `setQueryData`, `Actualizar`, `Reconsultar`, y clasificar por finalidad. Quitar el refresco obligatorio en la primitiva y migrar consumidores; **no ocultarlo con CSS ni dejar un callback muerto**. No retirar «Reintentar» de un error, «Consultar resultado» de una escritura incierta, verificación explícita de una operación de seguridad, o sincronización/contingencia que implica trabajo distinto de refrescar una lectura. Estas acciones son excepciones con significado propio; no llamarlas «Actualizar». Mostrar solo cuando realmente son necesarias.

| Evento | Comportamiento de datos |
|---|---|
| Entrar en pantalla | `useQuery`/hook existente habilitado solo cuando pantalla o tarea lo necesita y el ámbito es válido. Carga automática. Sin GET de tabs ocultas ni catálogo fuera de contexto. |
| Guardado propio confirmado | Validar respuesta; cancelar/evitar que GET anterior pise nuevo estado; `setQueryData` para recurso exacto cuando DTO suficiente; invalidar claves relacionadas acotadas. Si acuse parcial, una lectura necesaria. Nunca reintentar escritura por fallo de lectura. |
| Cambio de filtro/página | Clave declarativa estable, debounce razonable para texto (sin nueva escritura); no reconsultar filtro idéntico; conservar orden/cursor y alcance. |
| Cambios de otra pestaña/usuario | React solo no recibe cambios remotos. Revalidar en foco/reconexión y mediante intervalos acotados únicamente en recursos visibles que lo necesiten. Si ya existe mecanismo seguro de comunicación entre pestañas, reutilizarlo; no introducir WebSocket/SignalR ni polling agresivo. No prometer tiempo real instantáneo sin canal del servidor. |
| 429/5xx/401/403 | Respetar `Retry-After`, cooldown existente en Superadmin, no bucles de refetch ni reintentos masivos. Fallo de autorización retira datos/acciones del contexto afectado; error transitorio muestra estado correcto. |
| Resultado incierto | Preservar marcador de intención y consulta autoritativa exacta, sin reenvío. No confundir una invalidación con conciliación. |

Usar el cliente HTTP, TanStack Query y keys existentes/compatibles. Asegurar claves por identidad, tenant, membresía, sede, recurso y filtros relevantes o aislamiento equivalente comprobado por limpieza de sesión. Revisar logout, cambio de cuenta y scope, cancelar lecturas obsoletas; no ampliar RLS ni cachear datos privados en service worker. Nunca invalidar todo queryClient por una mutación ordinaria; conservar invalidaciones que protegen stock, caja, licencias, permisos, recibos y PWA. No usar `staleTime: Infinity` general ni `retry` de POST; no rebajar las comprobaciones de acceso existentes. Política orientativa para datos de referencia: caché fresca 30–60 s cuando sea adecuada, foco/reconexión; datos comerciales/seguridad según necesidad y reglas existentes. Medir solicitudes antes/después; no fijar TTL arbitrario para resolver 429. Cambios en la misma pestaña se reflejan sin clic de refresco; cambios remotos se reflejan según estrategia explícita de frescura. No mostrar dato antiguo como actual confirmado.

### F4.2 Estándar responsive, accesibilidad y rendimiento visual

Aplicar mobile-first desde primitives y layouts, no estilos ad hoc por pantalla. Usar CSS Grid/Flex con `minmax(0,1fr)`, `min-width:0`, `min(100%, ...)`, `auto-fit` cuando aporte, `flex-wrap`, `max-inline-size:100%`, `overflow-wrap:anywhere` para nombres largos, unidades relativas, `clamp` donde tenga sentido, `100dvh` y safe-area para diálogos/móvil. No anchos absolutos de desktop en formularios, no `overflow:hidden` global para disimular desborde. Breakpoints actuales 740/760/1099/1279 revisar por necesidad real; evitar reescribir por consistencia numérica sin evidencia. Controles no aplastados, textos alineados, paneles sin enormes huecos, tablas no comprimidas a ilegibles. En móvil: una columna, navegación accesible, botones de compromiso visibles sin cubrir contenido, menús que cierran con foco correcto. En POS: carrito, cobro y búsqueda usables con pantalla/teclado/lector, sin scroll anidado que oculte total o acción. Ticket/impresión conserva CSS print y contenido. No renderizar ambas versiones interactivas simultáneamente.

Objetivo WCAG 2.2 AA (no declarar conformidad completa sin auditoría): contraste texto normal 4.5:1, texto grande 3:1, controles/foco 3:1 donde aplica; foco visible y no cubierto, navegación teclado, labels y nombres accesibles, no solo color, estados anunciados con live regions moderadas, orden de lectura lógico. Reflow a 320 CSS px salvo tabla/visualización que requiere dos dimensiones; scroll localizado. Objetivo táctil 44×44 CSS px superior al mínimo AA de 24×24 sujeto a excepciones. Zoom 200%, comprobar reflow equivalente a 400% donde proceda. `prefers-reduced-motion`. No `tabIndex` positivo; diálogo con título asociado, trampa/retorno de foco nativos y cierre seguro. No sustituir semántica con div clicable. No uso obligatorio de hover. Probar teclado móvil y etiquetas largas.

### F4.3 Matriz de módulos y comprobaciones

| Superficie | Comprobar y corregir |
|---|---|
| Login, invitación, selección, onboarding, Configuración | Formulario y CTA visible; estados correctos; tarjetas de sede/negocio; foco y menú. |
| Superadmin completo | Directorios, filtros, categorías, licencias, modales; conservar cooldown, políticas de acceso y consultas de S1–S5. |
| Inicio/dashboard, reportes, comisiones | Grids adaptables, cifras legibles, filtros y tablas localizadas; no perder periodos/decimales. |
| POS, caja | Búsqueda/lector, carrito, cobro, arqueo, ticket, teclado; una escritura por intención y recuperación segura. |
| Productos, inventario, compras | Tablas, catálogos, lotes, presentaciones, formularios y acciones responsivas; mantener versión y precisión. |
| Transferencias de mercadería y efectivo | Resúmenes de custodia, origen/destino, importes, modales; no eliminar transiciones ni verificación requerida. |
| Personal, invitaciones, notificaciones | Tarjetas, listas, acciones según rol; conservar verificación independiente y revocación. |
| Excel, auditoría, contingencia/offline | Tablas anchas, resultados parciales, carga, consulta original y sincronización; mantener límites y recuperación. |

Corregir primitives primero, después usos concretos que todavía fallan. No hacer reescritura cosmética masiva de 600 pruebas. Todo módulo debe tener comprobación visual o quedar explícitamente `PENDIENTE_VISUAL` con razón; no declarar responsive global solo por CSS o jsdom. Si un defecto compartido se propaga, corregir raíz en primitive; un layout exclusivo de un feature puede tener CSS local.

## F5 — Validación, evidencia y ejecución local del candidato

### F5.1 Pruebas automatizadas

Reutilizar Vitest/Testing Library y fixtures existentes, no añadir dependencias de runtime. Localizar `EntryRouter.test.tsx`, `routes.test.tsx`, `TenantOnboardingPage.test.tsx`, `SettingsPage.test.tsx`, pruebas de acceso, sesión, navegación, PWA, `PlatformPage.test.tsx` y consumidores de primitives. Adaptar nombres accesibles legítimamente cambiados, no reducir cobertura. Probar comportamiento y no snapshots frágiles de estilos. Usar dobles HTTP con rutas estrictas; no afirmar que son PostgreSQL real.

Casos mínimos: una botica/una sede, varias (incluye paginación), selector explícito con una, Owner cero sedes y empleado cero asignaciones, rol antiguo o cambiado, licencia/horario revocado, error distinto de vacío, deep links y back/forward, Superadmin 403/429/404, cambio identidad/tenant/branch con GET tardío, preferencia obsoleta, guardia de navegación y PWA. Primera configuración: cero registros → dos POST secuenciales por un clic, existente → uno, doble clic/Enter/StrictMode, respuesta inesperada/tenant distinto, primer POST exitoso + segundo rechazo, pérdida de respuesta en cada fase, recarga parcial, GET fallido después de acuse válido, sede creada pero acceso denegado, cupo alcanzado, datos existentes sin duplicar. Verificar cero reenvíos y cero escrituras por simple navegación/filtro/focus. Guardados confirmados reflejados sin botón Actualizar. Configuración y onboarding usan misma lógica; recuperación solo una superficie operable por intención. Probar `PageControls` sin refresco rutinario, páginas vacías/últimas, `Modal` foco/busy/recovery, filtros, cooldown 429, protección de POS/caja/compra y pruebas de sync. Conservar suite previa: base reportada 600 frontend PASS en Superadmin, no convertirla en cifra obligatoria si se agregan casos; justificar cada variación.

Ejecutar suites focalizadas por grupo y al final desde `frontend/`: `npm run build`, `npm run lint`, `npm run test`. `git diff --check`. No repetir suites backend si backend intacto. No modificar CI ni omitir pruebas. Registrar PASS/FAIL/SKIP reales y total de archivos; flakiness: reproducir y resolver raíz o marcar pendiente, nunca ocultar primer fallo.

### F5.2 Navegador y diseño: gate indispensable

Usar Chrome/DevTools, herramienta de navegador o automatización existente si disponible. No introducir dependencia nueva sin aprobación. Fixtures/dobles solo en entorno de prueba controlado, no habilitar bypass Google en DEV real. Capturas sin correos, tokens, UUID innecesarios ni datos privados. Probar al menos: 320×640, 390×844, 768×1024, 1366×768 y 1920×1080 CSS px; zoom 200% y reflow; orientación landscape móvil si aplica; nombres largos, listas vacías/llenas, diálogo alto, teclado Tab/Shift+Tab/Escape/Enter y estado de error/pendiente. Revisar dimensiones con `document.documentElement.scrollWidth <= clientWidth` (excepción documentada para tabla contenida), controles visibles, foco sin obstrucción, scroll y contenido recortado. Registrar tabla de cobertura de F4.3 con viewport y resultado; no usar jsdom para afirmar layout real. Verificar al menos onboarding antes/después, selector, gestión sedes, modal de alta y navegación desktop/móvil mediante capturas o recorrido real. Si no hay browser accesible, dejar comandos y checklist para operador y estado `PENDIENTE_VISUAL`; no inventar resultados.

### F5.3 Gate de producto y mediciones

| Métrica | Criterio de aceptación |
|---|---|
| Login una opción elegible | 0 selecciones manuales; permiso real comprobado. |
| Owner nuevo | 1 vista, 1 compromiso, máximo 2 POST de alta normal; 0 clics de conciliación si acuse válido. |
| Owner con registro fiscal válido | 1 POST para nueva sede; sin duplicar registro fiscal. |
| Usuario existente | No vuelve al onboarding salvo incompleto; cambio explícito disponible. |
| Refresco rutinario | 0 botones en recorridos normales; lista de excepciones justificadas (error, resultado incierto, contingencia). |
| Consultas de vista oculta | 0 salvo dependencia necesaria documentada; sin incremento de escrituras por interacción. |
| Componentes | Sin variantes locales duplicadas de botones, campos, diálogos o tablas creadas por esta fase. |
| Responsive | Sin overflow horizontal de página, CTA/foco visibles, contenido comprensible en anchos definidos; cobertura por módulo. |
| Integridad | No fuga entre contextos, no POST duplicado, recuperación intacta, autorizaciones reales conservadas. |

Separar `ESTIMADO_POR_CODIGO`, `MEDIDO_DOM`, `MEDIDO_NAVEGADOR_FIXTURE`, `MEDIDO_DEV_REAL`. No inventar tiempos, número de GET ni métricas de Core Web Vitals. Comparar interacción y peticiones en rutas representativas, no buscar microoptimización sin medición. Si los gates de autorización/entorno impiden prueba real, seguir con correcciones independientes; no declarar piloto aprobado.

### F5.4 Evitar que el operador vea otra vez el frontend viejo

Verificar identidad del build servido, no solo HEAD. Preparar arranque con comandos conocidos:

```powershell
cd C:\Proyectos\MediPOS\frontend
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Error de build frontend' }
cd ..\backend
$env:ProductImportRecovery__EnabledAfterWriterDrain = 'false'
dotnet run --project src/MediPOS.Api --launch-profile https-local
```

Antes de arrancar, identificar PID en 7243 y no iniciar otra instancia encima. Detener/reiniciar proceso DEV identificado solo con aprobación del operador; no matar procesos ajenos. Comparar `index.html` y hashes de assets realmente servidos con `frontend/dist`, verificar readiness, preservar configuración privada y datos PWA. Si no puede reiniciarse, indicar exactamente qué falta. No publicar ni ejecutar migraciones. En Chrome hacer recarga normal/forzada si procede, sin borrar almacenamiento de operaciones. Aceptación real con Google requiere que operador autentique sus cuentas; no capturar credenciales ni simular permiso.

### F5.5 Entregable y reglas permanentes

Actualizar `docs/CURRENT_STATE.md` con estado F1–F5, SHA base, cambios, pruebas, mediciones y pendientes. Añadir solo una sección breve a `frontend/AGENTS.md` si no existe equivalente: diseño/terminología, responsive de primitives, auto-refresco selectivo y no doble escritura, evidencia visual. Mantener reglas anteriores y evitar contradicción. Este documento queda como referencia de diseño, no crear otros diez planes. No añadir skill propia duplicada si AGENTS + este MD bastan.

Entregar una conclusión consolidada con: archivos principales y consumidores, 5 grupos, cambios de UX/copy, lista de refrescos rutinarios eliminados/excepciones, peticiones/acciones antes y después, capturas y cobertura responsive por módulo, pruebas con fallos/skips y alcance del entorno, versión local servida o instrucción de arranque, riesgos reales. Dictamen separado `GO/NO-GO publicar candidato`, `PENDIENTE_ACEPTACION` o evidencia, `NO-GO piloto` hasta CI y gates reales. No declarar que acabó toda la interfaz solo porque pasó Vitest. No detenerse entre grupos por formalidad; detener únicamente tareas que requieran aprobación o riesgo no autorizado. No abrir fases nuevas sin defecto real.

## Referencias técnicas verificadas

- OpenAI, Frontend Design skill: https://developers.openai.com/blog/designing-delightful-frontends-with-gpt-5-4 — aplicar criterios de producto y diseño, no estilos de landing page.
- TanStack Query v5, invalidación después de mutaciones: https://tanstack.com/query/latest/docs/framework/react/guides/invalidations-from-mutations
- TanStack Query, claves de consulta: https://tanstack.com/query/latest/docs/framework/react/guides/query-keys
- TanStack Query, invalidación selectiva: https://tanstack.com/query/latest/docs/framework/react/guides/query-invalidation
- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/ — reflow, contraste, foco, tamaño de objetivo; objetivo AA, no certificación declarada.
- MediPOS: `frontend/AGENTS.md`, `docs/CURRENT_STATE.md`, `docs/MEDIPOS_FRONTEND_UX_LEAN.md`, `docs/MEDIPOS_ONBOARDING_ACCESS_LEAN.md` (si existe localmente), contratos y pruebas actuales. Este documento prevalece en decisiones de UX del alcance, no sobre las invariantes de seguridad o especificación comercial.
