# MediPOS — Fase 3 / RFA3 · Reconciliación final de causas y arquitectura TO-BE
**v1.0 · 27/11/2026 · GPT-6.2 Astra / Alto / Standard**

## 1. Mandato y baseline
GO explícito para cerrar el diagnóstico RFA2 y ejecutar únicamente RFA3 documental sobre rama `ci/postgres-diagnostics-8bc6b4c`, HEAD publicado `1deb4dae9ce5f11c4f9fc0f374837c5d67e1d1d4`, aplicación sin cambios respecto de `52a5ced168dbef2e80145b4e9d115113f0a63755`. El diff contra `c521e9c5019b25371d45b9a661bb90588554e389` contiene 15 archivos: instrumentación del arnés y workflow, orden de trabajo RFA2 y documentación RFA; no código productivo. Comprobar HEAD, status/diff y conservar trabajo previo. Si hay diferencias documentales compatibles, registrar y continuar; si cambió código relevante, acotar el impacto y no trasladar resultados automáticamente. Prevalecen AGENTS, SPEC, ARCHITECTURE, ADR y `docs/MEDIPOS_FUNCTIONAL_ARCHITECTURE_AUDIT.md` v1.1. Carpeta única de entrega `docs/audit/rfa/`. No repetir inventario, diagnóstico completo ni suites. No implementar RFA4 ni abrir L-06C–I. El objetivo es entregar el plan ejecutable de corrección y simplificación basado en evidencia, no conseguir CI verde en esta fase.

## 2. Evidencia CI suministrada y verificada directamente
Run https://github.com/saintdeyb321/MediPOS/actions/runs/46015549281 · job `140561621805` · intento 1 · push · 27/11/2026 14:23:50–14:27:39 UTC (229 s entre creación y última actualización). Conclusión `failure`, exit code 1; checkout, herramientas, build React, restore/build E2E e instalación Chromium exitosos; fallo en `Run preflight and P0–P5`. SHA coincide con HEAD remoto verificado. Extracto sin duplicación de líneas:

```text
B02 SHA 1deb4dae9ce5f11c4f9fc0f374837c5d67e1d1d4 BUILD e40191caae1b9b7295a27b34b2b3c9489d4a61cc1484178ae3d73c17d0985203
B02 P0 START
B02 POSTGRES 180007 ORIGIN https://localhost:34003 ROLE medipos_test_runtime
B02 P0 PASS
B02 P1 START
B02 P1 PASS
B02 P2 START
B02 P2 PASS
B02 P3 START
B02 P3 PASS CASH_HISTORY_AND_RECOVERY
B02 P4 START
B02 P4 PASS POS_PAY_TICKET_VOID
B02 P5 START
B02 P5 DIAG SEED START
B02 P5 DIAG SEED PASS
B02 P5 DIAG SEARCH START
B02 P5 DIAG SEARCH PASS
B02 P5 DIAG CREATE START
B02 P5 DIAG CREATE FAIL POST_LOST_ACK
B-02 failed or was incomplete. Unreported phases are unverified.
```

Importar como RUN-21 (si ya existe, siguiente ID libre, sin duplicar). El build hash coincide con RUN-19; no sustituye la identidad completa del código ni permite inferir el estado DEV. P0–P4 conservan evidencia por aserción. SEED y SEARCH acreditan ejecución real de sus bloques; en `CashPilotTests.Catalog.cs` comprobar orden y alcance de aserciones: 51 productos, página sin filtros, ingrediente/categoría/estado, comodines literales, negativos de rol/tenant, exclusión del inactivo en consulta operativa, limpieza de filtros y consulta del otro tenant. Promover a E4 únicamente fronteras de navegador/API/PG efectivamente recorridas, p. ej. CAT-06/20 si sus aserciones lo justifican; conservar variantes, veredictos BLOCKED cuando corresponda y no afirmar toda la familia PASS. CREATE/FORM se alcanzó antes del paso marcado; no implica POST confirmado. CREATE/POST_LOST_ACK falla; REENTRY y RECONCILIATION no alcanzadas, EDIT/ASSOCIATION/IMPORT/PERSISTENCE NOT_RUN en esta ejecución. P5 global FAIL. No inferir número de escrituras, commit, 201, duplicación o corrupción del bloque fallido. F-007 conserva evidencia RED/GREEN local, no causa atribuida de P5.

## 3. RFA2: cierre de investigación acordada, no certificación
Antes de RUN-21: 156 UC, 2 E1 / 28 E2 / 103 E3 / 23 E4 / 0 E5; 19 PASS, 135 BLOCKED, 1 NOT_RUN, 1 N/A. RUN-20: 6 Vitest + 17 xUnit con dobles PASS, AVI-05 subió a E2; el caso temporal fue retirado, conservar detalle de reproducción. CAT-22 E1/N/A, INT-16 E1/NOT_RUN. Restore bloqueado local falló, build con assets previos pasó; CI nuevo sí acredita restore/build del arnés. No hay Docker local, DEV sin atribución, E5 pendiente, gate integral histórico 645/646 del run 45915477001 (otro SHA). Recalcular matriz tras RUN-21. Declarar `RFA2_CIERRE_CON_LIMITACIONES` si se reconcilia la evidencia prevista y se explicitan fronteras por UC/familia; RFA2 PARCIAL en cobertura funcional, no promover todo a PASS ni exigir E4 universal. No más CI diagnóstico en RFA2. Bloqueos de release pasan a backlog, no desaparecen.

