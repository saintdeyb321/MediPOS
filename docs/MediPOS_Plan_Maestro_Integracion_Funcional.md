# MediPOS — Plan maestro de integración funcional y flujo operativo

**Versión de planificación:** 2026-10-16  
**Fuentes consultadas:** `MediPOS_Documento_Maestro_Arquitectura_Requisitos.pdf` (secciones 6.7, 6.8, 9.1 y 21), `docs/SPEC.md`, `docs/FRONTEND_INTEGRATION_PLAN.md`, contratos HTTP de `backend/src/MediPOS.Api/Tenant/` y frontend publicado en GitHub (`ffb668e9d4190109d366ee1b98bbc6e29af57b95`).  
**Prioridad:** entregar el MVP de botica operable, con UI provisional simple y negocio/seguridad gobernados por backend. **No** certificar producción ni modernizar la UX todavía.

> **Límite de visibilidad:** este documento describe el repositorio publicado y los resultados locales informados, no inspecciona ni modifica los archivos/procesos de Windows del operador. Antes de editar, el agente debe leer el estado local actual y fusionar los hallazgos con `docs/FRONTEND_INTEGRATION_PLAN.md`, sin perder progresos recientes.

## 1. Decisión de producto: POS simple para el cajero

**Recorrido normal y único de venta**

1. Sesión OIDC, licencia/rol/horario/sucursal válidos; consultar caja propia y **abrir caja con monto inicial** si aún no existe.
2. Buscar productos por texto/código/principio activo; consultar precios y disponibilidad por sucursal.
3. Agregar presentaciones y cantidades al carrito; editar o quitar líneas; mostrar subtotal y total con formato comercial claro.
4. Pulsar **Cobrar**. El sistema guarda de forma segura la venta *internamente* (borrador/versión y líneas) y presenta métodos de pago efectivo/Yape/Plin/tarjeta/transferencia, incluidos mixtos.
5. Validar que pagos sumen exactamente el total autorizado del servidor y ofrecer revisión final. Solo con confirmación explícita del vendedor ejecutar el POST definitivo de venta. En confirmación exitosa, backend crea pagos, movimientos/lotes FEFO, comisiones/auditoría según sus reglas.
6. Mostrar pantalla de **Venta realizada** con resumen e identificador seguro y **dos opciones principales:** «Imprimir ticket interno» y «Seguir vendiendo sin imprimir». Acceso secundario a «Ver ticket» o «Consultar venta» para recuperación. No abrir automáticamente el diálogo de impresión.
7. «Seguir vendiendo» crea un carrito vacío en la misma caja abierta y no repite ningún POST de venta. Impresión opcional del ticket interno, con 58/80 mm **solo donde esté implementado y verificado**.

**Regla clave:** `SaleDraft`, `expectedVersion`, reconciliación e idempotencia siguen existiendo **internamente** para integridad y recuperación, pero el vendedor no tiene por qué pulsar «Crear borrador», «Guardar líneas», copiar UUID ni manejar versiones. Únicamente una vista secundaria de «Recuperar operación» puede exponer estados técnicos humanizados en casos anómalos. No eliminar ni saltarse los endpoints `POST /sales`, `PUT /sales/{id}/lines`, `POST /sales/{id}/confirm` ni sus controles.

**No confundir:** ticket interno ≠ comprobante de pago tributario (CPE). En LAB sin SUNAT se permite «Imprimir ticket interno» o «Sin imprimir»; no ofrecer «Boleta/Factura electrónica emitida» hasta que el adaptador, la autorización fiscal y el contrato real estén validados.

## 2. Hallazgos de código que deben corregirse primero

| Hallazgo publicado | Impacto | Corrección mínima |
|---|---|---|
| `PosPage.tsx` monta `CashSessionPanel`, `SaleCartPanel`, `SaleDraftPanel` y `SaleCheckoutPanel` como paneles separados | Recorrido excesivamente técnico y disperso | Un orquestador de POS y pasos condicionales. Caja al inicio; carrito; cobro; resultado final. Mantener hooks existentes y sus controles. |
| `SaleDraftPanel.tsx` exige botones de «Crear borrador» y «Guardar líneas» | El cajero administra infraestructura de ventas | Orquestar acciones tras «Cobrar» usando comandos existentes y confirmación explícita. Reconciliar resultados inciertos sin duplicar venta. |
| `SaleCheckoutPanel.tsx` combina «Cobro y ticket interno» y muestra opciones técnicas | Tras confirmar, la impresión debe ser opcional y posterior | Vista final de éxito con las dos opciones visibles y ticket recuperable por GET. |
| `shared/api/decimal.ts::decimalLabel` conserva los ceros finales originales | Stock `20.000000000000`, precio `S/ 1.0000` | Nuevo formato de **presentación**, sin convertir importes a `Number` ni cambiar JSON exacto. S/ con 2 decimales **si la regla de precio acepta 2 en UI**; stock entero sin decimales, fracciones significativas sin ceros sobrantes; políticas de redondeo y precios con 4 decimales deben revisarse contra reglas de negocio. |
| `InternalTicketPanel.tsx` tiene `window.print()` y CSS principal 80 mm | Falta elección clara después de vender y validar 58 mm | Asegurar vista separada; ancho 58/80 solo si se implementa y prueba. Nunca etiquetar como SUNAT. |

