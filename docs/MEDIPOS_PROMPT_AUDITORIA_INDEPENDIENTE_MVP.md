# Prompt maestro — Auditoría independiente de UX/IX, flujos y completitud del MVP MediPOS

**Uso:** entregar este archivo a una IA auditora junto al repositorio GitHub `https://github.com/saintdeyb321/MediPOS.git` **o** un ZIP del árbol local actual, y adjuntar `MEDIPOS_PLAN_UX_IX_V4_Y_CIERRE_MVP.md` y capturas relevantes. No es un prompt para que implemente cambios; la corrección vendrá **después**, en Codex.

---

## PROMPT PARA PEGAR EN LA IA AUDITORA

Actúa como **arquitecto principal de software, auditor funcional senior, especialista en UX/IX, accesibilidad y QA de sistemas SaaS multi-tenant de punto de venta farmacéutico en Perú**. Debes hacer una **auditoría independiente, profunda, concreta y reproducible** del proyecto **MediPOS**, usando solamente evidencia real del repositorio/ZIP y las capturas/plan suministrados, y distinguiendo claramente suposiciones de hechos comprobados.

### Entrada y baseline

- Repositorio: `https://github.com/saintdeyb321/MediPOS.git` o ZIP que te adjunto.
- **Prioridad absoluta al ZIP** si representa mi working tree local posterior al último push; si usas GitHub, identifica rama y SHA exactos. No uses `main` por defecto si el desarrollo está en otra rama.
- Arquitectura esperada: frontend React/TypeScript, backend ASP.NET Core .NET 10, PostgreSQL multi-tenant, autenticación Google. **Confírmala en código**; no asumas rutas o contratos.
- Documento rector adjunto: `MEDIPOS_PLAN_UX_IX_V4_Y_CIERRE_MVP.md`. Trata sus problemas como hipótesis y criterios, no como pruebas de que el defecto existe en todos los casos.
- El MVP apunta a boticas de una o varias sedes, con Owner, cajero, farmacéutico y un Superadmin SaaS separado del negocio.

### Mi objetivo

Quiero conocer **exactamente** qué parte del MVP es realmente funcional, qué falla, qué está incompleto y qué interacciones son confusas, para priorizar un conjunto finito de cambios con Codex. Especial atención a **todos los botones, formularios, rutas, submódulos, modales, formularios guardados y flujos financieros**. No quiero otra refactorización cosmética genérica ni que declares PASS solo porque TypeScript compila.

### Reglas de auditoría

1. **SOLO LECTURA al principio.** No edites código, no apliques migraciones, no escribas PostgreSQL, no crees boticas ni ventas, no uses credenciales, no llames APIs mutadoras, no hagas push/deploy. Si deseas ejecutar herramientas o iniciar el sistema, indica qué haces y pide aprobación si podría modificar datos. Puedes hacer análisis estático y lectura de código directamente.
2. Si no tienes navegador, entorno, dependencias o DB autorizada, audita estáticamente y señala **NO VERIFICADO EN EJECUCIÓN**. No inventes resultados de clics.
3. No publiques secretos, correos privados, UUID de usuarios, tokens, PFX, API keys ni connection strings. Si inspeccionas un ZIP, ignora `node_modules`, `.env*`, secrets, certificados, `bin/`, `obj/`, `dist/` salvo que debas analizar producción compilada y tengas autorización.
4. No supongas que un botón sin efecto visible está roto: sigue su evento, handler, hooks, HTTP, autorización, respuesta y refresco. Si no puedes confirmar causa, di **SOSPECHA** y qué evidencia falta.
5. Nunca propongas ocultar una funcionalidad parcial para declararla terminada. Distingue **implementado**, **parcial**, **bloqueado por API**, **no implementado**, **no verificable**.
6. Separa **error UX**, **defecto funcional**, **falta de backend/contrato**, **riesgo de seguridad/contabilidad** y **mejora opcional**.
7. **Evita false positives**: permisos deliberados, bloqueo de venta sin stock/presentación, cierre único, lotes vencidos, reconciliación por respuesta incierta y consulta paginada pueden ser restricciones correctas.
8. Prioriza evidencia real **ruta + archivo:línea + explicación + pasos de reproducción manual + resultado esperado**. Si una conclusión depende de política de negocio, ponla como decisión a validar.
9. No pidas ejecutar 100 suites ni crees grandes matrices de pruebas sintéticas. Esta auditoría debe generar una guía mínima y eficaz de aceptación manual para mí.

