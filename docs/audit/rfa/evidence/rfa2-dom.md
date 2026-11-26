# Caracterizaciones DOM aisladas · RFA2

Baseline `2c54e1f71aa6f66a67190d37eacda6ada71f4b0f`. Usar una copia temporal de `frontend/src`, `build`, `package.json`, `tsconfig.json` y `vite.config.ts` con acceso a las dependencias instaladas de ese checkout. En esta ejecución se usó `frontend/node_modules/.cache/medipos-rfa2-2c54e1f/isolated-*` para que la resolución de módulos alcanzara las dependencias existentes. jsdom en `https://medipos.test/`, sin servidor o PostgreSQL. No modificar los tests rastreados.

## SC-01 · Reentrada al arqueo

En la copia, crear `src/features/cash/Rfa2CashReentry.test.tsx` con el preámbulo de `CashPage.test.tsx` hasta la primera línea que empieza por `it(` y el caso siguiente. Ejecutar `vitest run Rfa2CashReentry`. Los importes de arqueo son respuesta controlada. La prueba observa formulario, POST, navegación y GET, y deja el cálculo y la persistencia reales a E3.

```tsx
it('distinguishes a clean reentry from the original address after the simulated 100-to-10 close', async () => {
  const fake = server()
  const base = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (input, init) => {
    const path = String(input)
    const response = await base(input, init)
    if (path.endsWith('/cash-sessions/current') && !fake.state.closed) return json({ session: { ...session, openingAmount: n('100') } })
    if (path.endsWith('/' + cashId)) return json({ ...session, openingAmount: n('100'), status: fake.state.closed ? 'closed' : 'open',
      closedAt: fake.state.closed ? timestamp : null, expectedCashAmount: fake.state.closed ? n('100') : null })
    if (path.endsWith('/close') || path.endsWith('/reconciliation')) {
      const data = parseExactJson(await response.text()) as object
      return json({ ...data, openingAmount: n('100'), expectedCashAmount: n('100'), countedCashAmount: fake.state.counted,
        netSalesAmount: n('0'), cashDifference: n('-90'),
        paymentTotals: { cash: n('0'), yape: n('0'), plin: n('0'), card: n('0'), transfer: n('0') } })
    }
    return response
  })
  const initial = await prepare(fake)
  fireEvent.change(screen.getByRole('textbox', { name: 'Efectivo físico contado (S/)' }), { target: { value: '10' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText(/Faltan S\/ 90/)
  expect(fake.posts()).toHaveLength(1)
  expect(fake.posts()[0]?.[1]?.body).toBe('{"countedCashAmount":10}')
  const original = screen.getByTestId('location').textContent!
  initial.unmount()
  const clean = mount(fake)
  await screen.findByText('No hay cajas en esta página.')
  expect(screen.queryByRole('button', { name: 'Consultar cierre guardado' })).not.toBeInTheDocument()
  clean.unmount()
  mount(fake, original)
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar cierre guardado' }))
  await screen.findByText(/Faltan S\/ 90/)
  expect(fake.receiptGets()).toHaveLength(1)
  expect(fake.posts()).toHaveLength(1)
})
```

## SC-02 · Sencillo

En la copia, crear `src/features/transfers/Rfa2CashContract.test.tsx` con este contenido y ejecutar Vitest sobre ese archivo junto con `CashTransfersPage.test.tsx`. El doble de transportes devuelve el shape que proyecta el backend inspeccionado. La aserción caracteriza el bloqueo actual.

