# RFA3 · Cómo debería operar MediPOS

Corte del 26/11/2026 sobre `5d38898`. Esta es la propuesta para el operador. La [matriz](MATRIX.csv) conserva el estado observado y [RUN-10](VALIDATION.md#run-10) distingue las aserciones en PostgreSQL de las pruebas DOM. Las decisiones que cambian el producto están en el [backlog](ROADMAP.md).

## Decisión principal por caso

Los números son los sufijos del prefijo UC de cada fila. KEEP conserva el comportamiento; validar exige cerrar una variante antes de aceptarlo. Una brecha necesita una decisión de producto aunque la implementación actual cumpla su contrato. Los cuatro grupos son excluyentes; una decisión KEEP tampoco otorga aceptación comercial.

| Familia y ficha | KEEP | Corregir o simplificar | Brecha por decidir | Validar antes de aceptar |
|---|---|---|---|---|
| [CAJ · Caja y POS](flows/01-caja-pos.md) | 01–03,07–09,11–13 | 10,17,19 | — | 04–06,14–16,18 |
| [CAT · Catálogo](flows/02-catalogo.md) | 03–05,07–19,22 | — | 06,20 | 01–02,21 |
| [COM · Compras e inventario](flows/03-compras-inventario.md) | 01–02,05–08,10–11,15 | — | 09,12 | 03–04,13–14 |
| [TRA · Transferencias](flows/04-transferencias.md) | 02–07,09–11 | 01 | — | 08,12–13 |
| [REP · Reportes y comisiones](flows/05-reportes-comisiones.md) | 01–15,17 | — | — | 16 |
| [AVI · Avisos y auditoría](flows/06-avisos-auditoria.md) | 01–04 | — | — | 05–07 |
| [ACC · Acceso y estructura](flows/07-acceso-personal.md) | 01–12,17–24 | — | — | 13–16,25–27 |
| [PLA · Plataforma](flows/08-plataforma.md) | 01–06,08–16,18 | — | — | 07,17 |
| [INT · Contingencia](flows/09-contingencia-integraciones.md) | 01–10,13–15 | — | — | 11–12,16–18 |

### Caja y POS · encontrar un cierre después de salir

1. El cajero abre su caja y trabaja en su sede. Al cerrar cuenta el efectivo físico. Revisa esperado del servidor, contado y diferencia con signo. Yape, Plin y tarjeta conservan sus totales por separado.
2. Si el cierre está en curso, esperar. Si se pierde el acuse, volver a la misma caja y consultar. Un error de lectura o una lectura todavía abierta conserva la intención. Un estado cerrado confirma el cierre aunque todavía falte el detalle del arqueo.
3. La caja sale de **Activas**. Desde **Cierres**, el usuario consulta los cierres de la sede por fecha y abre el arqueo existente. Owner consulta todos los autorizados en la sede y el personal operativo solo los propios. La reentrada desde una URL limpia está implementada y pendiente de comprobación en P3 del candidato.
4. En POS, conservar presentación y precio del servidor, borrador versionado, cobro mixto, ticket interno y anulación compensatoria. Al salir de un carrito local con cambios, ofrecer conservar o descartar según la protección existente. Un borrador persistido sin cambios debe poder cerrarse y recuperarse. Caracterizar también vaciar todas las líneas de un borrador guardado. Durante un envío o una respuesta incierta, conservar su identidad y bloquear la repetición. El motivo para pedir confirmación es la pérdida de edición real. `useSaleDraft.ts` ya compara líneas guardadas; verificar antes de proponer cualquier refactor.

| Estado comprobado | Acción permitida | Efecto financiero |
|---|---|---|
| Abierta, sin intento pendiente | Enviar cierre con el conteo revisado | Una transacción de cierre |
| Envío incierto, GET abierto o fallido | Consultar el ID inicial | Ninguna escritura |
| Cerrada, arqueo indisponible | Reconsultar arqueo | Cierre permanece confirmado |
| Cerrada, directorio vacío | Verificar ámbito y buscar en Cierres si se aprueba | Solo lectura |

### Catálogo · encontrar y mantener un producto

1. Personal operativo busca en su sede; el Owner abre mantenimiento para alta local o asociación global. Comprueba tipo, composición cuando corresponda, presentaciones, estado y precio. Cambia unidades con la versión leída. Una colisión requiere abrir la configuración vigente antes de otra edición.
2. En mantenimiento, el filtro opera sobre la página recibida. Hoy el operador cambia de página para localizar otra fila. Mantener esa opción salvo que el operador necesite buscar en **todo el catálogo del negocio**. En ese caso consultar el directorio administrativo con texto y página acotados, conservando filtros y alcance al volver; el buscador POS tiene otro surtido y permisos.
3. Una escritura incierta se recupera con la intención o el recurso original. Un producto publicado globalmente aún requiere asociación local; su publicación no genera precios ni stock. Conservar importación Excel con resultados por fila. Un trabajo interrumpido solo se recupera con la autorización y los bloqueos vigentes. Borrado físico CAT-22 permanece fuera de alcance.

### Compras e inventario · reencontrar una recepción y seguir el lote

1. Quien tiene acceso selecciona sede, proveedor cuando corresponda y documento si lo tiene. Guarda borrador; añade líneas, presentación, costo, lote y vencimiento según producto. Revisa la versión antes de confirmar. Un conflicto conserva las líneas locales hasta comparar con el servidor.
2. Con acuse perdido, consultar la constancia original y abrir la recepción. Hoy se puede recuperar por ID o por referencia exacta; una referencia puede repetirse. Si se pierde el ID de una compra sin referencia, proponer **Recepciones recientes** por sede, fecha y estado si el operador requiere esa consulta. Elegir la fila abre el detalle actual. Cancelar búsqueda o ver vacío no origina compra.
3. Confirmar genera lotes y movimientos atómicos. Inventario distingue físico y vendible; el ajuste autorizado conserva motivo y movimiento. FEFO excluye medicina vencida. Al cambiar a Lotes debe mantenerse el nombre dentro del ámbito correcto; al cambiar de sede debe evitarse mostrar el nombre cacheado de la anterior. Los tres GET en frío de RUN-09 justifican medir costo en una página representativa, antes de añadir una proyección.

### Transferencias · enviar y recibir sencillo o mercadería

1. Para sencillo, el usuario habilitado elige caja abierta de origen, sede destino válida e importe positivo exacto. Hoy `expectedCashAmount` nulo bloquea incluso el envío al servidor. Propuesta mínima: permitir pedir el despacho al completar esos datos. Mostrar el importe a entregar. Si se necesita visualizar saldo antes de confirmar, aprobar aparte una **lectura informativa** de disponible con hora de cálculo. Ni apertura ni cero representan ese disponible.
2. El servidor vuelve a validar actor, sede, estado y saldo bajo bloqueo. Efectivo insuficiente deja editable el importe con el error preciso. Un resultado incierto conserva requestId, origen y monto; solo consultar la constancia. Tras un acuse válido se puede consultar el envío **en tránsito**. El destino elige otra caja abierta compatible y recibe una sola vez. El enviado cuenta en origen y el recibido en destino en sus instantes respectivos.
3. En mercadería se mantienen solicitud, aprobación de origen, despacho por lotes y recepción total/parcial con diferencia documentada. Cancelar solo en estado permitido. El destino consulta el mismo expediente e incorpora stock vendible únicamente al recibir. Reconsultar tras revocación, conflicto o pérdida de respuesta conserva permisos y el ID original.

~~~mermaid
stateDiagram-v2
    [*] --> Preparado
    Preparado --> EnTransito : despacho confirmado
    Preparado --> Preparado : saldo insuficiente
    Preparado --> Incierto : respuesta perdida
    Incierto --> Incierto : lectura insuficiente
    Incierto --> EnTransito : constancia del original
    Incierto --> Recibido : detalle ya recibido
    EnTransito --> Recibido : receptor autorizado confirma
~~~

### Reportes y comisiones · explicar una cifra

1. Owner selecciona rango y sede y aplica los filtros una vez. Abre el detalle con el mismo periodo; distingue venta, anulación y monto neto. La sede, los filtros y la fecha de reconocimiento acompañan totales y exportación.
2. Una comisión se reconoce con la venta y se revierte desde el asiento original incluso si la regla cambió. Mantener snapshots y consultas de solo lectura. Si falla una exportación volver a la consulta con el filtro original; una recarga fallida no muestra datos viejos como actuales. Reposición queda como consulta interna hasta que exista un requisito de pantalla.

### Avisos y auditoría · distinguir lectura y resolución

1. El usuario entra en su bandeja de sede, abre un aviso y, si está autorizado, consulta el lote de origen. Marcar leído afecta al destinatario; el riesgo sigue visible mientras su causa operativa exista.
2. Si falla el acuse, reconsultar aviso y contador con la misma membresía. Generación periódica y deduplicación deben tolerar reinicio. El Owner revisa metadatos auditables por periodo y ámbito; se conserva la historia incluso si cambia o se desactiva el producto. Confirmar la navegación al origen en E4 antes de ofrecer enlaces nuevos.

### Acceso, personal y estructura · llegar al lugar autorizado

1. El usuario inicia sesión. El servidor resuelve membresía, licencia, sede y horario. Con una sola opción autorizada continúa; varias opciones requieren elección. Sin asignación o con acceso revocado, salir al selector sin mostrar datos del contexto anterior. Una caída de red permite reconsultar.
2. El Owner configura entidad y primera sede dentro del cupo. Una entidad ya confirmada se reutiliza si la alta de sede queda pendiente. Para personal, emitir invitación, aceptar con la propia identidad y aprobar antes de operar. Antes de desactivar, revalidar el último Owner.
3. Al cambiar de sede o identidad, mantener las operaciones pendientes en su ámbito de origen y cancelar las lecturas obsoletas. Aceptación pendiente de reentrada, expiración y revocación con navegador aislado.

### Plataforma · designar al primer Owner

1. El operador con autoridad independiente crea la botica con negocio, Gmail de destino y licencia. Revisa esos datos, confirma con la clave original y ve **Owner pendiente**. Un acuse confirmado cierra el asistente aun si falla el refresco del directorio. Ante resultado incierto consultar la constancia de ese operador.
2. El destinatario reclama solo con su identidad verificada. Cambiar o revocar destino antes del reclamo revalida el estado; una membresía comercial nunca habilita la consola. En licencias la mutación deja historial. Distinguir 429 (esperar), 403 (sin autoridad) y 409 (conciliar). Preservar los contratos históricos y el catálogo global separado. Purga física fuera del circuito.

### Contingencia e integraciones · recuperar sin volver a cobrar

1. Antes de perder red, el operador prepara un dispositivo autorizado con catálogo y cursor de su sede. Al encolar una operación, mostrar **local pendiente**; conservar UUID, dispositivo, actor y caja original.
2. Al reconectar, procesar el mismo lote. Un acuse terminal se relee o reproduce por UUID. Si el stock se consumió o cerró la caja original, el Owner revisa el conflicto; cambiar de caja, usuario o dispositivo no convierte un rechazo en venta.
3. Mantener UBL en revisión humana, catálogo oficial en staging/curación y entrega CPE separada y desactivada. La lectura o importación documental por sí sola no modifica existencias. Backup y restauración son operaciones de recuperación con prueba separada. Persistencia de CI no comprueba PWA real, emisión externa ni restauración del host del operador.

## Límites de diseño

El ledger y la transacción del servidor deciden fondos y stock. El formulario conserva edición local; la constancia y las lecturas del recurso resuelven la incertidumbre. Una lista filtrada responde qué coincide hoy, mientras que un ID o constancia responde qué pasó con una operación concreta. Esta distinción explica F-001 y F-004 sin unificar sus políticas de lectura. Conservar los hooks de cierre, borrador y transferencias con sus reglas propias. No hay evidencia que justifique un nuevo orquestador genérico ni una migración. Toda consulta propuesta debe probar alcance y coste antes de llegar a producción.