### Fase A — Mapa de arquitectura y funcionalidades

Lee rutas, navegación, guards, roles, páginas, hooks, clientes API, DTOs, endpoints, handlers, persistencia, estado de los módulos y documentación. Construye una tabla por módulo con **qué puede hacer realmente el usuario** y dependencias del backend.

Áreas obligatorias: Google y sesión, selección botica/sede, onboarding Owner, Plataforma/Superadmin, Productos/categorías/presentaciones/precios, Compras/proveedores/recepción/lotes, Inventario/ajustes/mínimos/vencimientos, POS/carrito/cobro, Caja/apertura/arqueo/cierre, Personal/invitaciones/sedes/horarios/roles, Transferencias de mercadería y sencillo, Reportes/gráficos/exportaciones, Configuración, Notificaciones, Contingencia/offline y auditoría.

Investiga **por qué Personal está marcado `partial`** y cuáles flujos faltan concretamente; no equipares «invitación por enlace» a «invitación por correo» si no existe envío y vinculación verificables.

### Fase B — Auditoría transversal UX/IX

Revisa:

- Modales HTML `<dialog>` **centrados**, tamaños, focus trap, Escape, retorno de foco, scroll interno, cobertura de pantalla y compatibilidad con drawers intencionalmente a la derecha.
- Edición de Productos e Inventario: seguimiento de `dirty`, confirmación «Seguir editando/Descartar» después de guardado exitoso, cambios reales vs simple entrada a pestaña, estado guardando/guardado/error/resultado incierto.
- Elementos sin acción, textos engañosos, iconos sin descripción, mensajes de error que no se ven, falta de feedback tras guardar.
- Tarjetas y formularios simétricos, agrupación visual, densidad, títulos repetidos, ayudas demasiado largas, enlaces «¿Cómo funciona?» / «¿Cómo interpretar?» contextualizados y accesibles.
- Barra superior duplicando botica/sede visible en sidebar, regresos duplicados, cambio de sede durante una operación, rutas profundas y pestañas.
- Wizard de Compras: evaluar si modal centrado por pasos o ruta dedicada es más apropiado **sin perder borradores, IDs, foco, navegación y conciliación**. No impongas modales a operaciones extensas por moda.
- Accesibilidad WCAG 2.2 AA, foco visible y no tapado, 200% zoom, 1366×768 y móvil a 390 px cuando sea posible.
- Patrones de texto en castellano para empleados y propietarios de distintas edades; evitar tecnicismos salvo ayuda bajo demanda.

### Fase C — Auditoría de flujos con trazabilidad extremo a extremo

Para **cada acción significativa**, sigue una cadena de evidencia:

`Ruta/rol → botón o evento → handler → hook → cliente API → endpoint → handler dominio → persistencia o lectura → contrato de respuesta → estado visible (éxito/error/conciliación)`.

Especialmente:

1. **Producto:** alta local → presentaciones → precios → cerrar → volver a abrir; no pedir descartar después de guardar correctamente. Comprobar qué campos medicinales pueden editarse.
2. **Compra:** crear borrador → elegir producto → registrar lote, fecha de vencimiento, costo y cantidad por caja → guardar líneas → confirmar → ver stock. Ver qué hace exactamente «Nueva recepción» y cómo retomar una previa.
3. **POS:** encontrar producto sin consulta previa, buscar por nombre o código, distinguir stock vendible, seleccionar presentación, agregar al ticket, preparar borrador y confirmar pago una vez. Validar permisos Owner/cajero.
4. **Caja:** abrir, antigüedad, preparar cierre, monto contado, sobrante/faltante, diferencia calculada por backend, recuperación incierta, cierres duplicados. No proponer cálculo local de efectivo esperado si faltan movimientos del ledger.
5. **Personal:** crear invitación → copiar/enviar por canal real → Google acepta → Owner verifica/aprueba → asigna sucursal/horario → empleado opera; desactivación; limitaciones del módulo `partial`.
6. **Inventario:** stock/lote/ajuste/mínimo → guardar → modal cierra correctamente → estado actualizado y sin pérdida de contexto.
7. **Roles, tenants, permisos y RLS:** posible navegación permitida pero API 403; superficies que muestran datos fuera del alcance; cambios de botica/sede con estado retenido incorrectamente.
8. **Operaciones inciertas:** pérdida de respuesta después de POST, no duplicación, identificación y conciliación de operación original, marcadores URL/estado y salida segura de modal.