```tsx
import { fireEvent, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { CashTransfersPage } from './CashTransfersPage'
import { cashServer } from './cash.test.support'
import { id, json, mountTransfers, other, workspace } from './transfers.test.support'

it('characterizes the null expected cash contract in an open session', async () => {
  const fake = cashServer()
  const base = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (input, init) => {
    const path = new URL(String(input), 'https://unit.test').pathname
    if ((init?.method ?? 'GET') === 'GET' && path.endsWith('/cash-sessions/' + id(302))) {
      return json({ cashSessionId: id(302), branchId: workspace.branchId,
        membershipId: workspace.access.membershipId, openingAmount: fake.cash.expected,
        openedAt: '2026-01-01T12:00:00Z', closedAt: null, status: 'open', expectedCashAmount: null })
    }
    return base(input, init)
  })
  mountTransfers(fake, <CashTransfersPage />)
  fireEvent.click(screen.getByRole('button', { name: 'Nuevo envío de sencillo' }))
  await screen.findAllByRole('option', { name: /apertura UTC/ })
  fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(302) } })
  await screen.findByText(/Caja ABIERTA/)
  fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1.0001' } })
  const button = screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })
  expect(button).toBeDisabled()
  fireEvent.click(button)
  expect(fake.writes()).toHaveLength(0)
})
```

## SC-04 a SC-06 · Catálogo, Compras e Inventario

Para cada archivo nuevo, copiar como preámbulo los imports y helpers del test indicado, desde el comienzo hasta antes del primer `it(`. Añadir solo el caso mostrado. Ejecutar `vitest run Rfa2Finding` en la copia. Los contadores miden llamadas interceptadas por el doble de transporte.

### `src/features/catalog/Rfa2Finding3.test.tsx`

Preámbulo de `ProductsPage.test.tsx`.

```tsx
it('filters the current page and finds the later product after pagination', async () => {
  const fake = server()
  fake.state.products = Array.from({ length: 51 }, (_, index) =>
    ({ ...record, id: id(String(100 + index)), name: index === 50 ? 'Producto final único' : 'Otro producto ' + index }))
  mount(fake); showList()
  await screen.findByRole('button', { name: 'Ver Otro producto 0' })
  fireEvent.change(screen.getByLabelText('Filtrar esta página'), { target: { value: 'Producto final único' } })
  expect(await screen.findByText('Sin productos para esta vista')).toBeInTheDocument()
  expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/products?'))).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
  await screen.findByRole('button', { name: 'Ver Producto final único' })
  expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/products?'))).toHaveLength(2)
})
```

### `src/features/purchasing/Rfa2Finding4.test.tsx`

Preámbulo de `PurchasesPage.test.tsx`.

```tsx
it('finds a receipt without reference only through its original identifier in this scenario', async () => {
  const fake = operationServer()
  fake.state.purchases = [{ ...purchase, documentReference: null }]
  mountOperation(fake, <PurchasesPage />)
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia de la recepción a consultar' }), { target: { value: 'REF-UNIT' } })
  fireEvent.click(screen.getByRole('button', { name: 'Buscar referencia' }))
  await screen.findByText('No hay coincidencias para esta referencia.')
  fireEvent.click(screen.getByText('Buscar por código de soporte'))
  fireEvent.change(screen.getByRole('textbox', { name: 'Identificador de la recepción original' }), { target: { value: purchase.id } })
  fireEvent.click(screen.getByRole('button', { name: 'Consultar identificador original' }))
  await screen.findByRole('region', { name: 'Productos y lotes' })
  expect(fake.writes()).toHaveLength(0)
})
```

### `src/features/inventory/Rfa2Finding5.test.tsx`

Preámbulo de `InventoryPage.test.tsx`.

```tsx
it('fetches a cold product label once and reuses it across same-scope stock and lots', async () => {
  const fake = operationServer()
  mountOperation(fake, <InventoryPage />)
  const countName = () => fake.fetcher.mock.calls.filter(([path, init]) => (init?.method ?? 'GET') === 'GET' && String(path).endsWith('/products/' + id(10))).length
  await screen.findByRole('button', { name: 'Filtrar por Producto sintético unitario' })
  expect(countName()).toBe(1)
  fireEvent.click(screen.getByRole('button', { name: 'Lotes' }))
  await screen.findByRole('button', { name: 'Ajustar lote BATCH-UNIT' })
  const table = screen.getByRole('region', { name: 'Tabla de lotes' })
  await within(table).findByText('Producto sintético unitario')
  expect(countName()).toBe(1)
  expect(fake.writes()).toHaveLength(0)
})
```

