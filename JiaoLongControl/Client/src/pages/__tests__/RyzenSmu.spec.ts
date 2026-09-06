import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import { defineComponent, reactive } from 'vue'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import RyzenSmu from '../RyzenSmu.vue'

const mocks = vi.hoisted(() => ({
  store: {} as Record<string, unknown>,
  set: vi.fn(),
  save: vi.fn(),
  message: { success: vi.fn(), error: vi.fn() },
}))
vi.mock('@/stores/config', () => ({ useConfigStore: () => mocks.store }))
vi.mock('@arco-design/web-vue', () => ({ Message: mocks.message }))
vi.mock('@/utils/bridge', () => ({
  CPU: {
    GetPhysicalCoreCount: async () => ({ Success: true, Data: 16 }),
    GetCpuInfo: async () => ({
      Success: true,
      Data: { Name: 'MOCK AMD Ryzen 7945HX', Cores: 16, Threads: 32 },
    }),
  },
  RyzenSmu: {
    SetStapmLimit: (...args: unknown[]) => mocks.set(...args),
    GetSmuTelemetry: async () => ({
      Success: true,
      Data: { Ppt: 40, Tdc: null, Edc: null, Temp: 60, FreqMhz: 3000, Usage: 10 },
    }),
  },
}))
const slider = defineComponent({
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template:
    '<input type="range" :disabled="disabled" :value="modelValue" @input="$emit(\'update:modelValue\', Number($event.target.value))"/>',
})
const button = defineComponent({
  props: ['disabled'],
  template: '<button :disabled="disabled"><slot/></button>',
})
let wrapper: VueWrapper
beforeEach(async () => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  const smu = Object.fromEntries(
    [
      'StapmLimit',
      'StapmTime',
      'FastLimit',
      'SlowLimit',
      'SlowTime',
      'PptLimitRsmu',
      'VrmCurrentMp1',
      'VrmCurrentRsmu',
      'EdcLimitMp1',
      'EdcLimitRsmu',
      'TempLimitMp1',
      'TempLimitRsmu',
      'PboScalar',
      'OcClk',
      'OcVolt',
      'CurveOptimizerAll',
    ].map((key) => [key, 0]),
  )
  mocks.store = reactive({ config: { Smu: smu }, saveConfig: mocks.save })
  mocks.save.mockResolvedValue({ Success: true })
  wrapper = mount(
    defineComponent({ components: { RyzenSmu }, template: '<Suspense><RyzenSmu/></Suspense>' }),
    {
      global: {
        stubs: {
          'a-slider': slider,
          'a-button': button,
          'a-input-number': true,
          'a-spin': true,
          CpuDie: true,
        },
      },
    },
  )
  await flushPromises()
})
afterEach(() => {
  wrapper.unmount()
  vi.useRealTimers()
})
function firstApply() {
  return wrapper.findAll('button').find((b) => b.text() === '应用')!
}
it('shows unknown settings and absent currents without claiming hardware zeros', () => {
  expect(wrapper.text()).toContain('未填写')
  expect(wrapper.text()).toContain('未提供')
  expect(firstApply().attributes('disabled')).toBeDefined()
  expect(mocks.set).not.toHaveBeenCalled()
})
it('does not persist failed input, even after the old debounce interval', async () => {
  mocks.set.mockResolvedValue({ Success: false, Message: 'driver rejected' })
  await wrapper.get('input[type=range]').setValue('45')
  await firstApply().trigger('click')
  await flushPromises()
  await vi.advanceTimersByTimeAsync(2000)
  expect(mocks.set).toHaveBeenCalledWith(45)
  expect(mocks.save).not.toHaveBeenCalled()
  expect((mocks.store.config as { Smu: { StapmLimit: number } }).Smu.StapmLimit).toBe(0)
})
it('saves only the successfully applied field, not other edited inputs', async () => {
  mocks.set.mockResolvedValue({ Success: true })
  const sliders = wrapper.findAll('input[type=range]')
  await sliders[0]!.setValue('45')
  await sliders[1]!.setValue('30')
  await firstApply().trigger('click')
  await flushPromises()
  expect(mocks.save).toHaveBeenCalledTimes(1)
  expect(
    (mocks.store.config as { Smu: { StapmLimit: number; StapmTime: number } }).Smu,
  ).toMatchObject({ StapmLimit: 45, StapmTime: 0 })
})
it('reports save failure separately from accepted hardware command', async () => {
  mocks.set.mockResolvedValue({ Success: true })
  mocks.save.mockResolvedValue({ Success: false })
  await wrapper.get('input[type=range]').setValue('45')
  await firstApply().trigger('click')
  await flushPromises()
  expect(mocks.message.error).toHaveBeenCalledWith('驱动已接受，但配置保存失败')
  expect(mocks.message.success).not.toHaveBeenCalled()
})
