# MediPOS — UX-LEAN | Refactorización funcional del frontend

**Documento de ejecución para Codex · RC1**  
**Repositorio:** `C:\Proyectos\MediPOS`  
**Rama:** `ci/postgres-diagnostics-8bc6b4c`  
**Base de código observada:** `47e4335b2c46f0de874797724ba30e123705ea79`  
**Objetivo:** reducir pasos, clics, confirmaciones y tráfico innecesarios, alineando React con la lógica comercial simplificada del backend. Conservar la identidad visual, los contratos y las garantías de integridad. No ampliar el alcance del MVP.

## 1. Mandato y límites de esta revisión

Actúa como Principal Frontend Engineer y especialista en UX de sistemas POS, React, TypeScript, accesibilidad, TanStack Query y recuperación segura de operaciones. Implementa las mejoras justificadas de U1–U5 en un único encargo, con checkpoints internos. No te detengas al acabar un grupo si puedes continuar sin riesgo. Si un cambio exige alterar un contrato, datos, esquema, permisos o infraestructura, bloquea únicamente ese punto y continúa los independientes.

El usuario quiere que una operación habitual pueda realizarse sin marcar constantemente casillas de «revisé» o «confirmo», sin formularios excesivamente fragmentados, y sin reconsultar innecesariamente después de una respuesta definitiva. El producto debe ser intuitivo para cajeros, farmacéuticos, Owners y Superadmin; no debe exigir que comprendan la arquitectura interna para trabajar.

**No es un rediseño visual desde cero ni una nueva auditoría A360.** No sustituir los patrones actuales por una librería, store, motor de formularios o framework nuevo. Mantener React, React Router, TanStack Query, Tailwind, Vite, las fronteras de features y `shared/ui`. No tocar backend, migraciones, RLS, GRANT, OAuth, SUNAT, datos de DEV, S4 ni secretos. No crear un nuevo sistema de idempotencia. No modificar el workflow de certificación pendiente ni suprimir controles para obtener un PASS. No commit, push, merge ni despliegue automáticos.

### Evidencia que condiciona el trabajo

