\# PLAN UX/UI MEDIPOS V2

\## Rediseño visual, interacción y consistencia operativa



\## 1. Objetivo



Refactorizar nuevamente el frontend de MediPOS para lograr una experiencia:



\- más intuitiva,

\- más limpia visualmente,

\- mejor jerarquizada,

\- más consistente entre módulos,

\- más cercana a un SaaS profesional moderno,

\- y más fácil de usar durante pruebas manuales reales.



Este plan \*\*no cambia la lógica de negocio del backend\*\*, salvo validaciones de interfaz, estados visibles y ayudas operativas para que el usuario comprenda mejor los flujos.



\---



\## 2. Problemas detectados



\### 2.1 Inicio / Dashboard

\- La sección de indicadores reales de la sucursal aparece desordenada y aplastada.

\- No existe una separación visual clara entre métricas principales, métricas secundarias y contenido analítico.

\- Falta una estructura por bloques, tarjetas y gráficos.



\### 2.2 Punto de venta

\- El layout está mal distribuido.

\- Los espacios y proporciones no se sienten equilibrados.

\- El catálogo visual de productos es pobre y poco intuitivo.

\- Las categorías no destacan correctamente.

\- La caja abierta/cerrada no tiene suficiente visibilidad operacional.

\- Faltan alertas claras, estados destacados y mejor orden de acciones.



\### 2.3 Caja

\- Falta validación visual y operativa para sesiones abiertas durante demasiado tiempo.

\- No hay suficiente señalización de estado crítico.

\- Las operaciones de apertura/cierre deben guiarse mejor.



\### 2.4 Productos

\- El catálogo se siente visualmente débil.

\- Las categorías no están bien representadas.

\- Las tarjetas no tienen una presentación moderna ni uniforme.

\- El comportamiento visual cuando se busca productos no es satisfactorio.

\- Falta mejor agrupación entre listado, filtros, categorías y acciones.



\### 2.5 Reportes

\- Demasiado texto y demasiada lectura.

\- Faltan gráficos.

\- Falta diferenciación clara entre resumen ejecutivo, indicadores y detalle.

\- No existe suficiente “visual analytics”.



\### 2.6 Navegación y submódulos

\- Al entrar a submódulos no hay botón de retroceso o breadcrumb claro.

\- El usuario debe volver a hacer clic en el módulo principal para regresar.

\- La navegación no comunica bien “dónde estoy”.



\### 2.7 Sistema visual

\- Demasiado blanco.

\- Secciones insuficientemente diferenciadas.

\- Tarjetas, botones y bloques no conservan simetría visual consistente.

\- Falta un lenguaje cromático más fuerte y usable.

\- Falta mejor uso de bordeados, sombras, fondos suaves y separación por niveles.



\---



\## 3. Objetivos de la V2



1\. Crear un \*\*sistema visual profesional\*\*, consistente y agradable.

2\. Reestructurar cada módulo usando:

&#x20;  - encabezados claros,

&#x20;  - bloques visuales,

&#x20;  - tarjetas simétricas,

&#x20;  - paneles y modales mejor definidos.

3\. Mejorar el \*\*layout spacing system\*\*:

&#x20;  - separación entre secciones,

&#x20;  - alturas consistentes,

&#x20;  - paddings uniformes,

&#x20;  - grid coherente.

4\. Implementar mejor navegación:

&#x20;  - breadcrumb,

&#x20;  - botón volver,

&#x20;  - estados de submódulo.

5\. Convertir reportes en una experiencia visual:

&#x20;  - KPIs,

&#x20;  - gráficos,

&#x20;  - tarjetas ejecutivas,

&#x20;  - tablas como nivel secundario.

6\. Rediseñar POS y caja como módulos centrales del negocio.

7\. Mantener intactos backend, permisos y contratos API, salvo validaciones visuales del frontend.



\---



\## 4. Principios de diseño



\### 4.1 Claridad primero

Cada pantalla debe mostrar:

\- qué módulo es,

\- qué estoy viendo,

\- qué acciones principales puedo hacer,

\- y cómo regresar.



\### 4.2 Jerarquía visual fuerte

Deben distinguirse claramente:

\- encabezado del módulo,

