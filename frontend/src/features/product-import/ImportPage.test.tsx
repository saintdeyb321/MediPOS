import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { useQuery } from '@tanstack/react-query'
import { HttpClient } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { ImportPage } from './ImportPage'
import { excelType, importApi, validFile } from './api'
import { id, importJob, json, managementServer, mountTransfers, workspace } from '../notifications/management.test.support'

const file = () => new File(['SYNTHETIC-IMPORT-UNIT'], 'unit.xlsx', { type: excelType })
async function selectFile() { fireEvent.change(screen.getByLabelText('Archivo Excel .xlsx'), { target: { files: [file()] } }); fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo crear los productos/ })) }
it.each(['pharmacist', 'cashier'] as const)('denies product import to %s before HTTP access', role => {
  const fake = managementServer(); mountTransfers(fake, <ImportPage />, '', { ...workspace, access: { ...workspace.access, role } }); expect(screen.getByText('Importar productos requiere Owner.')).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('validates XLSX extension, MIME, name, nonempty bytes and 5 MiB before any upload', async () => {
  expect(validFile(file())).toBe(true)
  const invalid = [new File([], 'unit.xlsx'), new File(['unit'], 'unit.csv'), new File(['unit'], 'unit.xlsx', { type: 'text/html' }), new File(['unit'], 'bad\n.xlsx'), new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'unit.xlsx')]
  const fake = managementServer(); const api = importApi(new HttpClient(fake.fetcher)); for (const value of invalid) { expect(validFile(value)).toBe(false); await expect(api.create(workspace, value)).rejects.toMatchObject({ code: 'client.invalid_file' }) }; expect(fake.fetcher).not.toHaveBeenCalled()
})
it('requires explicit confirmation, preserves jobId, rereads results and uses raw bytes exactly once', async () => {
  const fake = managementServer(); mountTransfers(fake, <ImportPage />); const button = screen.getByRole('button', { name: 'Importar archivo confirmado' }); expect(button).toBeDisabled(); await selectFile(); fireEvent.click(button); fireEvent.click(button)
  await screen.findByRole('heading', { name: 'Resultado: Completado' }); expect(screen.getByText('Fila sintética inválida')).toBeInTheDocument(); expect(screen.getByText('import.unit_invalid')).toBeInTheDocument(); expect(fake.writes()).toHaveLength(1)
  const init = fake.writes()[0]![1]!; expect(init.body).toBeInstanceOf(File); expect(new Headers(init.headers).get('Content-Type')).toBe(excelType); expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('management-unit-csrf'); expect(screen.getByTestId('location')).toHaveTextContent('importJob=' + importJob.importJobId); expect(button).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué que es el trabajo/ })); fireEvent.click(screen.getByRole('button', { name: 'Finalizar revisión de importación' })); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('importPending')); expect(fake.writes()).toHaveLength(1)
})
it('keeps unknown import blocked through reload without inferring idempotency from a filename', async () => {
  const fake = managementServer(); fake.management.lose = 'import'; const view = mountTransfers(fake, <ImportPage />); await selectFile(); fireEvent.click(screen.getByRole('button', { name: 'Importar archivo confirmado' })); await screen.findByText(/La respuesta no identificó el trabajo/); expect(screen.getByRole('button', { name: 'Importar archivo confirmado' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
  view.unmount(); mountTransfers(fake, <ImportPage />, '?importPending=1'); expect(screen.getByRole('button', { name: 'Importar archivo confirmado' })).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Finalizar revisión de importación' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(1)
})
it('cannot review an unrelated terminal job as the original keyed import after reload', async () => {
  const fake = managementServer(); const requestId = id(600); const other = { ...importJob, importJobId: id(601), fileName: 'otro.xlsx' }
  fake.management.jobs.push(other)
  const normal = fake.fetcher.getMockImplementation()!; let showReceipt = false
  fake.fetcher.mockImplementation((path, init) => String(path).endsWith('/operations/' + requestId)
    ? Promise.resolve(showReceipt ? json({ requestId, kind: 'product-import', resultJson: JSON.stringify({ importJobId: importJob.importJobId }) }) : json({ code: 'operation.not_observed' }, 404))
    : normal(path, init))
  mountTransfers(fake, <ImportPage />, `?importPending=${requestId}&importJob=${other.importJobId}&importAck=1`)
  const recover = screen.getByRole('button', { name: 'Localizar trabajo por su intención original' })
  expect(screen.queryByRole('textbox', { name: 'Identificador del trabajo original' })).not.toBeInTheDocument()
  fireEvent.click(recover); await screen.findByText(/Aún no se observa una constancia confirmada/)
  expect(screen.getByRole('button', { name: 'Importar archivo confirmado' })).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Finalizar revisión de importación' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes(other.importJobId))).toBe(false)
  showReceipt = true; fireEvent.click(recover); await screen.findByRole('heading', { name: 'Resultado: Completado' })
  expect(screen.getByTestId('location')).toHaveTextContent('importJob=' + importJob.importJobId)
  expect(fake.writes()).toHaveLength(0)
})
it('preserves an import 403 as uncertain because authorization can be revoked after committed rows', async () => {
  const fake = managementServer(); fake.management.reject = 403; mountTransfers(fake, <ImportPage />); await selectFile(); fireEvent.click(screen.getByRole('button', { name: 'Importar archivo confirmado' })); await screen.findByText(/La respuesta no identificó el trabajo/); expect(fake.writes()).toHaveLength(1)
  const requestId = new Headers(fake.writes()[0]![1]?.headers).get('Idempotency-Key')
  expect(requestId).toMatch(/^[a-f\d-]{36}$/); expect(screen.getByTestId('location')).toHaveTextContent('importPending=' + requestId)
})
it('recovers known job after reload and pages server row cursors without rerunning import', async () => {
  const fake = managementServer(); fake.management.jobs[0] = { ...importJob, rows: Array.from({ length: 21 }, (_, i) => ({ rowNumber: i + 4, status: 'invalid', businessProductId: null, errorCode: 'unit.invalid', errorMessage: 'Fila ' + i })), totalRows: 21, validRows: 0, invalidRows: 21, importedRows: 0 }
  mountTransfers(fake, <ImportPage />, '?importJob=' + importJob.importJobId + '&importPending=1&importAck=1'); await screen.findByText('Fila 0'); fireEvent.click(screen.getByRole('button', { name: 'Resultados siguientes' })); await screen.findByText('Fila 20'); expect(screen.queryByText('Fila 0')).not.toBeInTheDocument(); expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('afterRowNumber=23&limit=20'))).toBe(true); expect(fake.writes()).toHaveLength(0)
})
it('only invalidates the catalog when paging observes newly imported rows', async () => {
  const fake = managementServer()
  fake.management.jobs[0] = { ...importJob, rows: Array.from({ length: 21 }, (_, i) => ({
    rowNumber: i + 4, status: 'invalid', businessProductId: null, errorCode: 'unit.invalid', errorMessage: 'Fila ' + i,
  })), totalRows: 22, importedRows: 0, status: 'processing', completedAt: null }
  const readCatalog = vi.fn(async () => [])
  function CatalogProbe() {
    useQuery({ queryKey: scopedKey(workspace, 'catalog-products'), queryFn: readCatalog, staleTime: Infinity })
    return null
  }
  mountTransfers(fake, <><CatalogProbe /><ImportPage /></>, '?importJob=' + importJob.importJobId)
  await screen.findByText('Fila 0')
  await waitFor(() => expect(readCatalog).toHaveBeenCalledTimes(2))
  fireEvent.click(screen.getByRole('button', { name: 'Resultados siguientes' }))
  await screen.findByText('Fila 20')
  expect(readCatalog).toHaveBeenCalledTimes(2)
  fake.management.jobs[0] = { ...fake.management.jobs[0]!, importedRows: 1 }
  fireEvent.click(screen.getByRole('button', { name: 'Actualizar mismo trabajo' }))
  await waitFor(() => expect(readCatalog).toHaveBeenCalledTimes(3))
  expect(fake.writes()).toHaveLength(0)
})
it('shows a terminal legacy job for consultation but its URL cannot settle the original import', async () => {
  const fake = managementServer()
  const legacy = `?importPending=1&importJob=${importJob.importJobId}&importAck=1`
  const view = mountTransfers(fake, <ImportPage />, legacy)
  await screen.findByRole('heading', { name: 'Resultado: Completado' })
  expect(screen.queryByRole('button', { name: 'Finalizar revisión de importación' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Importar archivo confirmado' })).toBeDisabled()
  const address = screen.getByTestId('location').textContent ?? ''
  view.unmount(); mountTransfers(fake, <ImportPage />, address)
  await screen.findByRole('heading', { name: 'Resultado: Completado' })
  expect(screen.getByTestId('location')).toHaveTextContent('importPending=1')
  expect(screen.queryByRole('button', { name: 'Finalizar revisión de importación' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('does not treat a processing or failed query as a completed import', async () => {
  const fake = managementServer(); fake.management.jobs[0] = { ...importJob, status: 'processing', completedAt: null }; mountTransfers(fake, <ImportPage />, '?importJob=' + importJob.importJobId + '&importPending=1'); await screen.findByRole('heading', { name: 'Resultado: En proceso' }); expect(screen.queryByRole('button', { name: 'Finalizar revisión de importación' })).not.toBeInTheDocument()
  fake.management.failGet = true; fireEvent.click(screen.getByRole('button', { name: 'Actualizar mismo trabajo' })); await screen.findAllByRole('alert'); expect(screen.queryByRole('heading', { name: 'Resultado: En proceso' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it.each([false, true])('confirms an interrupted job with one scoped POST and resolves a lost acknowledgement (%s)', async lost => {
  const fake = managementServer()
  const current = { ...importJob, status: 'processing', completedAt: null, canRecoverInterrupted: true }
  const terminal = { ...importJob, status: 'failed' as const, canRecoverInterrupted: false }
  const base = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, request) => {
    if (String(path).endsWith(`/product-imports/${importJob.importJobId}/recover-interrupted`)) {
      expect(request?.method).toBe('POST'); expect(request?.body).toBeUndefined()
      expect(new Headers(request?.headers).has('X-CSRF-TOKEN')).toBe(true)
      fake.management.jobs[0] = terminal
      if (lost) throw new TypeError('synthetic response loss')
      return json(terminal)
    }
    if (String(path).includes('/product-imports/') && fake.management.jobs[0] !== terminal) return json(current)
    return base(path, request)
  })
  mountTransfers(fake, <ImportPage />, `?importJob=${importJob.importJobId}`)
  const close = await screen.findByRole('button', { name: 'Cerrar trabajo interrumpido' }); expect(close).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo cerrar este trabajo/ })); fireEvent.click(close); fireEvent.click(close)
  await screen.findByRole('heading', { name: 'Resultado: Fallido' })
  await screen.findByText(/Estado terminal confirmado/)
  expect(screen.queryByRole('button', { name: 'Cerrar trabajo interrumpido' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(1)
})
it.each([false, true])('keeps interrupted recovery unavailable when gated or when the legacy URL lacks proof (%s)', async legacy => {
  const fake = managementServer()
  const base = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation((path, request) => String(path).includes('/product-imports/')
    ? Promise.resolve(json({ ...importJob, status: 'processing', completedAt: null, canRecoverInterrupted: legacy })) : base(path, request))
  mountTransfers(fake, <ImportPage />, `?importJob=${importJob.importJobId}${legacy ? '&importPending=1' : ''}`)
  await screen.findByRole('heading', { name: 'Resultado: En proceso' })
  expect(screen.queryByRole('button', { name: 'Cerrar trabajo interrumpido' })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('downloads the official binary template via the same client without a commercial POST', async () => {
  const fake = managementServer(); const binaryBlob = (await new Response('synthetic').blob()).constructor; vi.stubGlobal('Blob', binaryBlob)
  const create = vi.fn(() => 'blob:synthetic-template'); Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: create }); Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() }); const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  try { mountTransfers(fake, <ImportPage />); fireEvent.click(screen.getByRole('button', { name: 'Descargar plantilla oficial XLSX' })); await screen.findByText(/Plantilla oficial descargada/); expect(create).toHaveBeenCalledTimes(1); expect(fake.writes()).toHaveLength(0) } finally { click.mockRestore(); vi.unstubAllGlobals() }
})
it('rejects foreign/malformed job DTOs and never invents a new endpoint for reconciliation', async () => {
  const fake = managementServer(); fake.management.jobs[0] = { ...importJob, importJobId: id(999) }; await expect(importApi(new HttpClient(fake.fetcher)).read(workspace, importJob.importJobId, 0)).rejects.toMatchObject({ status: 404 }); expect(fake.writes()).toHaveLength(0)
})
it('cancels an old import read and never shows its late result in another membership', async () => {
  const fake = managementServer(); let finish!: (value: Response) => void
  fake.fetcher.mockImplementationOnce((_path, init) => new Promise(resolve => { finish = resolve; expect(init?.method).toBe('GET') }))
  const view = mountTransfers(fake, <ImportPage />, '?importJob=' + importJob.importJobId); await waitFor(() => expect(fake.fetcher).toHaveBeenCalledTimes(1)); const signal = fake.fetcher.mock.calls[0]![1]!.signal!
  fake.management.jobs = []; view.changeContext({ ...workspace, access: { ...workspace.access, membershipId: id(990) } }); expect(signal.aborted).toBe(true)
  await act(async () => finish(json({ ...importJob, fileName: 'LATE-PRIVATE-IMPORT.xlsx' }))); expect(screen.queryByText(/LATE-PRIVATE-IMPORT/)).not.toBeInTheDocument(); await screen.findByText('No pudimos cargar la información'); expect(fake.writes()).toHaveLength(0)
})
