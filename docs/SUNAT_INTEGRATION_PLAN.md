# MediPOS — Plan de integración tributaria futura (SUNAT / proveedor externo)

**Estado:** DIFERIDO — no forma parte del MVP comercial inicial.  
**Prioridad:** opcional, sujeta a demanda de clientes y verificación normativa.  
**Ubicación sugerida:** `docs/SUNAT_INTEGRATION_PLAN.md`  
**Alcance:** emisión de comprobantes de pago electrónicos (CPE). La importación de XML UBL de proveedores (B7.1) es una funcionalidad distinta.

## 1. Decisión de producto

MediPOS se orienta inicialmente a boticas independientes de una a cinco sedes en Perú. La prioridad de la primera versión comercial es vender, controlar caja e inventario, gestionar lotes y vencimientos y administrar sedes de manera confiable.

**No se implementará ni activará todavía la emisión electrónica.** Su complejidad tributaria, coste de soporte, firma digital y mantenimiento de integraciones no se justifican sin una demanda real validada.

Parte del mercado objetivo opera bajo el Nuevo RUS (NRUS), pero **no debe asumirse que todos los negocios de ese régimen están exentos de cualquier obligación de emisión electrónica**. La obligación y modalidad aplicables deberán revisarse según la normativa vigente, el contribuyente, el tipo de comprobante y la operación. MediPOS no debe determinar automáticamente el régimen tributario por número de sedes o tamaño.

La futura integración será **optativa y activable por entidad emisora**, sin ser dependencia para el uso normal del POS. Ninguna funcionalidad del core debe requerir conectividad con SUNAT para confirmar una venta.

## 2. Qué se conserva preparado

MediPOS mantiene una frontera modular `ElectronicDocuments`, separada de `SalesPos`, `Cash` e `Inventory` conforme a ADR-011 y BR-014.

El núcleo B7.2 proporciona, a nivel de implementación local, los conceptos de documento electrónico, snapshot de una venta confirmada, series, correlativos, idempotencia, cola persistente, intentos, estados e interfaz de proveedor. **Esto no significa que ya exista emisión tributaria válida ni autorización para utilizarla en producción.**

Mientras se posponga la integración:

- Los proveedores externos y el procesamiento automático de emisiones permanecen deshabilitados.
- El ticket interno conserva su condición **no tributaria**; jamás se rotula como boleta o factura electrónica.
- El POS, cobro, caja, comisiones, inventario y devoluciones siguen independientes del módulo CPE.
- No se crean documentos electrónicos reales ni se consume numeración tributaria por activar o utilizar el POS.
- No se exige certificado digital, usuario SOL ni suscripción a proveedores a clientes que no usen esta capacidad.
- No se implementan ahora endpoints productivos, firma, transformación UBL fiscal, consultas SUNAT, CDR, notas de crédito o resúmenes diarios solo «por dejar todo listo».

La activación futura requiere validar el código existente con PostgreSQL real, seguridad productiva y el proveedor elegido.

## 3. Alternativas abiertas

La elección se realizará cuando exista una necesidad comercial concreta. **No se toma ahora una decisión irreversible de proveedor.**

| Alternativa | Ventajas potenciales | Costos y riesgos que deben validarse |
|---|---|---|
| **SUNAT directo — SEE Del Contribuyente** | Control del proceso y menor dependencia de una plataforma comercial | Habilitación aplicable, XML UBL, firma digital, SOAP/servicios vigentes, CDR, cambios normativos, operaciones asíncronas, certificados y soporte |
| **API de un tercero (por ejemplo, «API SUNAT»)** | Puede reducir el esfuerzo de integración y mantenimiento tributario | Contrato/API real, cobertura, SLA, idempotencia, retención de datos, costos, seguridad, continuidad del servicio y quién asume la responsabilidad tributaria |
| **PSE/OSE u otro proveedor habilitado** | Servicios especializados y soporte formal según alcance contratado | Registro/habilitación, precio, disponibilidad, soporte, formatos, resguardo documental y obligaciones contractuales |