\- métricas principales,

\- controles,

\- contenido,

\- acciones secundarias,

\- estados críticos.



\### 4.3 Menos texto visible al mismo tiempo

Solo lo necesario debe estar expuesto.

El resto debe ir en:

\- paneles,

\- tabs,

\- modales,

\- drawers,

\- secciones expandibles.



\### 4.4 Consistencia

Los mismos patrones deben repetirse:

\- botón principal,

\- botón secundario,

\- modal de creación,

\- drawer de detalle,

\- tarjeta de resumen,

\- header de módulo,

\- alertas.



\### 4.5 Color funcional

El color no es decorativo únicamente.

Debe ayudar a distinguir:

\- acciones primarias,

\- éxito,

\- alerta,

\- error,

\- estados operativos,

\- tarjetas de resumen.



\---



\## 5. Sistema visual propuesto



\### 5.1 Paleta

Usar una paleta moderna con base clara y acento fuerte.



\- \*\*Primario\*\*: violeta/índigo profesional

\- \*\*Secundario\*\*: azul suave

\- \*\*Éxito\*\*: verde

\- \*\*Advertencia\*\*: ámbar

\- \*\*Peligro\*\*: rojo

\- \*\*Fondos\*\*:

&#x20; - fondo app muy claro

&#x20; - tarjetas blancas o casi blancas

&#x20; - fondos suaves para secciones

\- \*\*Texto\*\*:

&#x20; - principal oscuro

&#x20; - secundario gris azulado

&#x20; - muted para apoyo



\### 5.2 Espaciado

Definir spacing scale:

\- 4

\- 8

\- 12

\- 16

\- 20

\- 24

\- 32

\- 40

\- 48



Usarlo de forma uniforme en:

\- cards,

\- secciones,

\- headers,

\- botones,

\- modales,

\- formularios.



\### 5.3 Bordes y elevación

\- Bordes redondeados consistentes.

\- Sombra suave en tarjetas principales.

\- Sombra mayor en modales.

\- Bordes visibles en bloques que necesitan separación.



\### 5.4 Botones

Todos los botones deben seguir variantes consistentes:

\- primary

\- secondary

\- ghost

\- danger

\- success

\- icon-button



Con tamaños:

\- sm

\- md

\- lg



\---



\## 6. Patrones globales a implementar



\### 6.1 Header de módulo

Cada módulo debe iniciar con:

\- ícono

\- subtítulo pequeño

\- título grande

\- breve descripción

\- acciones principales a la derecha



\### 6.2 Toolbar

Bajo el header:

\- buscador

\- filtros

\- estado

\- acciones rápidas



\### 6.3 Breadcrumb y retroceso

Todos los submódulos deben incluir:

\- breadcrumb o ruta

\- botón “Volver”

\- título del subflujo



\### 6.4 Tarjetas KPI

Para dashboard/reportes:

\- tarjeta con icono

\- título corto

\- valor principal

\- pequeño descriptor

\- color diferenciador



\### 6.5 Modal estándar

Todos los modales deben tener:

\- título

\- subtítulo

\- contenido claro

\- footer fijo

\- acciones primarias y secundarias alineadas

\- cierre predecible



\### 6.6 Drawer lateral

Usar drawer para:

\- detalle de producto

\- detalle de empleado

\- detalle de ticket

\- detalle de caja

\- detalle de compra



\---



\## 7. Rediseño por módulo



\## 7.1 Inicio / Dashboard

\### Objetivo

Convertir Inicio en un verdadero dashboard ejecutivo.



\### Cambios

\- Separar “indicadores reales de la sucursal” en tarjetas KPI.

\- Agregar bloques:

&#x20; - ventas netas,

&#x20; - tickets,

&#x20; - caja,

&#x20; - capital,

&#x20; - lotes próximos a vencer,

&#x20; - alertas.

\- Reemplazar listas de texto por tarjetas y paneles.

\- Agregar gráficos:

&#x20; - evolución de ventas,

&#x20; - ventas por método de pago,

&#x20; - top productos,

&#x20; - alertas de stock/lotes.

\- Agregar selector de periodo como segmented control.

\- Mejorar encabezado de dashboard.



\---



\## 7.2 Punto de venta