### Formato numérico comercial

- `JsonNumber`/decimal exacto permanece la fuente de cálculos; no truncar ni redondear cantidades/valores al enviarlos al servidor.
- **Presentación:** `20.000000000000` → `20`; `1.0000` → `1` para cantidades enteras; `S/ 1.0000` → normalmente `S/ 1.00`, sujeto a las reglas de tarifa de MediPOS. Si una tarifa válida tiene precisión superior a 2 decimales, mostrarla explícitamente en contexto de tarifa/precio por unidad base y no ocultar diferencias que puedan afectar el total.
- Nunca convertir múltiples líneas a `Number`, ni aceptar una suma de pagos que difiera del total persistido.
- Mostrar precio **por presentación** cuando se elija caja de 10: base S/ 1.00, caja de 10 S/ 10.00 para el piloto conocido. Resultado de línea y total final vendrán del backend al confirmar.

## 3. Mapa maestro: cobertura por módulos

Estados: `DONE` solo tras integración + aceptación aplicable; `CODE_READY / PENDING_ACCEPTANCE` para funcionalidad escrita sin recorrido comercial; `PARTIAL`, `PENDING`, `BLOCKED` cuando corresponda.

| Bloque | Alcance / integración API | Estado auditado | Entrega visible para operador | Pruebas de cierre |
|---|---|---|---|---|
| F00 Autenticación y selección | Google, sesión, boticas, sucursales, `/access` | DONE funcional LAB Owner, no representa todas las autorizaciones | Acceso según rol/sucursal/licencia | Login, cambio sucursal, expiración/revocación |
| F01 **Venta rápida y caja** | GET/POST caja, POST borrador, PUT líneas versión, POST confirmar, GET ticket | **CODE_READY / PENDING_ACCEPTANCE** | Caja inicial → vender → cobrar → confirmada → imprimir/no imprimir → seguir vendiendo | 1 recorrido real autorizado + conciliación de stock/pagos; no duplicación |
| F02 Cierre y anulación | `/cash-sessions/{id}/close`, reconciliación, `/sales/{id}/void`, cajas activas | PENDING | Cerrar caja/arqueo, consultar/anular autorizadamente | Reversiones, pago por método, conteo, auditoría |
| F03 Productos/catálogo | categorías, global/local, unidades, precios, estado, búsqueda | Búsqueda DONE en LAB; pantallas administrativas PENDING | Buscar, listar, crear/editar catálogo permitido | Inventario visible, equivalentes estructurados sin inferencias terapéuticas |
| F04 Compras/inventario | proveedor, compra, líneas, confirmar; existencias, lotes, ajuste | Backend funcional, UI parcial | Compra/lotes/FEFO/alertas/ajustes | Entrada + salida lote, fecha, movimientos, autorización |
| F05 Empleados y sedes | empleados/roles/horarios/asignaciones y sucursales | PENDING UI | Alta/asignación/revocación de trabajadores OIDC reales | Dueño/QF/Cajero y horarios, denegación cruzada |
| F06 Transferencias | solicitud→aprobar→despachar→recibir/anular | PENDING UI | Trasladar mercadería entre LAB Centro y Norte | Trazabilidad lote origen/destino y permisos |
| F07 Gestión y reportes | reportes, exports, comisiones, dashboard, auditoría | PENDING UI | Dashboard básico del dueño; tablas y filtros | Totales congruentes con ventas/stock; exportación según contrato |
| F08 Configuración, notificaciones, importaciones | licencia, entidades, notificaciones, imports, alertas | PENDING UI | Pantallas simples con APIs públicas existentes | Validar tipos/archivos y estados; no crear rutas ficticias |
| F09 Offline/PWA y flujos diferidos | Sync/PWA, contingencia, SUNAT opcional separado | BLOCKED/SEGÚN ALCANCE | Implementar solo contra requisitos/gates autorizados | A14/A20 y pruebas multidispositivo cuando corresponda |

**Orden de ejecución recomendado:** primero corregir F01 y completar F02. Después F03→F04→F05→F06→F07→F08. Revaluar F09 sin inventar contratos, autorizaciones ni trabajo fiscal. Para cada bloque, comprobar rutas HTTP realmente publicadas; un handler no significa que exista un endpoint público. Este orden no renumera hitos oficiales del roadmap.