## 4. RFA3: análisis diferencial y causa raíz
Leer CHECKPOINT, RUN-18–21, FINDINGS, TO_BE, ROADMAP y filas/fichas afectadas. No releer 156 fichas completas ni repetir la radiografía ya consolidada. Revisar las nueve familias por riesgos y dependencias usando las decisiones existentes; corregir solo incoherencias o huecos demostrados. Para P5 trazar `CashPilotTests.Catalog.cs:192–225` y `CashPilotTests.Pos.cs:173–198` (`LosePostAfterCommitAsync`): ruta exacta, clic, captura del POST, `route.FetchAsync`, status esperado 201, extracción del ID, abort del acuse, propagación/timeout, aserción de botón deshabilitado. Contrastar `CreateProductForm.tsx`, `useCatalogWrite.ts`, `catalog/api.ts`, cliente HTTP/CSRF, `CatalogEndpoints`, `CreateLocalBusinessProductHandler`, store/transacción y constraints de código interno. Comparar usos exitosos del helper P3/P4 y fixture de creación. El marcador identifica un bloque, no la aserción exacta. Separar hechos, hipótesis ordenadas y prueba discriminante mínima (selector/petición no enviada, status/contrato, error del callback/abort, guarda UI o fixture). No inventar causa raíz ni repetir CI. Si no puede demostrarse por código/evidencia, registrar hallazgo diagnóstico nuevo o vincular al existente solo con justificación; en RFA4 autorizar diagnóstico seguro y reparación focalizada con RED/GREEN y una ejecución controlada, no una cadena de instrumentaciones. Considerar que conservar código en sessionStorage no es idempotencia persistente; documentar límites de sesión/pestaña y no imponer una nueva arquitectura sin aprobación. Mantener F-001–007, severidad basada en impacto demostrado; no elevar un fallo de arnés a P0/P1 sin evidencia. Revisar gate Compras 645/646 como fallo histórico sin causa atribuida. Registrar las demás fronteras reales y externos bloqueados.

## 5. Arquitectura TO-BE y plan RFA4/RFA5
Actualizar ROADMAP con una cola única de decisiones ordenada por riesgo y dependencias, agrupada bajo RFA4A (dominio/dinero/stock/seguridad y defectos críticos; Astra Alto) y RFA4B (flujos y refactor delimitado; Sol Alto). Mantener las siete fases originales; L-01–07 y L-06A–I son lotes subordinados, no fases ni obligación de reimplementar módulos. Distinguir implementado/acreditado, candidato pendiente, obligatorio por riesgo, opcional, KEEP y bloqueado. Para cada corrección propuesta: UC/F/FR, causa y confianza, símbolos/contratos, efectos y dependencias, alternativa mínima, invariantes, pruebas discriminantes/aceptación, compatibilidad/rollback y autorización comercial requerida. Integrar P5, F-004/005 y gate frontend sin duplicar trabajos. Mantener monolito modular, RLS/tenant/sede, autorización revalidada, exactitud decimal, transacciones, constancias/idempotencia, auditoría y Blue Studio/UAX. Para RFA5A (Sol Medio) planificar una validación integral del SHA candidato final y pruebas críticas faltantes; RFA5B (Astra Alto) revisión arquitectónica, E5 del operador y decisión de release, incluyendo gates externos, backup/restore, seguridad, rendimiento y límites operativos. No declarar producción GO. Entregar alcance preciso del primer encargo RFA4A o, si no hay defectos críticos demostrados pendientes, justificar que solo requiere diagnóstico focalizado y no rediseño. No ejecutar ese encargo en esta sesión.

## 6. Skills, economía y gobierno
Aplicar `ub-mode` por lectura directa de la versión MIT ya verificada `3da62bf3fcfd2034de41f30d920d2739e566171f` (https://github.com/unclebob/ub-mode/tree/3da62bf3fcfd2034de41f30d920d2739e566171f), según RUN-20; no reinstalar. Reproducir antes de atribuir, una decisión en un lugar, comportamiento observable, dominios e invariantes, reducción de complejidad. Sus instrucciones de editar, limpiar, commit/push y herramientas auxiliares no aplican en RFA3. GitNexus no autorizado sin licencia comercial compatible; no instalar Crapper/Dryer/Mutator/frontend-skill ni dependencias, no subagentes. Usar rg/Git/IDE y fuentes existentes, análisis por símbolo. Git solo lectura (`status`, `diff`, `diff --check`, `log`, `show`, `ls-files`, `rev-parse`, `grep`); operador stage/commit/push. No ejecutar suites, CI, DB, DEV, servicios reales, migraciones, cambios productivos, pruebas ni workflow. No abrir investigación ilimitada ni pedir aprobación de nuevo para esta fase ya autorizada. Si una decisión excede alcance, documentarla con propuesta y condición, continuar el resto.

## 7. Entrega y gate
Solo editar documentación afectada de `docs/audit/rfa/`: VALIDATION/RUN-21, MATRIX, flows/índice cuando cambie evidencia, FINDINGS, TO_BE, ROADMAP, README, CHECKPOINT. Preservar los 156 IDs, historial, enlaces y fronteras; verificar IDs/conteos/coherencia y `git diff --check`. Entregar `RFA3_CANDIDATO_CIERRE` o bloqueo documental preciso, con RFA2 cierre con limitaciones, resumen por riesgo, causas confirmadas/hipótesis, plan RFA4A/B y RFA5A/B, primer encargo recomendado, archivos y checks. RFA2 parcial funcional, P5 global FAIL, frontend histórico abierto, E5 pendiente, producción NO-GO. No ejecutar RFA4 ni crear más órdenes de trabajo. Detenerse para auditoría del operador.
