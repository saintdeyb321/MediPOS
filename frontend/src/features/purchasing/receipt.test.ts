import { expect, it } from 'vitest'
import { dateOnly } from '../../shared/api/contracts'
import { isSystemDecimal, systemDecimalInput } from '../../shared/api/decimal'
import { detail, n } from '../../test/operations'
import { prepareLine, receiptTotal } from './receipt'

it('preserves presentation cost and exact base conversion including large quantities', () => {
  const result = prepareLine(detail, detail.units[1], '900719925474099.3', '0,1234567890123456789012345678', 'Lote', '2027-12-31')!
  expect(result.input.quantity.literal).toBe('900719925474099.3')
  expect(result.input.unitCost.literal).toBe('0.1234567890123456789012345678')
  expect(result.baseQuantity.literal).toBe('9007199254740993.0')
  expect(result.input.productUnitId).toBe(detail.units[1]?.id)
  expect(receiptTotal([{ quantity: n('2'), unitCost: n('5.1234567890123456789012345678') }]).literal).toBe('10.2469135780246913578024691356')
})
it('requires batch and exact DateOnly for medicines without converting dates or doses', () => {
  for (const [batch, date] of [['', '2027-12-31'], ['Lote', ''], ['Lote', '2027-02-30'], ['Lote', '2027-13-01']]) expect(prepareLine(detail, detail.units[0], '1', '0', batch!, date!)).toBeNull()
  expect(prepareLine(detail, detail.units[0], '1', '0', ' Lote ', '2028-02-29')?.input).toMatchObject({ batchNumber: 'Lote', expirationDate: '2028-02-29' })
  expect(() => dateOnly('0000-01-01')).toThrow()
})
it('rejects invalid or inactive presentations, nonpositive quantity and negative costs', () => {
  for (const [quantity, cost] of [['0', '1'], ['-1', '1'], ['1', '-1'], ['1e3', '1'], ['NaN', '1']]) expect(prepareLine(detail, detail.units[0], quantity!, cost!, 'Lote', '2027-12-31')).toBeNull()
  expect(prepareLine(detail, undefined, '1', '1', 'Lote', '2027-12-31')).toBeNull()
  expect(prepareLine(detail, { ...detail.units[0]!, isActive: false }, '1', '1', 'Lote', '2027-12-31')).toBeNull()
  expect(prepareLine({ ...detail, product: { ...detail.product, isActive: false } }, detail.units[0], '1', '1', 'Lote', '2027-12-31')).toBeNull()
})
it('allows retail without pharmaceutical batch fields but never rounds System.Decimal overflow', () => {
  const retail = { ...detail, product: { ...detail.product, productType: 'retail' as const } }
  expect(prepareLine(retail, detail.units[0], '1', '0', '', '')?.input.batchNumber).toBeNull()
  expect(prepareLine(detail, detail.units[1], '7922816251426433759354395033.6', '1', 'Lote', '2027-12-31')).toBeNull()
  expect(systemDecimalInput('0.00000000000000000000000000001')).toBeNull()
  expect(systemDecimalInput('79228162514264337593543950336')).toBeNull()
  expect(isSystemDecimal(n('7.9228162514264337593543950335'))).toBe(true)
  expect(isSystemDecimal(n('7.9228162514264337593543950336'))).toBe(false)
  expect(systemDecimalInput('-0,000000000001', false, true)?.literal).toBe('-0.000000000001')
  expect(systemDecimalInput('-0', false, true)).toBeNull()
})