## 4. Protocolo para completar sin pruebas manuales infinitas

- **Cada bloque:** inspección focalizada → implementación completa de UI provisional y transporte → lint/build y regresiones nuevas relevantes → corregir → publicar únicamente LAB → aceptación manual **una vez por circuito completo**, no por botón.
- No repetir miles de tests backend cuando no se modificó lógica backend; pero probar siempre contratos, dinero exacto, seguridad, errores y concurrencia del código tocado.
- **No** ejecutar operaciones comerciales desde herramientas. Aperturas de caja, confirmaciones, anulaciones, importaciones y altas reales en LAB requieren acción explícita del operador en Chrome. Evidencia PostgreSQL solo después, mediante lecturas autorizadas.
- No confundir `HTTP 200`, mocks o contenido estático con flujo aceptado. Reportar PASS/BLOCKED honestamente.
- Si falta una autorización de negocio o un endpoint crítico, trabajar en partes no bloqueadas y registrar el impedimento concreto; nunca relajar RLS, CSRF, sesión, Origin, roles, horario o licencia para finalizar una pantalla.
- Handoff de máximo 12 líneas con bloque, archivos, APIs integradas, evidencia y siguiente operación. Fusionar con `docs/FRONTEND_INTEGRATION_PLAN.md`, no crear una segunda fuente de verdad.

## 5. Aceptación integral F01: una venta, no un test por botón

**Precondición:** entrar con Google como Owner autorizado en Chrome LAB, seleccionar LAB Centro, confirmar stock base previo y comprobar si ya existe caja o venta en curso. **No crear duplicados**.

- Abrir caja explícitamente si no existe y consultar su estado.
- Buscar `paracetamol`, elegir PANADOL Antigripal NF y agregar 2 Tabletas + 1 Caja de 10. Si tarifa y stock no variaron, total esperado `S/ 12.00`.
- Pulsar **Cobrar**: sistema guarda internamente borrador/líneas, valida sesión y muestra total persistido y medios de pago. El vendedor NO tiene que operar «Crear borrador» o «Guardar líneas».
- Aplicar medios LAB `S/ 5.00` efectivo + `S/ 7.00` Yape **sin transferir dinero real**. Revisar y confirmar una sola vez; venta, pagos y salida de 12 unidades solo si éxito transaccional.
- Ver pantalla **Venta realizada** y dos opciones: «Imprimir ticket interno» o «Seguir vendiendo». Puede recuperarse el ticket sin repetir confirmación.
- Si el stock previo era 20 y no hubo otros movimientos, el stock posterior debe ser 8. Verificar por lecturas de PostgreSQL LAB la venta única, sus líneas, pagos, caja, lote FEFO, movimientos y auditoría.
- Ante timeout/respuesta incierta: mantener identificador de venta si existe, bloquear nuevas confirmaciones, consultar el estado original. **No** ofrecer «Seguir vendiendo» mientras la transacción esté sin conciliar.
- Cambiar de sucursal o usuario no debe mostrar carrito/caja de otra identidad. DEV intacto, plataforma 404 y sin SUNAT.

## 6. Regla de finalización del MVP funcional

- Ningún módulo `DONE` por tener pantalla o tests de componentes solamente: debe tener contrato, navegación, estados, acciones autorizadas, pruebas focalizadas y aceptación aplicable.
- Auditar conjuntamente reglas P0 y A1–A20 del documento maestro; separar los diferidos mediante decisión explícita, no perderlos.
- Después de F00–F08 y gates pendientes relevantes, ejecutar integración PostgreSQL real, autorización multi-tenant con **dos tenants**, concurrency/idempotencia, recuperación, performance/recovery y aceptación guiada del usuario. No declarar producción lista sin controles externos.
- Solo **después** de estabilizar la funcionalidad: UI/UX final, reducción de clics, responsive, accesibilidad avanzada, navegación teclado/escáner, tickets térmicos, diseño de reportes. Mantener UI provisional limpia y comprensible mientras tanto.

## 7. Instrucción de continuidad a Codex

**Primer objetivo inmediato:** fusionar este contrato operativo en el `docs/FRONTEND_INTEGRATION_PLAN.md` local, aplicar UI de venta rápida (ocultar borrador técnico, venta terminada con impresión opcional), arreglar visualización decimal y completar cierre/anulación de caja y venta cuando esté dentro del alcance; continuar conectando módulos existentes sin exigir pruebas manuales por botones. Usar siempre los contratos locales actuales; no tocar datos DEV, GitHub, migraciones históricas ni seguridad.