\### Objetivo

Volver el POS visual, equilibrado y más rápido de operar.



\### Cambios

\- Crear layout de 3 áreas claras:

&#x20; 1. cabecera y estado de caja,

&#x20; 2. catálogo de productos,

&#x20; 3. ticket y pago.

\- Rediseñar tarjetas de producto:

&#x20; - foto/placeholder,

&#x20; - nombre,

&#x20; - precio,

&#x20; - stock breve,

&#x20; - botón de agregar.

\- Mostrar categorías como chips o tabs visuales.

\- Asegurar que las categorías aparezcan sin necesidad de escribir primero.

\- Mejorar espacios, alturas y simetría.

\- Resaltar estado “Caja abierta / Caja cerrada”.

\- Añadir alertas operativas claras si caja está cerrada.

\- Reordenar acciones de cobro.



\---



\## 7.3 Caja

\### Objetivo

Dar claridad operativa y prevenir errores.



\### Cambios

\- Mostrar panel de estado:

&#x20; - caja abierta/cerrada,

&#x20; - responsable,

&#x20; - monto inicial,

&#x20; - tiempo abierta.

\- Agregar alertas visibles si la caja lleva demasiadas horas abierta.

\- Crear modal de apertura y modal de cierre claros.

\- Mejorar lectura de movimientos.

\- Resumir efectivo, egresos, ingresos y cierre.



\---



\## 7.4 Productos

\### Objetivo

Que el catálogo y mantenimiento de productos sean visuales y fáciles.



\### Cambios

\- Rediseñar listado con:

&#x20; - toolbar superior,

&#x20; - filtros,

&#x20; - categorías visibles,

&#x20; - switch entre vista grid/list.

\- Tarjetas de producto más limpias y simétricas.

\- Mejorar modal o wizard de creación.

\- Separar:

&#x20; - datos generales,

&#x20; - precios,

&#x20; - presentaciones,

&#x20; - inventario relacionado.

\- Mejorar ficha detalle en drawer o tabs.



\---



\## 7.5 Compras

\### Objetivo

Facilitar recepción de mercadería y lotes.



\### Cambios

\- Flujo tipo wizard:

&#x20; 1. cabecera de compra

&#x20; 2. productos

&#x20; 3. lotes/vencimientos

&#x20; 4. revisión

&#x20; 5. confirmación

\- Mejor resumen lateral.

\- Mejor diferenciación visual de pasos.

\- Etiquetas claras para lotes y vencimiento.



\---



\## 7.6 Inventario

\### Objetivo

Hacer más evidente el estado físico del stock.



\### Cambios

\- Tab de resumen por sucursal.

\- Tab de lotes.

\- Tab de movimientos.

\- Destacar:

&#x20; - próximos a vencer,

&#x20; - vencidos,

&#x20; - bajo stock,

&#x20; - quiebres.

\- Usar tarjetas, badges y tablas limpias.



\---



\## 7.7 Personal

\### Objetivo

Hacer entendible la gestión de miembros e invitaciones.



\### Cambios

\- Listado con tarjetas limpias por persona.

\- Modal “Nuevo empleado” más ordenado.

\- Mejor visualización de:

&#x20; - rol,

&#x20; - sede,

&#x20; - turno,

&#x20; - estado.

\- Mejor diseño para editar, bloquear y reenviar invitación.



\---



\## 7.8 Reportes

\### Objetivo

Convertir reportes en módulos analíticos visuales.



\### Cambios

\- Encabezado ejecutivo.

\- KPIs principales.

\- Gráficos:

&#x20; - ventas por día,

&#x20; - ventas por método de pago,

&#x20; - top productos,

&#x20; - desempeño por sucursal.

\- Tablas como segundo nivel.

\- Filtros más ordenados.

\- Mejor uso de color.



\---



\## 7.9 Configuración y submódulos

\### Objetivo

Mejorar navegación y comprensión de estructura.



\### Cambios

\- Breadcrumb en submódulos.

\- Botón volver visible.

\- Tabs o subnavegación interna cuando aplique.

\- Mejor separación entre secciones.



\---



\## 8. Validaciones UX/IX a implementar



Estas validaciones sí deben verse en la interfaz:



\- Alertar si una caja lleva abierta demasiado tiempo.

\- Alertar si una acción requiere caja abierta.

\- Mostrar estados vacíos bien diseñados.

\- Mostrar loaders y confirmaciones claras.

\- Mostrar mensajes de éxito y error consistentes.

\- Deshabilitar acciones inválidas con explicación visual.

\- Evitar que el usuario se pierda en subflujos.



\---



\## 9. Restricciones



\- No cambiar backend ni contratos API, salvo si es indispensable y se solicita aprobación.

\- No alterar RLS, licencias, permisos, invitaciones ni seguridad.

\- No hacer push ni despliegues.

\- No crear datos de prueba nuevos por cuenta propia.

\- No ejecutar suites grandes de prueba.

\- Solo verificar compilación con `npm run build`.



\---



\## 10. Definición de terminado



Se considerará aceptable cuando:



1\. Cada módulo tenga estructura visual clara.

2\. Exista mejor diferenciación por color y bloques.

3\. Los submódulos tengan breadcrumb o botón volver.

4\. POS, Dashboard y Reportes se vean como módulos prioritarios y profesionales.

5\. Las tarjetas y botones mantengan simetría, espaciado y consistencia.

6\. Los estados vacíos, alertas y modales sean claros y agradables.

7\. El sistema completo sea más fácil de recorrer durante las pruebas manuales.



\---



\## 11. Ejecución por sprints



\### Sprint UX-V2.1

\- sistema visual global

\- tokens

\- colores

\- espaciado

\- botones

\- tarjetas

\- headers

\- breadcrumb

\- navegación base



\### Sprint UX-V2.2

\- dashboard / inicio

\- métricas

\- indicadores reales

\- resumen visual

\- primeras gráficas



\### Sprint UX-V2.3

\- punto de venta

\- caja

\- catálogo visual del POS

\- ticket

\- alertas operativas



\### Sprint UX-V2.4

\- productos

\- categorías

\- detalle

\- creación/edición

\- tarjetas y filtros



\### Sprint UX-V2.5

\- compras

\- inventario

\- lotes

\- vencimientos

\- recepciones guiadas



\### Sprint UX-V2.6

\- personal

\- invitaciones

\- edición de miembros

\- configuraciones relacionadas



\### Sprint UX-V2.7

\- reportes

\- dashboards visuales

\- gráficos

\- tablas de apoyo



\### Sprint UX-V2.8

\- consistencia final

\- responsive

\- pulido visual

\- empty states

\- feedbacks

\- loaders

\- revisión integral



\---


## 12. Implementación local V2 — 2026-10-26

Estado: UX-V2.1–UX-V2.8 implementados en frontend dentro de los contratos disponibles; compilación PASS. Aceptación visual, responsive y funcional en Chrome PENDIENTE del propietario. No se realizaron pruebas de navegador, suites automatizadas, operaciones comerciales ni despliegues.

| Sprint | Cambio integrado | Compilación |
|---|---|---|
| UX-V2.1 | Paleta índigo, escala de espacios, botones/tamaños, tarjetas, headers con icono, breadcrumb y regreso contextual | PASS |
| UX-V2.2 | Inicio: KPIs jerarquizados, periodo segmentado, riesgo de inventario, detalle operativo separado | PASS |
| UX-V2.3 | POS: catálogo en tarjetas, directorio Owner por categorías, precio exacto por presentación, caja visible, arqueo gráfico y ticket delimitado | PASS |
| UX-V2.4 | Productos: tarjetas/lista, categorías, filtros sobre la página, composición, precios y acceso al drawer; foco del wizard | PASS |
| UX-V2.5 | Compras: pasos diferenciados y resumen lateral persistido; inventario: indicadores de página y badges de fecha de lotes | PASS |
| UX-V2.6 | Personal: tarjetas/lista, rol, sedes y horario; invitación guiada y secciones de configuración | PASS |
| UX-V2.7 | Reportes: tipos de consulta, filtros editados/aplicados, KPIs, gráficos accesibles y tablas desplegables; rotación real en Inicio | PASS |
| UX-V2.8 | Responsive por CSS, empty/loading states, feedback y foco; salida segura de resultados inciertos sin limpiar marcador/identidad ni permitir otra escritura | PASS |

