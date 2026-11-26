# Checkpoint RFA

**FASE_01_PARCIAL · RFA0 y RFA1.** Baseline `7d453285252d2bf9279cb7c42e1ac95f4f8c70c6`, rama `ci/postgres-diagnostics-8bc6b4c`. Sol/Medium como referencia de la orden. [Huella y build](README.md). Inventario global y Caja patrón guardados antes de continuar por familias. Las nueve tienen ficha, 80/154 con E1, 74 E0 por completar. Toda ejecución NOT_RUN.

- Fuentes inspeccionadas y enlaces por familia en [README](README.md), [índice](USE_CASES.md) y [matriz](MATRIX.csv). RF y variantes quedan junto a las fichas. Indicios F-001 a F-005 en [FINDINGS](FINDINGS.md).
- Caso patrón Caja. Cierre y recuperación por ID categorizados por código. Reproducción del reporte del operador pendiente de autorización de la fase correspondiente. Separar build/permiso/sede y no interpretar ausencia en activas como pérdida.
- Siguiente acción exacta dentro de RFA1, tras revisión del operador. Retomar `UC-CAT-08` en `SearchProductsHandler.cs:17–43`. Resolver la implementación de `IProductSearchStore`, términos normalizados, filtros de estado, stock y límites entre sedes. Después continuar con los IDs E0 según tabla de cobertura. No reabrir el patrón de Caja salvo evidencia nueva.
- RFA2 requiere otra instrucción explícita después de la revisión de estos artefactos. Los checks de esta fase se limitan al árbol, enlaces, IDs y CSV.
