import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { createPinia } from 'pinia'
import GPU from '../GPU.vue'

const mocks = vi.hoisted(() => ({
  api: {
    GetGpuCoreClockRange: vi.fn(), GetGpuMemoryClockRange: vi.fn(),
    GetGpuPowerLimitRange: vi.fn(), GetClockOffsetRange: vi.fn(),
    GetClockOffsets: vi.fn(), GetGpuThermalPolicy: vi.fn(), GetOverclockCapabilities: vi.fn(),
    LockGpuClock: vi.fn(), LockMemoryClock: vi.fn(), ResetGpuClock: vi.fn(), ResetMemoryClock: vi.fn(),
  },
  config: {
    Gpu: { ClockLockEnabled: false, GpuClock: 2250, MemoryClock: 8001, PowerLimit: 0,
      CoreClockOffset: 0, MemoryClockOffset: 0, VoltageBoostPercent: 0 },
  },
  save: vi.fn(), error: vi.fn(), success: vi.fn(), info: vi.fn(),
}))
vi.mock('@/utils/bridge', () => ({ NvidiaGpu: mocks.api, CPU: {}, Fan: {} }))
vi.mock('@/stores/config', () => ({
  useConfigStore: () => ({ config: mocks.config, saveConfig: mocks.save }),
}))
vi.mock('@arco-design/web-vue', () => ({ Message: {
  error: mocks.error, success: mocks.success, info: mocks.info,
} }))

const select = defineComponent({
  props: ['modelValue', 'disabled'], emits: ['update:modelValue'],
  template: '<select :value="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', Number($event.target.value))"><slot /></select>',
})
const option = defineComponent({ props: ['value'], template: '<option :value="value"><slot /></option>' })
let wrapper: VueWrapper | undefined
async function render() {
  wrapper = mount(defineComponent({ components: { GPU }, template: '<Suspense><GPU /></Suspense>' }), {
    global: { plugins: [createPinia()], stubs: {
      'a-select': select, 'a-option': option, 'a-slider': true, 'a-spin': true, GpuCurveEditor: true,
    } },
  })
  await flushPromises()
  return wrapper
}
function applyButton() { return wrapper!.findAll('button').find(b => b.text() === '应用')! }
beforeEach(() => {
  vi.resetAllMocks()
  mocks.config.Gpu.ClockLockEnabled = false
  mocks.config.Gpu.MemoryClock = 8001
  for (const fn of Object.values(mocks.api)) fn.mockResolvedValue({ Success: false, Message: '不可用' })
  mocks.api.GetGpuCoreClockRange.mockResolvedValue({ Success: true, Data: { Min: 1545, Max: 2250 } })
  mocks.api.GetGpuMemoryClockRange.mockResolvedValue({ Success: true,
    Data: { Min: 405, Max: 8001, Values: [8001, 7001, 6001, 810, 405, 8001] } })
  mocks.api.LockGpuClock.mockResolvedValue({ Success: true })
  mocks.api.LockMemoryClock.mockResolvedValue({ Success: true })
  mocks.api.ResetGpuClock.mockResolvedValue({ Success: true })
  mocks.save.mockResolvedValue({ Success: true })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

it('8001/8001 factory clocks no longer collapse the memory control; selecting is read-only', async () => {
  const view = await render()
  expect(view.findAll('option').map(o => o.attributes('value'))).toEqual(['405', '810', '6001', '7001', '8001'])
  await view.get('select').setValue('7001')
  expect((view.get('select').element as HTMLSelectElement).value).toBe('7001')
  expect(mocks.api.LockGpuClock).not.toHaveBeenCalled()
  expect(mocks.api.LockMemoryClock).not.toHaveBeenCalled()
  expect(mocks.save).not.toHaveBeenCalled()
  expect(mocks.config.Gpu.MemoryClock).toBe(8001)
  await applyButton().trigger('click'); await flushPromises()
  expect(mocks.api.LockMemoryClock).toHaveBeenCalledWith(7001)
  expect(mocks.config.Gpu.MemoryClock).toBe(7001)
  expect(mocks.save).toHaveBeenCalledOnce()
})
it('driver rejection disables selection and reports the cause without guessed options', async () => {
  mocks.api.GetGpuMemoryClockRange.mockResolvedValue({ Success: false, Message: '此驱动不提供档位' })
  const view = await render()
  expect(view.text()).toContain('此驱动不提供档位')
  expect(view.get('select').attributes('disabled')).toBeDefined()
  expect(applyButton().attributes('disabled')).toBeDefined()
  expect(view.findAll('option')).toHaveLength(0)
})
it.each([
  { Min: 8001, Max: 8001 },
  { Values: [] },
  { Values: [8001, '7001'] },
  { Values: [8001, 7001.5] },
  { Values: [8001, 0] },
])('invalid or legacy response cannot silently enable memory adjustment: %j', async (Data) => {
  mocks.api.GetGpuMemoryClockRange.mockResolvedValue({ Success: true, Data })
  await render()
  expect(applyButton().attributes('disabled')).toBeDefined()
  expect(mocks.api.LockMemoryClock).not.toHaveBeenCalled()
})
it('one reported value is explained explicitly instead of displaying a stuck slider', async () => {
  mocks.api.GetGpuMemoryClockRange.mockResolvedValue({ Success: true, Data: { Values: [8001] } })
  const view = await render()
  expect(view.findAll('option')).toHaveLength(1)
  expect(view.text()).toContain('驱动只报告一个显存档位')
})
it('an unrelated optional query rejection cannot hide valid memory choices', async () => {
  mocks.api.GetClockOffsetRange.mockRejectedValue(new Error('old private interface unavailable'))
  const view = await render()
  expect(view.findAll('option')).toHaveLength(5)
  expect(applyButton().attributes('disabled')).toBeUndefined()
})
it('invalid saved input is only prepared locally, never applied or saved during reading', async () => {
  mocks.config.Gpu.MemoryClock = 7500
  const view = await render()
  expect((view.get('select').element as HTMLSelectElement).value).toBe('8001')
  expect(mocks.config.Gpu.MemoryClock).toBe(7500)
  expect(mocks.save).not.toHaveBeenCalled()
})
it('memory write refusal is not saved or reported as success', async () => {
  mocks.api.LockMemoryClock.mockResolvedValue({ Success: false, Message: '显存锁频不支持' })
  const view = await render()
  await view.get('select').setValue('7001')
  await applyButton().trigger('click'); await flushPromises()
  expect(mocks.save).not.toHaveBeenCalled()
  expect(mocks.success).not.toHaveBeenCalled()
  expect(mocks.config.Gpu.ClockLockEnabled).toBe(false)
  expect(mocks.error).toHaveBeenCalledWith(expect.stringContaining('显存锁频不支持'))
})
