# Checkpoint RFA

## RFA4A · candidato de conciliación CAT-21

**RFA4A_CONCILIACION_CANDIDATA** sobre `55de01125954154f0a67ad61ff5ca812e760f14b`. [RUN-25](VALIDATION.md#run-25) acredita F-008 corregido en el alta local y CAT-21 hasta reentrada. [RUN-26](VALIDATION.md#run-26) registra la caracterización local y las etiquetas acotadas. CAT-21 E4 parcial/BLOCKED. Los 156 UC conservan sus veredictos.

El operador publica una sola vez en `ci/postgres-diagnostics-8bc6b4c` para activar B-02. Revisar el resumen del SHA y parar en la primera frontera fallida o presentar P5 completo para revisión. Producción NO-GO.

## RFA4A · corrección candidata F-008

**RFA4A_CORRECCION_CANDIDATA** sobre `2248381ffcd808533f2f8204ff8aeece4ce028cd` en `ci/postgres-diagnostics-8bc6b4c`. [RUN-23](VALIDATION.md#run-23) acredita el alta prematura. [RUN-24](VALIDATION.md#run-24) documenta el RED/GREEN de identidad de botón, confirmación y recuperación. Los 156 UC conservan sus veredictos. CAT-09 sube a E4 por el fallo real del baseline, con el candidato aún BLOCKED. CAT-21 permanece E2/BLOCKED.

**Siguiente acción del operador.** Publicar el candidato una vez en la rama diagnóstica. El push de la prueba E2E activa B-02. Revisar el resumen del nuevo SHA y conciliar por separado F-008 y el gate global de P5. RFA4B y L-06C–I siguen pendientes. Producción NO-GO.

## RFA4A · candidato F-008

RFA3 aprobada en `e1df3ec9e9fb1b346118a1b168450cf832862801`. **RFA4A_CANDIDATO** sobre ese HEAD en `ci/postgres-diagnostics-8bc6b4c`. [RUN-22](VALIDATION.md#run-22) registra la compilación, la validación del filtro y el alcance del arnés. Chromium/API/PostgreSQL NOT_RUN local por ausencia de Docker. Los 156 UC conservan sus niveles y veredictos. F-008 sigue abierto.

**Siguiente acción del operador.** Publicar el candidato en esa rama. El push de los cambios del arnés activa B-02 una sola vez. Revisar el resumen saneado del SHA publicado, desde P0 hasta P5, incluidos el recuento de POST y la lectura runtime. Detenerse en la primera frontera fallida. RFA4B, L-06C–I y producción quedan fuera de este intento.

## RFA3 · Candidato de cierre

- **RFA3_CANDIDATO_CIERRE.** Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD `1deb4dae9ce5f11c4f9fc0f374837c5d67e1d1d4`. El diff desde `c521e9c5019b25371d45b9a661bb90588554e389` tiene 15 archivos de arnés y documentos. Sin cambios de aplicación frente a `52a5ced168dbef2e80145b4e9d115113f0a63755`. El MD de encargo preexistente sigue aparte.
- **RFA2_CIERRE_CON_LIMITACIONES.** [RUN-21](VALIDATION.md#run-21) concilia P0–P4 y P5 SEED/SEARCH. 156 UC, nueve familias, 2 E1 / 25 E2 / 103 E3 / 26 E4 / 0 E5. Veredictos sin cambios. P5 FAIL en CREATE/POST_LOST_ACK y etapas posteriores no alcanzadas. Aserción individual y causa indeterminadas. F-007 sigue E2.
- Análisis focalizado del arnés y contrato de alta, con los principios de ub-mode MIT ya leídos en RUN-20 (`3da62bf3fcfd2034de41f30d920d2739e566171f`). Sin instalación, pruebas ejecutadas ni CI. [F-008](FINDINGS.md#f-008--el-piloto-de-alta-local-falla-en-la-pérdida-inducida-del-acuse)
- **Checks documentales PASS:** índice/matriz/fichas, decisión TO-BE para 156 UC, nueve familias, ocho hallazgos, 668 enlaces locales y `git diff --check`.
- **Reanudación:** el operador revisa y decide si autoriza [el primer encargo RFA4A](ROADMAP.md#primer-encargo-rfa4a) en un runner aislado. Un solo intento de diagnóstico de alta con contraste de persistencia posterior y conservación de P0–P4. Frontend 645/646 de otro SHA, DEV sin atribución, E5 pendiente y producción NO-GO. L-06C–I siguen sin abrirse.

## RFA2 · Candidato de cierre anterior a RUN-21

- **RFA2_CANDIDATO_CIERRE**, aún parcial. Rama `ci/postgres-diagnostics-8bc6b4c`. HEAD documental `c521e9c5019b25371d45b9a661bb90588554e389`, aplicación `52a5ced168dbef2e80145b4e9d115113f0a63755`. [RUN-20](VALIDATION.md#run-20) detalla nueve familias, alcance, resultados y límites.
- 156 UC, 2 E1 / 28 E2 / 103 E3 / 23 E4 / 0 E5. AVI-05 sube a E2 en el detalle aislado y mantiene BLOCKED para la validación real. Veredictos acumulados: 19 PASS / 135 BLOCKED / 1 NOT_RUN / 1 N/A. CAT-22 N/A e INT-16 NOT_RUN.
- El operador publicó la instrumentación y suministró el resumen del único run. [RUN-21](VALIDATION.md#run-21) registra el resultado y el límite alcanzado.

## Cierre documental RFA0–RFA3 · 27/11/2026

- **RADIOGRAFÍA_DOCUMENTAL_CONSOLIDADA.** Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD `52a5ced168dbef2e80145b4e9d115113f0a63755`. Se conserva el MD de L-06C aportado por el operador fuera de la carpeta de trabajo documental. [Entrada canónica](README.md).
- RFA0–RFA1 inventariados y RFA3 consolidada. RFA2 **PARCIAL**: 156/156 UC, nueve familias, 3 E1 / 27 E2 / 103 E3 / 23 E4 / 0 E5. Veredictos: 19 PASS / 135 BLOCKED / 1 NOT_RUN / 1 N/A. El cotejo documental con Python verificó los IDs de índice/matriz/fichas, estados, clasificación TO-BE, lotes asociados, F-001–007 y 631 enlaces locales. `git diff --check` PASS.
- [RUN-19](VALIDATION.md#run-19) acredita P0–P4 del SHA publicado. P5 falló sin aserción identificada. Frontend integral 645/646, DEV sin atribución, variantes por rol y E5 pendientes. Producción **NO-GO**.
- **Reanudación:** esperar instrucción explícita del operador que identifique fase, UC y alcance. Para investigar P5, pedir solo evidencia saneada de la primera aserción fallida o una reproducción aislada autorizada con SHA/build comprobados. No iniciar diagnóstico ni abrir L-06C–I con este cierre.

## RFA4 · L-06B · corte local previo a RUN-19

- Gate de entrada satisfecho con GO explícito, resumen sanitizado y HEAD `5a94de91a17592d5d3739426db8ec08223976758`. [RUN-17](VALIDATION.md#run-17) concilia P0–P4 por UC. L-06A acreditado para el recorrido ejecutado. Frontend integral y DEV siguen pendientes.
- **L-06B_CANDIDATO.** Filtros de texto, categoría y estado en servidor antes de paginar, recuperación de alta con el código original y P5 preparado. [RUN-18](VALIDATION.md#run-18) registra los 22 UC, F-003/F-007, verificaciones y variantes pendientes.
- El operador publicó el candidato. El resultado posterior quedó registrado en RUN-19 y las acciones operativas quedaron suspendidas.

## RFA4 · L-06A · corte local anterior a RUN-17

- Base `c26ee5090ebbc2d48a27bb25b8817554f6ec7adf`. [RUN-15](VALIDATION.md#run-15) reconcilia el CI B-02 45933239638 P0–P3 con PostgreSQL 18.7 y navegador real. L-01 acreditado para Owner. F-001 demuestra brecha de entrada limpia y persistencia por ID. DEV sigue sin atribución.
- **L-06A_CANDIDATO.** GET y pestaña Cierres (nuevo UC-CAJ-19), protección del borrador vaciado y pilotos P3/P4. [RUN-16](VALIDATION.md#run-16) incluye tabla de 19 UC, RED/GREEN, pruebas focalizadas y alcance pendiente. P3/P4 del candidato están NOT_RUN por Docker ausente. RFA2 parcial y gate frontend 645/646.
- El operador publicó la revisión de L-06A y suministró el resultado saneado de P0–P4. La entrada de L-06B y las variantes pendientes quedan registradas en RUN-17.

## RFA4 · B-02

- Baseline `781277c0032e8d4f364893191137254b3e024c2e`. Composición reutilizable implementada en `backend/tests/MediPOS.E2E`. Reutiliza fixture, seed, host y cookie/CSRF. Pilotos secuenciales P0→P3 preparados. [RUN-14](VALIDATION.md#run-14).
- **B02_CANDIDATO_PENDIENTE_RUNNER.** Restore en modo bloqueado por lockfile, compilaciones y preflight local aprobados. P0–P3 NOT_RUN por Docker ausente. L-01 pendiente GET HTTP+PostgreSQL. F-001 mantiene E3 exacta de RUN-13 y reentrada E4 pendiente. Frontend permanece en 645/646 en RUN-13. RFA2 parcial.
- Próximo paso del operador: revisar y publicar el candidato. Ejecutar **B-02 isolated browser pilot** sobre su SHA si el workflow ya está registrado en la rama por defecto, o seguir los comandos del [arnés](../../../backend/tests/MediPOS.E2E/README.md) en runner Linux aislado. Reconciliar los hitos y efectos por UC antes de autorizar L-06A.

## RFA4 · L-05B y cobertura global

- HEAD `e2ffd12f577234c943237e71361a8ee0716a8eb8`, rama `ci/postgres-diagnostics-8bc6b4c`. RUN-13 importado del MD. PostgreSQL 18.7 acredita el caso exacto F-001 y el estado L-01 por `StatusAsync`. Pendientes GET respaldado por PostgreSQL y B-02/E4. CI frontend 645/646.
- Compras 1/1 focalizado y 27/27 en su archivo. FAIL CI NOT_REPRODUCED, aserción fallida desconocida. Sin ajuste de prueba ni atribución al producto.
- RFA2 parcial, 155 UC, 119 E3 / 33 E2 / 3 E1 / 0 E4. L-06A–I definidos en ROADMAP, con L-02/03/04 incorporados a sus cortes y un habilitador común B-02 pendiente de autorización.
- Próximo paso exacto: revisar RUN-13 y recuperar solo la aserción fallida sanitizada del caso Compras si existe. El operador decide si combina un CI nuevo con el siguiente lote autorizado y si encarga B-02. Producción NO-GO.

## RFA4 · L-05 y evidencia de Caja

- HEAD de entrada `bc36ecc5b7b9e2e0cd4c52d6c8b9f00ab7790b76`. L-05 queda como candidato local. Los dos casos F-006 modificados pasan, con 44/44 en la repetición acotada. Se conservan los cinco fallos de otros casos de la primera corrida posterior y el fallo histórico sin nombre. Diagnóstico CI acotado validado con 11 fixtures. [RUN-12](VALIDATION.md#run-12).
- L-01 sigue `L01_CANDIDATO_PENDIENTE_CONTRATO_REAL`: HTTP del host con dobles PASS, prueba PostgreSQL NOT_RUN. F-001 exacto 100→10 compilado, NOT_RUN. E4 y causa DEV pendientes.
- Próxima acción del operador: revisar y publicar manualmente el candidato combinado y disparar **una ejecución nueva** de `Backend validation - PostgreSQL 18` sobre el SHA publicado. Reconciliar el resultado de ambas pruebas PostgreSQL y del job frontend. Luego acreditar el GET respaldado por PostgreSQL en aislamiento. L-02 requiere una decisión separada sobre alcance y permisos de historial.

## RFA4A · L-01

- Baseline `50b97a2c451a15ac59a21d06c11d266b81a77a68`; solo dos MD del operador sin rastrear al iniciar. Docker ausente. RED focalizado: caja abierta con esperado nulo dejó deshabilitado el botón en `toBeEnabled()`.
- **`L01_CANDIDATO_PENDIENTE_CONTRATO_REAL`.** Alternativa A implementada, 24/24 regresiones E2, lint/tipos y compilación aislada de tests .NET en PASS. [RUN-11](VALIDATION.md#run-11) conserva los resultados del baseline y separa contrato preparado de evidencia ejecutada. Próxima acción del operador: revisar el diff, ejecutar CI del candidato y verificar el GET aislado de estado abierto con JSON null siguiendo RUN-11. RFA2 sigue parcial y producción NO-GO.

## Corte RFA3 · FASE_03_COMPLETA para revisión del operador

26/11/2026. Rama `ci/postgres-diagnostics-8bc6b4c`, HEAD `5d3889806e11fea23a3ba19402c22c15635a1301`. El árbol de entrada contenía cambios documentales de 02B y los MD del operador sin rastrear. Se incorporó copia idéntica del resumen saneado que estaba en `docs/`. No hubo nueva ejecución. Gobierno, contratos, aplicación, tests y entornos operativos conservados.

- 9/9 familias y 155/155 UC clasificados en [TO_BE](TO_BE.md). [ROADMAP](ROADMAP.md) distingue el bugfix, las capacidades opcionales y las validaciones de los circuitos conservados. Seis hallazgos, un P1 reproducido a E2, sin P0 demostrado.
- RFA2 permanece **FASE_02_PARCIAL** conforme al corte inmediatamente inferior. E4 cero. Falta atribuir el FAIL frontend del run 45891472332. La falta de nombre no identifica un séptimo defecto.
- **Próxima acción exacta del operador:** revisar [L-01](ROADMAP.md) y aprobar, si procede, caracterización del GET de caja abierta en destino efímero y corrección mínima A en la guarda de Sencillo. Cualquier nuevo campo de saldo, directorio de arqueos/compras o búsqueda transversal precisa decisión independiente. RFA4/RFA5 siguen sin iniciar. **NO-GO producción.**

## Corte 02B · reconciliación cerrada, FASE_02_PARCIAL

HEAD y run coinciden en `5d3889806e11fea23a3ba19402c22c15635a1301`. RUN-10 importado después de RUN-09; 905/905 en 99 clases contra PostgreSQL 18.7, frontend 641/642 y fallo innominado. 3 E1, 34 E2, 118 E3. 133 BLOCKED, 1 FAIL, 1 N/A, 1 NOT_RUN, 19 PASS. Los 155 IDs se conservan. B-01 queda resuelto para las aserciones citadas en [VALIDATION](VALIDATION.md#run-10). Quedan el cierre exacto 100→10 y GET open de Sencillo, B-02 de navegador y B-03 externo. UC-TRA-01 conserva FAIL comercial E2 pese a la evidencia E3 del ledger. Sigue el diagnóstico RFA3 autorizado.

## Continuación 02B · preflight

- HEAD `5d3889806e11fea23a3ba19402c22c15635a1301`, rama `ci/postgres-diagnostics-8bc6b4c`. Sin cambios rastreados al iniciar. Entre `2c54e1f7` y este SHA solo cambió documentación, por lo que RUN-01–08 conservan su alcance. Se preservan los dos MD de 02B sin rastrear.
- El encargo no incluye URL ni resumen del run nuevo. Solicitado exclusivamente el resumen sanitizado. Docker ausente en PATH y en la ubicación estándar, sin destino aislado acreditado. Mantener B-01/B-02 y el FAIL E2 de Sencillo hasta incorporar evidencia válida. Trabajar mientras tanto en el contraste de aserciones y F-005 multi-ID.
- RUN-09 / F-005, 1 PASS, 0 FAIL, 0 SKIP. Tres GET en frío, cero en la vista de Lotes con caché vigente y tres GET de otra sede. Límite E2. Se cotejaron candidatos de Caja/Sencillo y los cuatro NOT_RUN sin importar resultados aún.
- **FASE_02_PARCIAL.** 155 casos, 128 E2, 27 E1, 0 E3/E4. Próximo paso exacto al recibir el resumen: conciliar SHA, attempt, motor y clases completas, vincular primero aserciones de Caja/Sencillo en VALIDATION y reflejar el alcance por UC. Conservar los huecos de 100→10 y del GET `open` si el resumen no los demuestra. E4 requiere B-02. RFA3 solo podría planificarse de forma condicionada tras revisión del operador. Producción NO-GO.

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