Validación final: `cd frontend; npm run build` terminó con exit code 0 (TypeScript + Vite). Bundle JS 756.70 kB, gzip 213.39 kB; advertencia Vite >500 kB pendiente de optimización, sin ocultarla. No equivale a aceptación operativa ni evidencia PostgreSQL.

Límites conservados:
- Las categorías y el directorio POS solo se consultan como Owner, conforme a la autorización vigente. Los trabajadores conservan la búsqueda operacional. Las categorías filtran la página cargada (50 productos; primeras 100 categorías POS); seleccionar una referencia consulta el stock y las presentaciones reales antes de agregar.
- No se inventaron series diarias, evolución temporal ni reparto general por medios de pago: los reportes actuales no los devuelven. Sí se grafican agrupaciones reales, rotación, capital, riesgos y comisiones. Los pagos por medio se grafican exclusivamente desde un arqueo real consultado.
- Inventario no ofrece un directorio de movimientos en su contrato actual. No se simula. Los mínimos/críticos siguen gobernados por el servidor; los indicadores locales señalan página actual, saldo cero o fecha, sin redefinir stock vendible.
- Recordatorio de caja a las 12 horas: solo indicador visual explícito, sin expiración, bloqueo ni cierre automático.
- No se agregó reenvío de invitaciones inexistente en el contrato. Se conservan enlace temporal, verificación y aprobación independientes.
- En resultados inciertos, ocultar el modal conserva su estado y los marcadores existentes. Proveedores sigue requiriendo conciliación operacional: su API no ofrece constancia consultable ni se libera una nueva alta por ausencia en una página. No se guardaron formularios sensibles en almacenamiento nuevo.
- Backend, contratos, PostgreSQL, OAuth, RLS y permisos sin modificaciones.

Archivos de esta intervención (rutas relativas al repositorio):
- Base: `frontend/src/shared/styles/tokens.css`, `operations.css`; `frontend/src/shared/ui/Button.tsx`, `PageHeader.tsx`, `Breadcrumbs.tsx`, `MetricCard.tsx`, `BarChart.tsx`, `Modal.tsx`, `Steps.tsx`, `States.tsx`, `ui.css`.
- Navegación/composición: `frontend/src/app/layouts/WorkspaceShell.tsx`, `workspace.css`; `frontend/src/app/router/routes.tsx`.
- Inicio/análisis: `frontend/src/features/dashboard/DashboardPage.tsx`, `dashboard.css`; `frontend/src/features/reporting/OwnerDashboard.tsx`, `ReportCharts.tsx`, `ReportsPage.tsx`, `ReportFilters.tsx`.
- POS/caja: `frontend/src/features/pos/PosCatalogBrowser.tsx`, `ProductSearchPanel.tsx`, `PosHeader.tsx`, `SaleCheckoutPanel.tsx`, `pos.css`; `frontend/src/features/cash/CashAge.tsx`, `CashSessionPanel.tsx`, `CashPage.tsx`.
- Catálogo/abastecimiento: `frontend/src/features/catalog/ProductsPage.tsx`, `CreateProductForm.tsx`, `catalog.css`; `frontend/src/features/purchasing/PurchasesPage.tsx`, `ReceiptEditor.tsx`, `SuppliersPanel.tsx`; `frontend/src/features/inventory/InventoryPage.tsx`.
- Personal/configuración/recuperación: `frontend/src/features/employees/EmployeesPage.tsx`, `EmployeeInvitationsPanel.tsx`; `frontend/src/features/settings/SettingsPage.tsx`; `frontend/src/features/platform/PlatformPage.tsx`; `frontend/src/features/commissions/CommissionsPage.tsx`; `frontend/src/features/transfers/RequestGoods.tsx`.
- Este plan. Se conservó su contenido anterior y las modificaciones locales existentes.

Siguiente acción: simulaciones del propietario en su entorno habitual; revisar Inicio, POS, caja, catálogo, compras/inventario, personal y reportes a 360/768/1280/1440 px, navegación por teclado y recuperación de resultados inciertos. El build no instala estos archivos en hosts LAB/QA publicados.