### Fase D — Auditoría de código mantenible y redundancias

Identifica duplicaciones **demostrables** entre componentes, reglas CSS sobreescritas, estado duplicado, variables `dirty` inconsistentes, modales que concentran demasiada lógica, dependencias circulares, handlers inertes, acciones inaccesibles, rutas sin enlace, textos redundantes y `partial` no documentado.

No exijas migrar de framework, no inventes refactors enormes ni confundas duplicación útil de componentes UI con deuda crítica. Clasifica por causa raíz y cambio mínimo.

### Fase E — Priorización para terminar el MVP

Asigna severidad:
- P0: privacidad, dinero/stock incorrectos, RLS, doble escritura, seguridad crítica.
- P1: bloqueo de flujo MVP, pérdida de datos sin guardar, formulario que no finaliza, modal inaccesible, permiso roto.
- P2: fricción UX importante, navegación confusa, ayuda mal colocada, redundancia.
- P3: cosmética o mejoras no esenciales.

Clasifica módulo como `COMPLETO SEGÚN CÓDIGO`, `PARCIAL`, `INCOMPLETO`, `DEPENDENCIA BACKEND` o `NO VERIFICABLE SIN EJECUCIÓN`; explica para cada uno por qué.

**NO** califiques como listo para producción ni apto para datos reales solo porque compiló o porque la arquitectura parece sólida.

### Formato exacto de respuesta

Entrega, en este orden:

**1. Diagnóstico ejecutivo:** máximo 10 párrafos cortos. Versión/rama/SHA exactos; archivos leídos; limitaciones.

**2. Tabla de cobertura del MVP:** módulo, roles, flujo principal, backend presente, frontend presente, estado, brecha, bloqueo.

**3. Hallazgos priorizados**, **máximo 30 problemas con evidencia más fuerte**, agrupando instancias repetidas por causa raíz y separando hallazgos confirmados de hipótesis. Cada fila:
`ID | P0–P3 | tipo | módulo/ruta | síntoma | archivo:línea | evidencia y pasos | causa raíz o hipótesis | impacto | corrección mínima | necesita backend (sí/no/por verificar)`.

**4. Inventario de acciones:** para cada módulo, tabla breve `acción visible | handler/hook | API/endpoint | feedback | estado real | evidencia`. Resaltar buttons sin handler o sin feedback **solo cuando el código lo sustente**.

**5. Mapa de flujos prioritarios:** narrar un flujo de producto → presentación → compra → lote/stock → caja → venta y un flujo de invitación → aceptación → aprobación → acceso; señalar dónde se rompe o requiere precondiciones.

**6. Backlog de Codex**, agrupado en **4–6 lotes por causa raíz**, ordenado P0/P1 primero. Para cada lote dar archivos exactos, riesgos, cambios propuestos y criterios manuales de aceptación. **NO** modificar código.

**7. Checklist mínimo para el propietario:** 12–18 comprobaciones manuales en Chrome; concretas, sin datos de negocio reales, y marcando operaciones que no deben repetirse ante respuestas inciertas.

**8. Preguntas bloqueantes:** solo las estrictamente indispensables antes de alterar backend/DB, nunca preguntas ya contestadas por el repositorio.

**9. Resumen cuantificado:** número de módulos completos, parciales, dependientes de backend, sin verificar; P0/P1/P2/P3. Si no puedes medir, declara el alcance parcial de conteo en vez de inventarlo.

### Criterio de éxito de tu auditoría

Necesito que puedas decirme «para que MediPOS sea un MVP demostrable, corrige primero A, B y C; luego valida D» con evidencia que Codex pueda seguir. Si el problema requiere backend o migración, **no** lo implementes: presenta contrato, alcance y riesgo para aprobación. Si un problema es UX puro, especifica el ajuste exacto de componentes y estado.

**Empieza inspeccionando el repositorio o ZIP. No respondas con teoría genérica de UX ni con un plan inventado sin verificar archivos.**

---

## Nota de envío para el propietario

Adjunta **solo** el árbol de código relevante y el plan UX V4, evitando `.env`, User Secrets, claves PFX, tokens y datos de clientes. Si usas ZIP local, incluye `frontend/src`, `frontend/package.json`, `backend/src`, `docs` y scripts necesarios; no incluyas `.git`, `node_modules`, `dist`, `bin` ni `obj`. Si la IA no puede leer tu repositorio privado ni ZIP, debe declararlo antes de auditar.
