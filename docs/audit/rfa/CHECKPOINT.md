# Checkpoint RFA

## FASE_02_PARCIAL · RFA2

- Preflight 26/11/2026 Lima. HEAD `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f` en `ci/postgres-diagnostics-8bc6b4c`, sin diferencias rastreadas. Se conservan los dos MD del operador sin rastrear. Docker no disponible en PATH, sin destino PostgreSQL aislado acreditado. E3/E4 bloqueados y DEV excluido. Fixture revisado: crea su propio Testcontainers, aplica migraciones allí y utiliza roles de prueba. Próxima acción: piloto E2 de Caja, antes de Sencillo.
- F-001. CashPage en jsdom, 12 PASS, 0 FAIL, 0 SKIP. Se ejercitó confirmación, cierre incierto, reconsulta y alcance con respuestas simuladas. Secuencia real 100→10, persistencia y reentrada por navegador requieren E3/E4. Siguiente: Sencillo, comparar el doble heredado con el contrato de caja abierta.
- F-002. Sencillo heredado 14 PASS y caracterización DOM fiel del campo nulo 1 PASS, 0 FAIL, 0 SKIP del runner. Veredicto comercial de la variante DOM: FAIL, botón deshabilitado y cero POST pese a caja abierta y saldo inicial positivo. Persistencia y E4 pendientes. Siguiente: POS y lotes agrupados de las nueve familias.
- POS: RUN-03, 42 PASS DOM. Confirmación, pérdida de respuesta, ticket y anulación ejercitados con doble. E3/E4 pendientes.
- Catálogo: RUN-03, 35 PASS. F-003 reproducido como filtro de página en RUN-04, sin incumplimiento acreditado.
- Compras / Inventario: RUN-03, 44 PASS. RUN-04 confirma acceso de compra sin referencia por ID y 1 GET de nombre en frío, sin nuevas lecturas al cambiar a Lotes con el mismo ID. Pago, stock, RLS y rollback requieren B-01.
- Mercadería: RUN-03, 15 PASS y 1 FAIL de sincronización de una aserción. RUN-05 permite observar el caso PASS aislado. Custodia, concurrencia y acuses reales pendientes.
- Reportes / Comisiones: RUN-03, 21 PASS. Auditoría / Avisos: 15 PASS. Ambos E2, reconciliación de agregados, deduplicación y persistencia pendientes.
- Acceso: RUN-03, 54 PASS y RUN-06 añade 14 PASS de invitaciones. Plataforma: RUN-03, 27 PASS y un FAIL de lectura en curso. El mismo caso pasa en RUN-05. Ninguna prueba usó Google real ni concedió autoridad operacional.
- Contingencia: RUN-03, 5 PASS y RUN-06 añade el PASS de exclusión de APIs en la caché del shell. Restan E3/E4 y controles de Recovery/integraciones.
- RUN-07 cierra el hueco DOM de F-001: un POST de 10, faltante 90 en acuse simulado, URL limpia sin historial y GET por dirección original. Fila, importe calculado y auditoría reales quedan bloqueados.
- .NET: salida segregada bajo obj evitó archivos ocupados sin detener procesos. VSTest abortó por conexión testhost; RUN-08 en proceso xUnit, 688 PASS, 0 FAIL, 0 SKIP. Frontend total: 306 PASS, 2 FAIL y 0 SKIP ejecutados, con los dos fallos verdes en el recheck estricto. F-006 registra el riesgo de repetibilidad sin adjudicar un defecto de aplicación.
- Gate acumulativo: 155 UC, 128 E2 y 27 E1, E3/E4 cero. 148 BLOCKED, 4 NOT_RUN, 1 N/A, 1 PASS y 1 FAIL. Doce escenarios en 9/9 familias, seis hallazgos. Bloqueos B-01/B-02 en [VALIDATION](VALIDATION.md). Pendiente de revisión del operador.
- Siguiente acción exacta: una vez disponible Docker en un runner autorizado, verificar aislamiento y ejecutar el comando PostgreSQL piloto de VALIDATION. Empezar por F-001 con caja sintética 100→10 y luego F-002 con el GET real de caja abierta y despacho/recepción. Recuperar primero E3/E4 sobre esos mismos IDs. RFA3 requiere otro encargo.

## Cierre previo

**FASE_01_COMPLETA · RFA0–RFA1.** Baseline de cierre `939ad83a5da8de3ceb7d4308879f6ac470546b9f`, rama `ci/postgres-diagnostics-8bc6b4c`. Árbol limpio y sin diferencias con la referencia al iniciar. Orden de trabajo original Sol/Medium. Inventario inicial preservado desde `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6` con cambios UAX registrados en el [inventario](USE_CASES.md).

- Caja/POS quedó en 18/18 E1. Se añadió solo la referencia de políticas SQL para confirmación. La reanudación comenzó en UC-CAT-08, después cerró Compras, Transferencias, Reportes, Avisos, Acceso, Plataforma y Contingencia. Los 74 E0 iniciales se trazaron y reposición interna se registró como UC-REP-17.
- Gate estático: 9/9 familias, 27/27 declaraciones del router mapeadas o justificadas y 123/123 contratos HTTP en inventario, ficha y matriz. 155/155 IDs únicos con E1 y NOT_RUN, 0 E0. Los checks cotejaron nivel, estado, enlaces, referencias de pruebas y cinco IDs de hallazgo. Se cuentan ocho Mermaid por inspección del texto.
- Artefactos: [cobertura y fuentes canónicas](README.md), [inventario](USE_CASES.md), [matriz](MATRIX.csv), nueve fichas accesibles desde la cobertura y [hallazgos con recetas](FINDINGS.md). Las referencias a tests no aportan ejecución. Build servido, datos reales y esquema activo no se determinaron.
- Siguiente acción exacta: revisión documental por el operador. Solo con nuevo encargo de RFA2, retomar primero F-001 desde la [receta de Caja](flows/01-caja-pos.md), comparar listado de activas, consulta por ID y arqueo para el caso S/100 → S/10 sin movimientos en un entorno aislado autorizado. Después contrastar F-002 con caja abierta, saldo positivo y consulta de estado del servidor. F-001 y F-002 tienen confirmación estática externa y reproducción pendiente.
