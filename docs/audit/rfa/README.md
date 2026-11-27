# Auditoría funcional · AS-IS y TO-BE

## Corte vigente · 27/11/2026

**RFA4A_CORRECCION_CANDIDATA** para F-008 sobre `2248381ffcd808533f2f8204ff8aeece4ce028cd`, pendiente de CI aislado. RFA3 aprobada documentalmente en `e1df3ec9e9fb1b346118a1b168450cf832862801`. RFA0–RFA1 inventariados. **RFA2_CIERRE_CON_LIMITACIONES**, cobertura funcional parcial. Los 156 UC en nueve familias tienen 2 E1, 25 E2, 102 E3, 27 E4 y 0 E5. Los veredictos se mantienen en 19 PASS, 135 BLOCKED, 1 NOT_RUN y 1 N/A. [Evidencia por familia](VALIDATION.md#run-20) · [Evidencia de F-008](VALIDATION.md#run-23) · [Correctivo](VALIDATION.md#run-24)

**Gates independientes.** P0–P4 y P5 SEED/SEARCH PASS en RUN-23. P5 global FAIL por alta prematura durante REVIEW. Frontend integral 645/646, E5 pendiente y producción NO-GO. [RUN-23](VALIDATION.md#run-23) · [Checkpoint](CHECKPOINT.md)

## Cómo usar esta radiografía

1. Localiza el ID por pantalla, actor, ruta, endpoint o job en [USE_CASES](USE_CASES.md). La fila de [MATRIX](MATRIX.csv) aporta actor, entrada, alcance de implementación, requisito, fuente de pruebas y veredicto. Abre `flow_ref` para ver los pasos del usuario y el circuito técnico. Las notas compartidas de cada familia aplican a sus fichas.
2. Si vas a cambiar o retirar un flujo, parte de los símbolos de la ficha. Comprueba el router y enlaces directos, clientes API y otros consumidores, trabajos asíncronos, constancias y recuperaciones, tablas y referencias históricas. Usa el mapa de dependencias de abajo y contrasta el símbolo con el SHA actual. Una ruta interna, histórica o sin menú puede seguir siendo consumida. [Cruce de rutas y usos internos](USE_CASES.md#cruce-router--casos)
3. Verifica permiso, tenant, sede, licencia, versión, CSRF/RLS, atomicidad, idempotencia, saldos, movimientos y auditoría según apliquen. Los requisitos y límites canónicos están en [SPEC](../../SPEC.md), [ARCHITECTURE](../../ARCHITECTURE.md) y [ADR](../../ADR.md). Registra una frontera sin rastreo como **no determinada**. La visibilidad de una acción no prueba autorización.
4. Para una regresión, usa `test_ref` y [VALIDATION](VALIDATION.md) para localizar la aserción y distinguir doble, HTTP, PostgreSQL y navegador. Elige caso feliz, denegación, recarga o reintento y efectos persistidos según riesgo. Si falta una prueba dedicada, la matriz no la da por cubierta. La aceptación del operador es E5.
5. Lee [FINDINGS](FINDINGS.md) para la causa y evidencia, [TO_BE](TO_BE.md) para la intención de diseño y [ROADMAP](ROADMAP.md) para correcciones, candidatos y dependencias futuras. El [CHECKPOINT](CHECKPOINT.md) identifica lo autorizado para reanudar.

## Estado del código y de la aceptación

| Estado | Alcance |
|---|---|
| Correcciones acreditadas en recorridos concretos | L-01 Sencillo con Owner y L-06A Caja/POS, incluido Cierres. L-05 estabilizó dos pruebas y añadió identificación saneada. Conservan las variantes pendientes. |
| Candidato y evidencia publicada | L-06B incluye filtros de Mis productos acreditados en SEARCH y recuperación local del alta acreditada con dobles. RUN-23 constató un envío antes de Guardar. El correctivo local separa Continuar de Guardar/Asociar y espera B-02. [F-008](FINDINGS.md#f-008--el-piloto-de-alta-local-falla-en-la-pérdida-inducida-del-acuse) |
| Backlog | Compras/Inventario y los demás cortes L-06C–I, medición F-005, gate integral y E5. Las propuestas no cambian el contrato vigente. [Fases y lotes](ROADMAP.md) |

### Referencias históricas del AS-IS

Baseline de cierre `939ad83a5da8de3ceb7d4308879f6ac470546b9f`, rama `ci/postgres-diagnostics-8bc6b4c`, 26/11/2026 Lima. El árbol estaba limpio al reanudar y coincidía con el publicado auditado. Se conserva el inventario original capturado sobre `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6` y sus cambios UAX registrados, incluida la huella del HTML `68cb1c38d5fbe3a07d59479aae3d0e2772215b0194baab178797b825f8d429cf`. Artefacto servido y esquema real NO_DETERMINADO. [SPEC](../../SPEC.md), [ARCHITECTURE](../../ARCHITECTURE.md) y [ADR](../../ADR.md) son las fuentes canónicas.

[Inventario](USE_CASES.md) · [Matriz acumulativa](MATRIX.csv) · [Validación RFA2](VALIDATION.md) · [Hallazgos](FINDINGS.md) · [Continuidad](CHECKPOINT.md)

## Cobertura estática de Fase 01

**FASE_01_COMPLETA (historial RFA0–RFA1)**. Se reconciliaron 27/27 declaraciones del router con casos o exclusiones y 123/123 contratos HTTP inventariados con fichas. Se trazaron los 74 E0 de la entrega previa y se añadió UC-REP-17 para reposición interna. En ese corte hubo 155/155 casos E1 y NOT_RUN, ocho diagramas y cinco hallazgos. La matriz refleja la evidencia acumulada posterior.

| Familia | UC AS-IS histórico / inventario actual |
|---|---:|
| [Caja/POS](flows/01-caja-pos.md) | 18/19 |
| [Catálogo](flows/02-catalogo.md) | 22/22 |
| [Compras e inventario](flows/03-compras-inventario.md) | 15/15 |
| [Transferencias](flows/04-transferencias.md) | 13/13 |
| [Reportes y comisiones](flows/05-reportes-comisiones.md) | 17/17 |
| [Avisos/auditoría](flows/06-avisos-auditoria.md) | 7/7 |
| [Acceso y estructura](flows/07-acceso-personal.md) | 27/27 |
| [Plataforma](flows/08-plataforma.md) | 18/18 |
| [Contingencia e integraciones](flows/09-contingencia-integraciones.md) | 18/18 |

UC-CAJ-19 se añadió con el historial de Cierres. Las garantías de comportamiento se consultan por escenario y SHA en [VALIDATION](VALIDATION.md). El entorno DEV servido sigue sin verificación en este cierre.

## Dependencias funcionales

- Identidad, membresía, licencia y sede habilitan el espacio de trabajo. La autoridad de plataforma es independiente y la designación del primer Owner vincula ambos dominios mediante el login verificado.
- Catálogo y presentaciones alimentan POS, Compras y Transferencias. La recepción genera lotes y movimientos, y la confirmación de venta consume stock y produce pagos. La anulación compensa con registros auditados.
- Caja calcula efectivo físico desde pagos/reversiones y préstamos. El arqueo por ID y el directorio de abiertas son consultas distintas. Reportes agregan ventas/stock; comisiones contabilizan con la venta y su reversión.
- Avisos derivan de lotes y registran lectura por membresía. La auditoría muestra metadatos. El módulo offline usa catálogo de sede, dispositivo y acuses originales, y CPE queda desacoplado.

## Cortes históricos de RFA2

**FASE_02_PARCIAL** sobre `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`. Los 155 UC están clasificados en 9/9 familias y 12 escenarios. Evidencia más alta por caso: 27 E1, 128 E2, 0 E3 y 0 E4. Veredictos: 1 PASS, 1 FAIL, 148 BLOCKED, 4 NOT_RUN y 1 N/A. Los PASS parciales de ejecución se detallan aparte. Falta evidencia real de persistencia, concurrencia, RLS y reentrada en navegador aislado. [Ejecuciones y reanudación](VALIDATION.md).

En 02B, `5d38898` conserva el código y los tests del corte anterior. RUN-09 amplía F-005 en DOM con varios IDs y cambio de sede. La incorporación de E3 está pendiente del resumen del run del operador. La clasificación permanece **FASE_02_PARCIAL**.