**Precio no confirmado:** se ha mencionado informalmente un posible plan de alrededor de **S/ 5 al mes** para una API externa. No es un precio verificado ni una base para presupuestar. Más adelante se revisarán tarifario oficial, moneda, IGV, cantidad de comprobantes, costo por RUC, excedentes, almacenamiento y condiciones contractuales.

También debe verificarse si la actividad que MediPOS prestaría como SaaS multiempresa exige obligaciones o habilitaciones específicas como proveedor de servicios electrónicos. No asumir que integrar directamente un web service convierte automáticamente a MediPOS en un PSE ni que lo exime de exigencias regulatorias.

## 4. Condiciones para reabrir este trabajo

Reevaluar la integración solo cuando se cumplan criterios concretos:

1. Existe al menos una botica piloto con necesidad tributaria confirmada y disposición a probar el servicio.
2. Se conoce el régimen, la condición de emisor y los comprobantes que realmente necesita.
3. Existe un responsable de validar requisitos legales y tributarios vigentes.
4. Se dispone de presupuesto y estimación de soporte por tenant/RUC.
5. La infraestructura de autenticación productiva, aislamiento entre tenants y pruebas PostgreSQL está validada.
6. Se cuenta con documentación técnica vigente del proveedor elegido y un ambiente autorizado de pruebas.

No crear un cronograma de activación ni asumir que B7.3 es requisito para lanzar el MVP sin esta evidencia.

## 5. Evaluación comparativa previa

Cuando corresponda, preparar una decisión breve con pruebas y cotizaciones verificadas:

- **Cobertura:** boletas, facturas, notas de crédito/débito, bajas, resúmenes, consultas y CDR según necesidad real.
- **Modelo de costo:** alta, mensualidad, por emisor/RUC, volumen, excedentes, infraestructura y mantenimiento.
- **Integración:** API REST/SOAP, firma, credenciales, especificaciones oficiales y compatibilidad con .NET 10.
- **Operación:** uptime/SLA, sandbox, timeouts, reconciliación de estados inciertos, reintentos e idempotencia externa real.
- **Seguridad:** segregación por RUC/entidad legal, custodia de certificados y secretos, protección de información personal.
- **Portabilidad:** exportación de XML/CDR, acceso al histórico, posibilidad de cambiar de proveedor y mecanismos de contingencia.
- **Cumplimiento:** obligaciones tributarias del emisor y obligaciones aplicables al operador del SaaS/proveedor.

Escoger la opción de menor **costo total y riesgo operativo**, no simplemente la mensualidad aparentemente más baja. Registrar una ADR únicamente cuando se apruebe una elección arquitectónica duradera.

## 6. Arquitectura prevista al reactivar

```text
Venta confirmada (SalesPos)
           |
           v
Solicitud fiscal explícita y autorizada
           |
           v
ElectronicDocuments (snapshot + serie + identidad + estados)
           |
           v
Cola persistente PostgreSQL (claim, lease, intentos, idempotencia)
           |
           v
IElectronicDocumentProvider
       /          \
SUNAT directo   API/PSE/OSE seleccionado
       \          /
        v        v
Respuesta tipificada + evidencia + conciliación + auditoría
```

**Reglas innegociables:**

- La llamada externa ocurre **fuera** de la transacción comercial y fuera de las transacciones SQL de claim.
- Un error tributario nunca desconfirma una venta ni altera stock, caja, precios o comisiones.
- Un timeout tras el envío produce **resultado incierto**; no se reenvía a ciegas.
- HTTP 200 o una respuesta del proveedor no equivalen automáticamente a aceptación de SUNAT.
- Solo evidencia verificable puede respaldar estados de aceptación/rechazo oficiales.
- Idempotencia local no implica idempotencia en el servicio externo.
- Conservar historial de operaciones, documento original firmado, respuestas y CDR aplicables conforme a las obligaciones vigentes.
- Toda operación respeta tenant, entidad legal emisora, sede, roles, auditoría y privilegios mínimos.
- Las credenciales/certificados son por emisor, gestionados de manera segura y nunca guardados en código ni logs.
- Las anulaciones comerciales no simulan automáticamente bajas, notas de crédito o anulaciones tributarias.

