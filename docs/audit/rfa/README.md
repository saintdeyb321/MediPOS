# Radiografía AS-IS · Fase 01

Baseline de cierre `939ad83a5da8de3ceb7d4308879f6ac470546b9f`, rama `ci/postgres-diagnostics-8bc6b4c`, 26/11/2026 Lima. El árbol estaba limpio al reanudar y coincidía con el publicado auditado. Se conserva el inventario original capturado sobre `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6` y sus cambios UAX registrados, incluida la huella del HTML `68cb1c38d5fbe3a07d59479aae3d0e2772215b0194baab178797b825f8d429cf`. Artefacto servido y esquema real NO_DETERMINADO. [SPEC](../../SPEC.md), [ARCHITECTURE](../../ARCHITECTURE.md) y [ADR](../../ADR.md) son las fuentes canónicas.

[Inventario](USE_CASES.md) · [Matriz acumulativa](MATRIX.csv) · [Validación RFA2](VALIDATION.md) · [Hallazgos](FINDINGS.md) · [Continuidad](CHECKPOINT.md)

## Cobertura

**FASE_01_COMPLETA (historial RFA0–RFA1)**. Se reconciliaron 27/27 declaraciones del router con casos o exclusiones y 123/123 contratos HTTP inventariados con fichas. Se trazaron los 74 E0 de la entrega previa y se añadió UC-REP-17 para reposición interna. En ese corte hubo 155/155 casos E1 y NOT_RUN, ocho diagramas y cinco hallazgos. La matriz refleja la evidencia acumulada posterior.

| Familia | E1 / inventario |
|---|---:|
| [Caja/POS](flows/01-caja-pos.md) | 18/18 |
| [Catálogo](flows/02-catalogo.md) | 22/22 |
| [Compras e inventario](flows/03-compras-inventario.md) | 15/15 |
| [Transferencias](flows/04-transferencias.md) | 13/13 |
| [Reportes y comisiones](flows/05-reportes-comisiones.md) | 17/17 |
| [Avisos/auditoría](flows/06-avisos-auditoria.md) | 7/7 |
| [Acceso y estructura](flows/07-acceso-personal.md) | 27/27 |
| [Plataforma](flows/08-plataforma.md) | 18/18 |
| [Contingencia e integraciones](flows/09-contingencia-integraciones.md) | 18/18 |

Los resultados de comportamiento, la reproducción de los hallazgos, el estado del entorno en ejecución y las garantías con PostgreSQL real quedan pendientes de la fase que autorice verificarlos. Cuando no se cita una prueba específica en la ficha o matriz, no se atribuye cobertura dedicada al caso.

## Dependencias funcionales

- Identidad, membresía, licencia y sede habilitan el espacio de trabajo. La autoridad de plataforma es independiente y la designación del primer Owner vincula ambos dominios mediante el login verificado.
- Catálogo y presentaciones alimentan POS, Compras y Transferencias. La recepción genera lotes y movimientos, y la confirmación de venta consume stock y produce pagos. La anulación compensa con registros auditados.
- Caja calcula efectivo físico desde pagos/reversiones y préstamos. El arqueo por ID y el directorio de abiertas son consultas distintas. Reportes agregan ventas/stock; comisiones contabilizan con la venta y su reversión.
- Avisos derivan de lotes y registran lectura por membresía. La auditoría muestra metadatos. El módulo offline usa catálogo de sede, dispositivo y acuses originales, y CPE queda desacoplado.

## RFA2 · Validación conductual

**FASE_02_PARCIAL** sobre `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`. Los 155 UC están clasificados en 9/9 familias y 12 escenarios. Evidencia más alta por caso: 27 E1, 128 E2, 0 E3 y 0 E4. Veredictos: 1 PASS, 1 FAIL, 148 BLOCKED, 4 NOT_RUN y 1 N/A. Los PASS parciales de ejecución se detallan aparte. Falta evidencia real de persistencia, concurrencia, RLS y reentrada en navegador aislado. [Ejecuciones y reanudación](VALIDATION.md).
