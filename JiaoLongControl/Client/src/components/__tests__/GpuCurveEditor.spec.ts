import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent } from 'vue'
import GpuCurveEditor from '../GpuCurveEditor.vue'

const api = vi.hoisted(() => ({
  GetVoltageFrequencyCurve: vi.fn(),
  PreviewVoltageFrequencyCurve: vi.fn(),
  Preview4060LaptopPreset: vi.fn(),
  ApplyVoltageFrequencyCurve: vi.fn(),
  GetCurveTrialStatus: vi.fn(),
  KeepVoltageFrequencyCurve: vi.fn(),
  RestoreVoltageFrequencyCurve: vi.fn(),
}))
vi.mock('@/utils/bridge', () => ({ NvidiaGpu: api }))
const points = Array.from({ length: 32 }, (_, Id) => ({
  Id,
  VoltageMicroV: 600000 + Id * 12500,
  FrequencyKHz: 1200000 + Id * 45000,
  OffsetKHz: 0,
}))
const snapshot = {
  Device: 'mock',
  Points: points,
  Offsets: Array(128).fill(0),
  MinOffsetKHz: -1000000,
  MaxOffsetKHz: 1000000,
}
const button = defineComponent({
  props: ['disabled'],
  template: '<button :disabled="disabled"><slot /></button>',
})
const select = defineComponent({
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template:
    '<select :value="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', Number($event.target.value))"><slot /></select>',
})
const option = defineComponent({
  props: ['value'],
  template: '<option :value="value"><slot /></option>',
})
const numeric = defineComponent({
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template:
    '<input type="number" :value="modelValue" :disabled="disabled" @input="$emit(\'update:modelValue\', Number($event.target.value))" />',
})
const checkbox = defineComponent({
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template:
    '<label><input type="checkbox" :checked="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.checked)"/><slot /></label>',
})
let wrapper: VueWrapper
function byText(text: string) {
  return wrapper.findAll('button').find((b) => b.text().includes(text))!
}
beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  api.GetVoltageFrequencyCurve.mockResolvedValue({ Success: true, Message: '只读', Data: snapshot })
  api.GetCurveTrialStatus.mockResolvedValue({
    Success: true,
    Data: { Active: false, SecondsRemaining: null, Message: '尚未应用' },
  })
  api.PreviewVoltageFrequencyCurve.mockResolvedValue({
    Success: true,
    Message: '仅预览',
    Data: {
      Token: 'preview-token',
      Plan: { Original: snapshot, Offsets: snapshot.Offsets, AnchorId: 16, TargetMhz: 1950 },
    },
  })
  api.ApplyVoltageFrequencyCurve.mockResolvedValue({ Success: true, Message: '试用中' })
  api.Preview4060LaptopPreset.mockResolvedValue({ Success: true, Message: '仅预览，不保证稳定', Data: {
    Token: 'preset-token', Plan: { Original: snapshot, Offsets: snapshot.Offsets, AnchorId: 24, TargetMhz: 2280 },
  } })
  wrapper = mount(GpuCurveEditor, {
    global: {
      stubs: {
        'a-button': button,
        'a-select': select,
        'a-option': option,
        'a-input-number': numeric,
        'a-checkbox': checkbox,
      },
    },
  })
})
afterEach(() => {
  wrapper.unmount()
  vi.useRealTimers()
})
async function preview() {
  await byText('读取曲线').trigger('click')
  await flushPromises()
  await wrapper.get('select').setValue('16')
  await wrapper.get('input[type=number]').setValue('1950')
  await byText('生成预览').trigger('click')
  await flushPromises()
}
describe('safe curve editor', () => {
  it('built-in preset only previews, synchronizes inputs, and still requires consent', async () => {
    await byText('读取曲线').trigger('click'); await flushPromises()
    await byText('生成 4060 保守预设').trigger('click'); await flushPromises()
    expect(api.Preview4060LaptopPreset).toHaveBeenCalledOnce()
    expect(wrapper.get('select').element.value).toBe('24')
    expect((wrapper.get('input[type=number]').element as HTMLInputElement).value).toBe('2280')
    expect(wrapper.findAll('polyline')).toHaveLength(2)
    expect(byText('试用 60 秒').attributes('disabled')).toBeDefined()
    expect(api.ApplyVoltageFrequencyCurve).not.toHaveBeenCalled()
    await wrapper.get('input[type=checkbox]').setValue(true)
    await byText('试用 60 秒').trigger('click'); await flushPromises()
    expect(api.ApplyVoltageFrequencyCurve).toHaveBeenCalledWith('preset-token', true)
  })
  it('preset rejection cannot expose an apply button or invent parameters', async () => {
    api.Preview4060LaptopPreset.mockResolvedValue({ Success: false, Message: '型号不符或已有偏移' })
    await byText('读取曲线').trigger('click'); await flushPromises()
    await byText('生成 4060 保守预设').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('型号不符或已有偏移')
    expect(wrapper.text()).not.toContain('试用 60 秒')
    expect(api.ApplyVoltageFrequencyCurve).not.toHaveBeenCalled()
  })
  it('mount/status/read/preview never apply offsets', async () => {
    await flushPromises()
    expect(api.GetVoltageFrequencyCurve).not.toHaveBeenCalled()
    await preview()
    expect(api.PreviewVoltageFrequencyCurve).toHaveBeenCalledWith(16, 1950)
    expect(api.ApplyVoltageFrequencyCurve).not.toHaveBeenCalled()
    expect(wrapper.findAll('polyline')).toHaveLength(2)
  })
  it('requires a fresh preview plus explicit risk acknowledgement', async () => {
    await preview()
    expect(byText('试用 60 秒').attributes('disabled')).toBeDefined()
    await wrapper.get('input[type=checkbox]').setValue(true)
    await byText('试用 60 秒').trigger('click')
    await flushPromises()
    expect(api.ApplyVoltageFrequencyCurve).toHaveBeenCalledWith('preview-token', true)
    expect(wrapper.find('input[type=checkbox]').exists()).toBe(false)
  })
  it('invalidates the preview when the target changes', async () => {
    await preview()
    await wrapper.get('input[type=number]').setValue('1965')
    expect(wrapper.findAll('polyline')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('试用 60 秒')
  })
  it('does not invent a curve when the driver refuses a read', async () => {
    api.GetVoltageFrequencyCurve.mockResolvedValue({ Success: false, Message: '不支持该驱动' })
    await byText('读取曲线').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('不支持该驱动')
    expect(wrapper.find('svg').exists()).toBe(false)
    expect(api.ApplyVoltageFrequencyCurve).not.toHaveBeenCalled()
  })
  it('shows persistent backup recovery even when the curve has not been loaded', async () => {
    api.GetCurveTrialStatus.mockResolvedValue({
      Success: true,
      Data: { Active: true, SecondsRemaining: 12, Message: '备份已保留' },
    })
    await vi.advanceTimersByTimeAsync(1000)
    await flushPromises()
    expect(wrapper.text()).toContain('剩余 12 秒')
    expect(byText('恢复备份').exists()).toBe(true)
    expect(byText('读取曲线').attributes('disabled')).toBeDefined()
  })
  it('leaving the page does not call Keep or cancel backend recovery', async () => {
    await preview()
    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(60000)
    expect(api.KeepVoltageFrequencyCurve).not.toHaveBeenCalled()
    expect(api.RestoreVoltageFrequencyCurve).not.toHaveBeenCalled()
  })
})