La validación y generación fiscal debe derivarse de los requisitos reales vigentes, **no** del perfil provisional `preparation_v1` de B7.2 ni de la importación de facturas de proveedores.

## 7. Secuencia futura de implementación

### Etapa A — Contrato y decisión

Confirmar la habilitación tributaria, seleccionar SUNAT directo o proveedor API, obtener especificación/versiones, identificar flujos obligatorios y diseñar pruebas de contrato. Corregir cualquier semántica de simulación que pueda parecer un resultado fiscal real.

**Salida:** decisión documentada, riesgos resueltos y criterios técnicos verificables.

### Etapa B — Perfil fiscal y seguridad

Implementar el formato tributario requerido, identificación de emisor/receptor, series válidas, impuestos y redondeos conforme a reglas verificadas, firma/certificado si aplica, controles de acceso y custodia.

**Salida:** documentos de prueba reproducibles y verificables; sin emisiones de clientes reales.

### Etapa C — Adaptador e integración de pruebas

Implementar el adaptador elegido sin tocar el núcleo comercial: envío, consulta de estados, parsing de respuestas, recepción de CDR/evidencia, clasificación de fallos, cola y conciliación. Probar duplicados, reintentos, caídas, autorizaciones y aislamiento.

**Salida:** pruebas de contrato y validación real en sandbox, distinguidas de mocks.

### Etapa D — Piloto y activación gradual

Activar en **una entidad emisora autorizada**, con métricas, alertas, procedimiento de recuperación, soporte y conservación documental. Verificar numeración, respuestas, errores, plazos y documentos especiales. Luego permitir habilitación optativa por entidad legal, mediante configuración explícita.

**Salida:** aceptación formal del piloto antes de ampliar a otros clientes.

## 8. Criterios de finalización futura

No declarar lista la emisión electrónica hasta contar, al menos, con:

- Contrato técnico real y cumplimiento tributario aplicable revisados.
- Emisor piloto habilitado y entorno de pruebas autorizado.
- Generación/firma/documentación tributaria válida para los tipos elegidos.
- Recepción, interpretación y conservación verificables de estados y CDR.
- Recuperación segura de resultados inciertos y prevención de duplicados.
- Pruebas reales PostgreSQL (migraciones, RLS, concurrencia, colas y rollback).
- Pruebas de seguridad, segregación de credenciales y autorización.
- Evidencia de pruebas sandbox y piloto con criterios aprobados.
- Mecanismo para apagar la integración sin detener el POS.

## 9. Relación con el roadmap

- **B7.1:** importación de XML UBL de proveedores — independiente de la emisión.
- **B7.2:** núcleo de documentos electrónicos — implementado localmente; activación productiva y validación PostgreSQL pendientes.
- **B7.3:** proveedor/sandbox — **DIFERIDO por decisión de producto**; se reactiva cuando se cumpla la sección 4.
- **B7.4:** evaluación SIRE — iniciativa distinta y opcional; no debe incluirse automáticamente en B7.3.
- **M8:** continuar con seguridad, rendimiento, respaldos, observabilidad y aceptación del MVP sin exigir emisión electrónica.

**Regla de alcance:** este documento preserva la intención y los límites de una integración futura. **No es una orden de implementar B7.3 ahora** y no sustituye `docs/BACKEND_PLAN.md`, `docs/SPEC.md` ni los ADR aprobados.

---

**Última decisión de producto:** 12 de octubre de 2026.  
**Revisión pendiente:** cuando exista demanda verificable o cambien las obligaciones tributarias aplicables.