- E0 tuvo 2664 unitarias, 8 arquitectura y 904 PostgreSQL 18.7 aprobadas, sin fallos ni omisiones, en `4523902c848a53f7f1d9eb89de3e2d8a7a03ae11`: [run 45325845075](https://github.com/saintdeyb321/MediPOS/actions/runs/45325845075). No trasladar esta certificación a otro SHA.
- RC1 reportó 592 pruebas frontend PASS en dos corridas completas y build/lint PASS. El backend del RC1 tiene pruebas focalizadas locales aprobadas, pero no tiene gate PostgreSQL integral publicado para su SHA al momento de esta revisión. El ajuste de CI frontend se reportó localmente. Verificar HEAD, árbol local y ejecución más reciente antes de iniciar. Si existe certificación en curso, no interferir; los cambios de UX requerirán su propio candidato. No afirmar una aceptación real en Chrome por pasar Vitest.
- E1 agrupó las lecturas de productos en compras y POS; E2/E3 aprovecharon acuses válidos de compra y apertura de caja, y redujeron invalidaciones de Excel. RC1 reforzó actor, estado contradictorio de ticket, tipo de producto tras bloqueos y progreso de importación. Conservar esos cambios y sus regresiones.
- CPE/SUNAT sigue `FISCAL_PREPARATION_PENDING`, desactivado. No mostrar ticket como boleta/factura fiscal ni agregar un flujo de facturación electrónica en este trabajo.

### Lectura inicial mínima

Leer `AGENTS.md`, `frontend/AGENTS.md`, las secciones RC1 de `docs/CURRENT_STATE.md`, los apartados relevantes de `SPEC.md`, `ADR.md` y de los planes UX previos solo cuando resuelvan una decisión. Usar el diff y `rg` focalizado (por ejemplo `checkbox|setReviewed|setConfirmed|acknowledge|ConfirmDialog|<Steps` en `frontend/src`, excluyendo tests para el inventario). No leer todo el repositorio ni los planes históricos de principio a fin. GitNexus solo si su licencia comercial está autorizada y el índice corresponde al código actual; preferir git/rg. No instalar herramientas. Preservar cambios locales del operador, especialmente CI.

## 2. Política de confirmaciones: eliminar fricción, no control

Inventariar casillas por significado, no por apariencia. Cada candidato a eliminación debe indicar la acción que protege, el mecanismo que la sustituye, su contrato real y su prueba. Si existe una exigencia de negocio, legal o de seguridad que requiera una declaración distinta de ejecutar la acción, conservarla hasta decisión expresa. Nunca convertir la revisión en un `true` fijo ni enviar una declaración no realizada por el usuario.

| Categoría | Interacción objetivo | Ejemplos |
|---|---|---|
| Dato real / selección | Conservar checkbox, radio o selector apropiado | Sucursales asignadas, presentación activa, opciones de filtrado |
| Guardado ordinario | Formulario válido + botón explícito, sin casilla de revisión ni modal adicional | Precios, stock mínimo, cambios ordinarios de catálogo |
| Operación comercial habitual | Resumen integrado + único botón de compromiso; no checkbox de «revisé» ni modal de confirmación encima de otro | Cobro, recepción habitual de compra, apertura de caja |
| Operación sensible o irreversible | Una confirmación contextual cuando sea necesaria; reutilizar diálogo existente o formulario de compromiso, no ambos | Anulación, cierre de caja, ajuste de stock, despacho/recepción física, suspensión, revocación, importación masiva |
| Declaración independiente | Mantener la evidencia de revisión cuando tenga significado propio; evitar pedirla dos veces | Verificación de la identidad Google que acepta una invitación, consentimiento exigido por SPEC |
| Incertidumbre / recuperación | Mantener bloqueo de reenvío, consultar intención original, decisión explícita si falta evidencia | Respuesta perdida, trabajo Excel heredado sin clave, conciliación de caja/POS/transferencia |

- El botón describe el efecto: `Guardar precios`, `Cobrar S/ …`, `Confirmar recepción`, `Anular venta`, `Despachar S/ …`, `Desactivar empleado`; evitar «Sí», «Aceptar» o «Continuar» ambiguos. Para los casos sensibles, mostrar entidad, sucursal, monto/cantidad y consecuencia. Motivo obligatorio cuando el contrato lo requiere.
- No añadir una confirmación para compensar la eliminación de un checkbox que no aportaba información. Una confirmación final dentro del formulario es suficiente si incluye el efecto y respeta la política de riesgo. No usar modales apilados.
- En operaciones físicamente verificadas, conservar la distinción real entre revisar datos en pantalla y recibir dinero/mercadería. El botón de compromiso debe expresar la recepción/despacho real; no inferir que fue realizada solo porque se abrió el formulario. Mantener cualquier afirmación independiente exigida por el contrato.
- No autoconfirmar, autocomprar, autocobrar, autodespachar ni autoaprobar cuando cambian datos, se elige una opción o se escanea un código. Nunca emitir escrituras desde un efecto de montaje. No hacer que Enter global ejecute una acción destructiva por accidente.
- Solo habilitar acción con datos válidos, contexto vigente, permisos observados, versión requerida, conectividad y sin otra intención pendiente. El backend sigue siendo la autoridad; no usar disabled como control de autorización.

## 3. U1 — POS, caja y compras (camino crítico diario)

### U1.1 Cobro y anulación POS

**Código:** `features/pos/PosPage.tsx`, `SaleCartPanel.tsx`, `SaleDraftPanel.tsx`, `SaleCheckoutPanel.tsx`, `CheckoutSummary.tsx`, `useSaleCheckout.ts` y controlador de borrador existente.

Se observó `reviewed` en el formulario de pagos (`SaleCheckoutPanel.tsx`): la casilla habilita «Confirmar venta y registrar pagos» y se desmarca al editar importes. Sustituir por resumen siempre visible de total, medios e importes, validación exacta y un botón final explícito. Mantener edición del carrito, borrador/versionado, reglas de stock, pertenencia de caja y vendedor, prevención de doble envío y modalidad mixta. Si la API actual no admite efectivo entregado/vuelto como dato persistido, distinguirlo del importe aplicado al pago; no cambiar el contrato para añadir vuelto ni usar floats. No introducir una calculadora separada innecesaria.

Usar foco en búsqueda/lector y navegación por teclado. Mantener un acceso a cobrar que sea claro, sin duplicar formulario. Si hay un modal de cobro, su submit es la confirmación final. En anulación conservar motivo, importe, consecuencia y acción destructiva explícita; evaluar eliminar solo la casilla de revisión redundante, no la confirmación deliberada ni el flujo de reversión. Si el ticket discrepa del estado local, no mostrar éxito anterior ni permitir anulación con estado stale; conservar la consulta de reconciliación. Ticket interno disponible tras confirmación, impresión optativa, continuar vendiendo sin imprimir. No forzar imprimir ni esperar consultas secundarias para permitir un nuevo carrito si la operación anterior está terminalmente resuelta.

**Regresiones:** un POST por intención; clics repetidos/Enter mientras pendiente, pérdida de respuesta, GET fallido tras confirmación, estado de ticket contradictorio, pago mixto/decimal, caja/sesión ajena, anulación y cambio de identidad. Eliminar checkbox no debe cambiar los datos enviados.

### U1.2 Caja

**Código:** `features/cash/CashPage.tsx`, `CashSessionPanel.tsx`, `useCashSession.ts`, `useCashClose.ts`.

Apertura: introducir monto y confirmar; ya existe un formulario de apertura sin checkbox, no añadir otro. Conservar acuse validado por tenant/sucursal/membresía/actor, cancelación de lecturas antiguas y actualización de la clave exacta de caché. Cierre: mostrar efectivo contado, caja y resumen de conciliación; expresar claramente que el cierre termina la sesión. Una sola acción consciente, sin casilla repetida ni confirmaciones encadenadas. Si el acuse de cierre es definitivo y falla la lectura de recibo, mantener estado cerrado y recuperación del comprobante, jamás volver a habilitar el cierre sobre la misma sesión. No calcular saldos autoritativos en frontend.

**Regresiones:** apertura sin GET redundante de éxito, error de respuesta, cierre con lectura fallida, otro operador/sucursal, importe exacto, sesión cerrada desde otra pestaña y revisión posterior. Conservar la consulta necesaria cuando el acuse no es suficiente.

### U1.3 Compra y recepción

**Código:** `features/purchasing/PurchasesPage.tsx`, `ReceiptEditor.tsx`, `PurchaseReview.tsx`, `PurchaseLookup.tsx`, `SuppliersPanel.tsx`, `usePurchaseReceipt.ts`, `api.ts`.

`PurchaseReview` exige marcar que se revisó estado persistido y mercadería recibida físicamente. Sustituir la casilla redundante, cuando SPEC lo permita, por resumen de proveedor/documento, líneas, unidades, cantidades, costo, lotes/vencimientos y botón `Confirmar recepción de mercadería`. El botón constituye la declaración de recepción; no permitir confirmar por seleccionar un producto. Mantener crear borrador y editar/guardar líneas como estados técnicos cuando los contratos los requieran; se pueden presentar como una misma pantalla sin esconder un nuevo POST inesperado. Evitar pedir campos opcionales y navegar a otro formulario para completar una sola tarea. No ocultar líneas no guardadas ni confirmar una versión distinta de la revisada.

Conservar acuse terminal de compra validado por sucursal, ID, estado, fecha y actor; mantener revisión y líneas persistidas. No volver a introducir GET de detalle en camino exitoso solo para alimentar la UI. Si hay acuse inválido, recuperación del borrador o resultado incierto, conservar clave/intención, lectura autorizada y bloqueo de nuevo envío. Una lectura prematura en borrador no prueba rollback. Mantener búsqueda y creación de proveedor separados cuando su responsabilidad es distinta; mejorar el acceso contextual sin crear automáticamente un proveedor.

**Regresiones:** revisión exacta, stock solo al confirmar, edición concurrente, acuse de otro actor, pérdida de respuesta, mismo UUID, no duplicación, precisión de cantidades y lotes. Pruebas de ahorro de GET sobre el camino ya optimizado.

### U1.4 Revisión de navegación y foco del flujo diario

Comprobar que desde la operación habitual se pueda buscar, seleccionar, modificar, confirmar y seguir trabajando sin visitar páginas intermedias innecesarias. Mostrar solo acciones disponibles al rol y estado. No mezclar módulo de ventas con administración de stock ni añadir acciones que el rol no tiene. Mantener selección explícita de sucursal cuando exista ambigüedad. Revisar en desktop 1366×768, pantalla pequeña y teclado; no declarar Chrome PASS si solo se ejecutan tests DOM.

## 4. U2 — Catálogo, inventario y movimientos

### U2.1 Catálogo y precios

**Código:** `features/catalog/ProductsPage.tsx`, `CreateProductForm.tsx`, `EditLocalProduct.tsx`, `ProductDetailPanel.tsx`, `ProductLookup.tsx`.

`ProductDetailPanel` exige confirmación de precios, activación/desactivación y conjunto de presentaciones. Eliminar casilla de guardar precios si formulario válido, mostrar diferencia antes/nuevo y efecto en ventas futuras. Mantener activación/desactivación explícita con consecuencia (no cambia historial). Presentaciones: conservar checkbox de actividad como dato, no confundirlo con `reviewed`. Mostrar versión original, cambios y conflicto de revisión; quitar confirmación redundante sin permitir reemplazar la configuración por una lectura tardía. Factores históricos inmutables, desactivación y nuevas unidades según contrato actual. No sincronizar cada refetch con el borrador local. No clonar formularios de datos comunes.

### U2.2 Inventario

**Código:** `features/inventory/InventoryForms.tsx`, `InventoryPage.tsx`.

Eliminar checkbox de `Configurar stock mínimo`, manteniendo sucursal/producto y validación decimal. Ajuste de lote: conservar lote, vencimiento, signo entrada/salida, motivo obligatorio, cantidad y consecuencia; presentar un único resumen/confirmación de registrar movimiento. No permitir ajuste negativo incompatible ni mostrar stock inventado. Evitar obligar al operador a recordar IDs: utilizar etiquetas de negocio de los recursos ya autorizados. No cambiar cantidades, reglas de lotes o endpoint por UX.

### U2.3 Mercadería

**Código:** `features/transfers/RequestGoods.tsx`, `DispatchGoods.tsx`, `ReceiveGoods.tsx`, `GoodsDetail.tsx`, `TransfersPage.tsx`, `TransferRecovery.tsx`.

Solicitud contiene asistente origen/destino, productos y revisión, más casilla final. Reducir pasos de presentación cuando los datos caben en una pantalla usable; destino fijo del contexto visible, origen explícito, líneas editables y acción `Enviar solicitud`. No enviar si queda una línea sin agregar o la selección es inválida. Conservar autorización de cada sede y estados de solicitud/aprobación (si aplica), despacho y recepción como transiciones reales separadas. No fusionar acciones de origen y destino ni automatizar una entrega física. Despacho: conservar asignación de lotes, cantidades y vencimientos; recepción: cantidades recibidas reales y diferencias, sin rellenar automáticamente como si se hubiera contado. Un botón contextual final por transición sustituye una casilla solo si no se pierde declaración requerida. Mantener revisión de cada etapa, idempotencia de creación y recuperación de operaciones inciertas.

### U2.4 Sencillo

**Código:** `features/transfers/CashTransfersPage.tsx`, `CashSelection.tsx`, controlador de transferencias existente.

Las casillas `reviewed` y `receiptReviewed` están vinculadas a firmas de importe/destino/caja. Quitar redundancia visual solo conservando el vínculo con el contexto actual: una confirmación contextual debe corresponder a la caja, sucursal, monto y versión/intención revisados. Si cambian los datos de una confirmación abierta, invalidarla o volver a mostrar resumen. Mantener despacho y recepción separados, recepción completa conforme al contrato y caja destino válida. No ocultar dinero en tránsito, ni inferir recepción por haber realizado el despacho. Mantener límite y saldo del servidor, claves originales y un solo efecto en ledger.

### U2.5 Regresión cruzada

Validar catálogo/stock/búsqueda después de guardar, recibir y transferir, sin invalidar todas las consultas de la aplicación. No introducir nuevas fuentes de verdad. Verificar actor/tenant/sucursal, stock/lotes, versiones y rechazo de datos stale; no degradar trazabilidad por quitar pasos visuales.

## 5. U3 — Administración, configuración y tareas periódicas

### U3.1 Alta de botica y licencia

**Código:** `features/platform/NewTenantDialog.tsx`, `PlatformPage.tsx`, `PlatformTenantDetails.tsx`, `PlatformRecovery.tsx`, `features/settings/TenantOnboardingPage.tsx`.

`NewTenantDialog` usa tres pasos y checkbox negocio/Gmail/licencia. Evaluar formulario compacto (nombre, Gmail del Owner, límite de sedes y resumen Trial) con una acción final clara. Si móvil o dependencias requieren pasos, conservar los indispensables. La designación debe mostrar el Gmail exacto y efecto; no autocompletar con identidad de otro tenant, no eliminar revisión de identidad crítica. Mantener acuse recuperable y autorización de plataforma, reclamo del Owner mediante Google verificado, licencia y separación entre Superadmin/negocio. Suspensión/reactivación y cambios de licencia: mostrar efecto y confirmación contextual cuando corresponda, sin confirmaciones duplicadas. No convertir alta y entrada Google en un flujo automático que conceda permisos sin el reclamo.

### U3.2 Personal e invitaciones

**Código:** `features/employees/EmployeeForms.tsx`, `EmployeeInvitationsPanel.tsx`, `EmployeesPage.tsx`, `AcceptInvitationPage.tsx`.

Conservar checkboxes de sucursales como selección real. Eliminar casilla `reviewed` para reemplazar asignaciones/horario si el resumen y el botón muestran claramente la modificación; si deja al empleado sin acceso, mostrar la consecuencia y confirmar de manera proporcional. Mantener horarios reales, revisión vigente y revocación. Desactivar empleado: confirmación explícita con persona y efecto. Crear invitación: rol, enlace temporal y explicación breve; evitar confirmar dos veces lo mismo. Aprobar incorporación: preservar verificación independiente de la identidad Google aceptante, rol y autorización; no sustituir la comprobación humana por un valor por defecto. Si SPEC exige constancia independiente, mantenerla. No mostrar enlace sensible en dos lugares; preservar expiración, copia y recuperación de RC1. No conceder acceso al copiar/abrir enlace. 

### U3.3 Comisiones y configuración

**Código:** `features/commissions/RuleForm.tsx`, `CommissionsPage.tsx`, `features/settings/SettingsPage.tsx`.

Mostrar producto, valor, vigencia y efecto de reemplazar la regla anterior; acción final de guardar o aplicar regla. Eliminar checkbox de revisión redundante si la misma confirmación expresa el efecto. Conservar validación decimal, versiones/estado de regla, auditoría y límites, sin recalcular comisiones históricas. En configuración y onboarding, mantener campos obligatorios reales y agrupar controles relacionados; no exigir terminar datos opcionales para utilizar funciones habilitadas.

### U3.4 Importación Excel

**Código:** `features/product-import/ImportPage.tsx` y cliente API existente.

Simplificar a seleccionar archivo válido, resumen de nombre/tamaño/tenant y acción explícita `Importar productos`. La plantilla es ayuda opcional cuando el usuario ya dispone de un archivo válido, no un requisito de haberla descargado. La carga crea productos: mantener una confirmación masiva clara, sin checkbox y diálogo duplicados. Mostrar progreso y resultado persistido (importados, inválidos, parciales) directamente; no exigir volver a teclear el ID reconocido. Conservar el bloqueo mientras la intención original no esté reconciliada. Para trabajo terminal correctamente identificado, ofrecer cierre de revisión explícito y directo, sin exigir clic extra que no aporte evidencia. Mantener revisión especial de legado sin clave, errores, trabajo interrumpido y resultados parciales. Cierre Owner de trabajo interrumpido: conservar advertencia de efectos, permiso, flag de backend y una confirmación explícita que conserva filas/productos. Nunca habilitar la recuperación si el servidor no la permite. No reenviar XLSX automáticamente ni resetear estado pendiente por cerrar modal. Conservar invalidación de catálogo por progreso real, no por cada página ni lectura atrasada.

### U3.5 Inicio, reportes, auditoría, notificaciones

**Código:** `features/reporting`, `features/dashboard`, `features/audit`, `features/notifications`.

Mostrar datos esenciales primero, filtros contextualizados y valores por defecto seguros conforme a fechas/sucursal/rol vigentes; no pedir aplicar o confirmar un filtro local inocuo cuando puede cambiarse directamente sin saturar API. Debounce solo donde corresponda, filtros de exportación claramente visibles, no descargar automáticamente. Mantener límites de paginación, filtros servidor, auditoría y precisión. Reusar contador de notificaciones existente y carga por alcance. No convertir reportes en una segunda fuente de verdad de saldos. No rediseñar todos los gráficos ni agregar nuevos reportes.

## 6. U4 — Arquitectura React, alineación backend y experiencia transversal

### U4.1 Estado mínimo y reutilización

Mantener dirección `app → features → shared` y API pública de cada feature. Eliminar `reviewed`, `confirmed` y firmas de revisión únicamente donde su propósito se haya reemplazado de forma equivalente. No duplicar estado derivable (`isValid`, totales, flags de presentación) ni sincronizar con efectos a cada render. Usar estado local para borradores, TanStack Query para estado remoto, URL para navegación/recuperación ya definida. No extraer hooks o componentes genéricos solo por similitud textual. Reutilizar `Button`, `Modal`, `ConfirmDialog`, `FormLayout`, `Notice`, `States`, `NavigationSafety`, `Steps` cuando aporten significado. Mantener estados de error y recuperación explícitos. No borrar dirty/abort/pending/mounted sin demostrar equivalencia. Evitar refs de estado como bypass de validación.

### U4.2 Contratos, caché y red

Comparar secuencia de HTTP normal y excepcional antes/después por módulo tocado. El acuse terminal completo y válido puede actualizar la vista o caché exacta; si no incluye evidencia suficiente o hay cambio concurrente, consultar el recurso correcto. Mantener comprobación de identidad/ámbito, expectedVersion/revision, exactNumbers y cliente HTTP común, cookies same-origin HTTPS y CSRF. No reemplazar la respuesta autoritativa por optimistic success de dinero/stock/roles. No cambiar POST/PUT existentes para ahorrar clics. Cada escritura debe corresponder a una intención real; no usar efectos, retries automáticos o refetch para repetir mutaciones. No confiar en GET 404 como prueba de rollback.

Agrupar invalidaciones solamente de los recursos afectados; conservar actualizaciones necesarias de stock, caja, ticket, reportes y notificaciones. Cancelar lecturas antiguas si pueden pisar acuses. No cambiar `staleTime` global ni introducir caché de permisos sin análisis. No remover polling/refresh explícito necesario para estados externos. Mantener claves que distinguen identidad, tenant y sucursal y limpieza de ámbito al cerrar sesión. Mantener recarga y navegación al recuperar una operación.

### U4.3 Formularios y mensajes

Un formulario, una acción primaria por contexto; secundarios claros, sin repetir bloques de error, avisos o instrucciones ya visibles. Mostrar errores inline cerca del campo y una explicación accionable de la API, evitando SQL, UUID técnicos y jerga cuando no son necesarios. Identificadores de soporte disponibles en detalle, no exigidos en flujo normal. Los textos deben usar español del Perú y moneda PEN; mantener exactitud decimal del contrato, fechas y hora de Lima donde el módulo lo define. Campos opcionales claramente marcados. Los botones no desaparecen o cambian de lugar inesperadamente durante carga. Diferenciar `Guardando…`, `Confirmado`, `No realizado` y `Resultado pendiente de verificar`. Mantener protección de cambios no guardados y recuperación; no pedir confirmación de abandono si no hay cambios reales.

### U4.4 Accesibilidad y rendimiento percibido

Verificar etiquetas de campos, errores asociados, orden de tabulación, foco inicial/restauración en modal, escape seguro, región viva para progreso/resultado y contraste. No ocultar texto por estética, no usar solo color para estados. En POS, priorizar teclado, lector y acciones grandes, evitando atajos globales que interfieran con inputs o confirmaciones. En móvil, conservar navegación, botones, tablas/scroll y diálogos utilizables; no crear una segunda aplicación. Reusar skeleton/estados existentes sin bloquear toda la pantalla por consultas secundarias. Medir bundle o renders solo si hay problema concreto. No introducir virtualización o memoización indiscriminada.

### U4.5 PWA y recuperación: límites no negociables

**Código:** `features/offline`, `app/OfflineBoundary.tsx`, `WorkspaceOutboxSync.tsx`, hooks de recuperación compartidos. La ruta normal no debe mostrar manuales técnicos de contingencia, pero si hay pérdida de red mostrar qué está autorizado, qué queda pendiente y qué requiere volver a conectar. No cambiar alcance offline, claves, IndexedDB, Web Locks, inmutabilidad de eventos, revocación o cuarentena. Mantener estados terminales frente a respuestas tardías y rechazo de constancias ajenas. No tratar almacenamiento local como confirmación de servidor. Probar dos pestañas y cambio de identidad según capacidad del entorno; documentar los casos no ejecutados, no simular aceptación real.

## 7. U5 — Pruebas, medición y candidato final

### U5.1 Baseline y presupuesto de interacción

Antes de editar, seleccionar recorridos representativos por rol y registrar los pasos que efectivamente existen en el código/tests. Si hay entorno Chrome aislado autorizado, medir clics/teclas, pantallas/modales, checkboxes de revisión, peticiones GET/POST/PUT, tiempo de tarea y errores para mismos datos. Si no existe, usar tests de transporte y walkthrough de código etiquetados como `ESTIMACIÓN_DE_INTERACCIÓN` / `MEDIDO_EN_TEST_DOM`, nunca como medición humana. No exigir otra corrida integral únicamente para reconstruir la evidencia histórica de 592 tests si el árbol y entorno son comparables; repetir baseline focalizado al surgir un fallo ambiguo.

Objetivos orientativos (no SLA ni resultados ya medidos): quitar al menos una interacción obligatoria de cada operación donde se elimine un checkbox; 0 checkboxes de «revisé» sin significado independiente en el camino normal; máximo una confirmación de compromiso por transición; cero escrituras extra por refactor; sin aumento de GET ordinarios sin razón demostrada. Reducir pasos de asistentes solo donde conserve comprensión, contexto y permisos. No prometer una venta completa en un número fijo de clics porque depende del carrito, medios y autorización. El resultado debe favorecer tareas habituales y no penalizar la recuperación excepcional.

### U5.2 Regresiones focalizadas por grupo

Ejecutar tests afectados después de cada grupo. Usar Testing Library por rol/nombre accesible y `user-event`; esperar resultado observable (`findBy`, `waitFor` sobre condición real), no sleeps ni timeouts arbitrarios. Contrastar request payload, scope, versión, cantidad de POST y recuperación. Actualizar aserciones de checkbox/labels solo cuando se sustituya por una interacción equivalente; preservar las pruebas negativas y de integridad de 592 tests RC1. No convertir un fallo en skip, todo, test eliminado o selector frágil. Si se fusionan casos realmente duplicados, justificar cobertura equivalente y diferencia del inventario. No bajar número de workers para ocultar una carrera; conservar dos workers RC1 salvo evidencia clara y autorización.

### U5.3 Gate frontend final

En `frontend/`, usar Node aprobado (RC1 24.18.1) y lockfile actual. Ejecutar `npm run build`, `npm run lint`, `npm run test` completos una vez al finalizar los cambios, y `git diff --check`. Mantener 0 FAIL, 0 SKIP y pruebas realmente ejecutadas. Repetir solo casos intermitentes/impactados y, si falla la completa, reproducir y corregir la causa antes de volver a ejecutarla. No cambiar dependencias ni snapshots de API para obtener PASS. Registrar comandos, entorno, conteos, SHA/base y diff. Mantener la evidencia de frontend de RC1 como histórica, no como validación del nuevo candidato. El workflow integral publicado por el operador certificará backend y frontend sobre el mismo SHA final, sin desactivar NuGet Audit ni PostgreSQL real. No ejecutar suites .NET locales por modificaciones únicamente visuales; si se detecta necesidad de cambio de contrato, dejarla bloqueada para revisión.

### U5.4 Aceptación operativa

Reutilizar guion RC1 de `CURRENT_STATE.md`: A Superadmin, B y C Owners de dos boticas Trial; trabajadores según rol y sede; datos sintéticos y cuentas autorizadas, perfiles Chrome separados. Ejecutar alta/reclamo, licencia/roles, catálogo/stock mínimo/unidades, compra, apertura/cobro/ticket/anulación/cierre, transferencias de bienes y efectivo, personal, Excel, reportes y PWA con pérdida de respuesta/dos pestañas. Verificar aislamiento, no duplicación, saldos, cantidades, versiones y resultado visible. Comparar tiempo de tarea, pasos, clics, red y errores en desktop/móvil/teclado. No utilizar DEV ni producción sin autorización expresa; no aplicar migración ni resetear datos para montar la prueba. Si falta entorno o cuentas, preparar el guion y reportar `PENDIENTE_ACEPTACION`, no detener correcciones independientes.

### U5.5 Informe de salida y congelación

Actualizar una sección `UX-LEAN` en `docs/CURRENT_STATE.md` (no crear un nuevo informe por módulo). Entregar una tabla compacta U1–U5 con estado `IMPLEMENTADO`, `PROBADO_LOCAL`, `MEDIDO`, `PENDIENTE_ACEPTACION`, `BLOQUEADO` o `SIN_CAMBIO_JUSTIFICADO`, y evidencia. Incluir inventario de confirmaciones retiradas/conservadas con motivo, comparación antes/después por flujo (pasos, clics, checkboxes, HTTP, fuente de medición), archivos, tests y resultados, riesgos pendientes, contrato intacto o cambio propuesto no aplicado. Identificar cualquier regresión real que impida publicar. No afirmar «listo para producción» únicamente por tests DOM. Concluir con GO/NO-GO para publicar el candidato de pruebas y para aceptación; piloto sigue sujeto a certificación exacta, rendimiento, Chrome/PWA, recuperación, operación y elegibilidad tributaria.

**Fin del encargo:** detener desarrollo cuando los cambios de UX estén verificados localmente. No comenzar nuevas fases, cambiar stack ni rediseñar módulos aceptados. Eliminar solo complejidad que pueda demostrarse redundante. Preservar los mecanismos de recuperación y control que ya hicieron confiable al backend.

## 8. Resumen de criterios de producto

| Flujo | Experiencia objetivo | Control que permanece |
|---|---|---|
| Vender | Escanear/buscar → carrito → medios → cobrar → ticket/continuar | Pago exacto, caja, stock, versión, una intención y conciliación |
| Comprar | Documento/proveedor → líneas → confirmar recepción | Borrador, revisión, lotes, ingreso de stock único |
| Caja | Monto inicial → abrir; contado → cerrar | Sesión, actor, ledger y cierre irreversible |
| Catálogo | Buscar → editar → guardar | Datos válidos, versiones e historia |
| Inventario | Producto/lote → cambio → guardar o registrar ajuste | Motivo, cantidades y auditabilidad |
| Transferir | Solicitar → despachar → recibir según rol | Custodia separada y cantidades físicas reales |
| Personal | Elegir persona/rol/sedes/horario → guardar | Identidad verificada, permiso y revocación |
| Excel | Elegir archivo → importar → resultado | Efecto masivo explícito, parcialidad e intención original |
| Alta botica | Datos esenciales y Owner → crear → reclamo | Gmail verificado, licencia, plataforma aislada |
| PWA | Operar dentro del alcance y conciliar al reconectar | Identidad, inmutabilidad, deduplicación y revocación |

**Decisión de diseño propuesta:** simplificar confirmaciones mediante botones explícitos y resúmenes integrados, conservar declaraciones independientes y transiciones de custodia; mantener diseño y backend actuales. Esta propuesta requiere contraste con SPEC y pruebas por flujo. No representa una auditoría ejecutada en Chrome ni autoriza producción.
