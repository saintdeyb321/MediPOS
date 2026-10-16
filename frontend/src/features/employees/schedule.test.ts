import { expect, it } from 'vitest'
import { timeInput, validSchedule, weekday, wireTime } from './schedule'

it('uses named weekdays and exact microseconds with no time-zone/Date conversions', () => {
  expect(weekday('sunday')).toBe('sunday'); expect(weekday('wednesday')).toBe('wednesday')
  for (const invalid of [0, 'Monday', '1', 'lunes']) expect(() => weekday(invalid)).toThrow()
  expect(timeInput('09:00')).toBe('09:00:00')
  expect(wireTime('09:00:00.0000010')).toBe('09:00:00.000001')
  expect(wireTime('23:59:59.999999')).toBe('23:59:59.999999')
  expect(timeInput('09:00:00.0000001')).toBeNull()
  expect(timeInput('24:00')).toBeNull(); expect(timeInput('09:60')).toBeNull()
  expect(() => wireTime('09:00')).toThrow()
})
it('rejects duplicates including equivalent time representations, equal bounds and midnight crossings', () => {
  for (const [startTime, endTime] of [['18:00', '09:00'], ['09:00', '09:00'], ['23:00', '01:00'], ['09:00:00.000001', '09:00:00.000000']]) expect(validSchedule([{ dayOfWeek: 'monday', startTime: startTime!, endTime: endTime! }])).toBe(false)
  expect(validSchedule([{ dayOfWeek: 'monday', startTime: '09:00', endTime: '18:00' }, { dayOfWeek: 'monday', startTime: '09:00:00.0000000', endTime: '18:00:00' }])).toBe(false)
})
it('preserves empty and multiple windows, without inventing an overlap prohibition', () => {
  expect(validSchedule([])).toBe(true)
  expect(validSchedule([{ dayOfWeek: 'monday', startTime: '09:00', endTime: '12:00' }, { dayOfWeek: 'monday', startTime: '11:00', endTime: '18:00' }, { dayOfWeek: 'tuesday', startTime: '09:00:00.000001', endTime: '09:00:00.000002' }])).toBe(true)
})
