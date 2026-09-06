<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  NvidiaGpu,
  type CommandResult,
  type CurvePreview,
  type CurveSnapshot,
  type CurveTrialStatus,
} from '@/utils/bridge'

const curve = ref<CurveSnapshot | null>(null)
const preview = ref<CurvePreview | null>(null)
const trial = ref<CurveTrialStatus | null>(null)
const anchor = ref<number>()
const target = ref<number>()
const acknowledged = ref(false)
const busy = ref(false)
const message = ref('点击“读取曲线”开始；本页不会自动试写显卡。')
const failed = ref(false)
const validTarget = computed(
  () =>
    target.value !== undefined &&
    Number.isInteger(target.value) &&
    target.value >= 1000 &&
    target.value <= 3000,
)
const anchors = computed(
  () =>
    curve.value?.Points.filter((p) => p.VoltageMicroV >= 650000 && p.VoltageMicroV <= 1050000) ??
    [],
)
watch([anchor, target], () => {
  preview.value = null
  acknowledged.value = false
})
const plotted = computed(() => preview.value?.Plan.Original ?? curve.value)
const coordinates = (proposed: boolean) => {
  const points = plotted.value?.Points ?? []
  if (!points.length) return ''
  const minV = points[0]!.VoltageMicroV
  const maxV = points[points.length - 1]!.VoltageMicroV
  return points
    .map((p) => {
      const offset = proposed ? preview.value?.Plan.Offsets[p.Id] : undefined
      const freq = (p.FrequencyKHz + (offset === undefined ? 0 : offset - p.OffsetKHz)) / 1000
      return `${52 + ((p.VoltageMicroV - minV) / Math.max(1, maxV - minV)) * 490},${218 - (freq / 4000) * 188}`
    })
    .join(' ')
}
async function status() {
  try {
    const res = await NvidiaGpu.GetCurveTrialStatus()
    if (res.Success && res.Data) trial.value = res.Data
    else throw new Error(res.Message)
  } catch {
    failed.value = true
    message.value = '无法确认后端试用/恢复状态；不要假定已经恢复。请停止负载并检查应用连接。'
  }
}
async function run(action: () => Promise<Pick<CommandResult, 'Success' | 'Message'>>) {
  if (busy.value) return
  busy.value = true
  try {
    const res = await action()
    failed.value = !res.Success
    message.value = res.Message
  } catch (error) {
    failed.value = true
    message.value = String(error)
  } finally {
    await status()
    busy.value = false
  }
}
async function read() {
  preview.value = null
  curve.value = null
  anchor.value = undefined
  target.value = undefined
  await run(async () => {
    const res = await NvidiaGpu.GetVoltageFrequencyCurve()
    if (res.Success && res.Data) curve.value = res.Data
    return res
  })
}
async function plan() {
  if (anchor.value === undefined || !validTarget.value) return
  preview.value = null
  acknowledged.value = false
  await run(async () => {
    const res = await NvidiaGpu.PreviewVoltageFrequencyCurve(anchor.value!, target.value!)
    if (res.Success && res.Data) preview.value = res.Data
    return res
  })
}
async function apply() {
  if (!preview.value || !acknowledged.value) return
  const token = preview.value.Token
  await run(() => NvidiaGpu.ApplyVoltageFrequencyCurve(token, true))
  preview.value = null
  acknowledged.value = false
}
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  void status()
  timer = setInterval(() => {
    if (!busy.value) void status()
  }, 1000)
})
onUnmounted(() => clearInterval(timer)) // The rollback timer lives in the backend, not in this page.
</script>

<template>
  <section class="curve-panel bg-panel/60 border border-ink/10 rounded-xl p-5 shadow-lg space-y-4">
    <div class="flex justify-between items-center flex-wrap gap-2">
      <h2 class="font-semibold">GPU 电压 / 频率曲线</h2>
      <span class="text-xs text-amber-600">实验性 · 已验证单点读写/恢复，未验证降压稳定性</span>
    </div>
    <p class="text-xs leading-6 text-gray-500">
      参考小飞机的曲线平台操作：保留低电压侧，锚点及右侧设为目标频率。
      这不是硬电压上限，也不等于稳定降压。请先重置锁频、关闭 MSI Afterburner 等调参程序，保存工作。
    </p>
    <a-button :disabled="busy || trial?.Active" @click="read">读取曲线（只读）</a-button>
    <div v-if="plotted" class="rounded-lg bg-ink/[0.03] p-2">
      <svg
        viewBox="0 0 580 260"
        role="img"
        aria-label="GPU 电压频率曲线，灰色为读取值，紫色为待应用预览"
        class="w-full"
      >
        <g stroke="currentColor" fill="none" opacity="0.15">
          <path d="M52 30V218H542" />
          <path d="M52 77H542M52 124H542M52 171H542" />
        </g>
        <g fill="currentColor" font-size="11">
          <text x="4" y="34">4000</text>
          <text x="4" y="128">2000</text>
          <text x="4" y="222">0 MHz</text>
          <text x="52" y="245">{{ (plotted.Points[0]?.VoltageMicroV ?? 0) / 1000 }} mV</text>
          <text x="480" y="245">
            {{ (plotted.Points[plotted.Points.length - 1]?.VoltageMicroV ?? 0) / 1000 }} mV
          </text>
        </g>
        <polyline :points="coordinates(false)" fill="none" stroke="#8b95aa" stroke-width="2" />
        <polyline
          v-if="preview"
          :points="coordinates(true)"
          fill="none"
          stroke="#8b5cf6"
          stroke-width="3"
        />
      </svg>
      <p class="text-xs text-gray-500">灰：读取时曲线 · 紫：待应用预览；不是实时工作电压。</p>
    </div>
    <div v-if="curve" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
      <label class="text-xs space-y-2"
        ><span>电压锚点（驱动原有点）</span>
        <a-select
          v-model="anchor"
          aria-label="电压锚点"
          placeholder="请选择，不预填推荐值"
          :disabled="busy || trial?.Active"
        >
          <a-option v-for="point in anchors" :key="point.Id" :value="point.Id"
            >{{ point.VoltageMicroV / 1000 }} mV · {{ point.FrequencyKHz / 1000 }} MHz</a-option
          >
        </a-select>
      </label>
      <label class="text-xs space-y-2"
        ><span>目标频率（MHz）</span>
        <a-input-number
          v-model="target"
          model-event="input"
          aria-label="目标频率"
          placeholder="输入目标频率"
          :min="1000"
          :max="3000"
          :precision="0"
          :disabled="busy || trial?.Active"
        />
      </label>
    </div>
    <a-button
      v-if="curve"
      :disabled="busy || trial?.Active || anchor === undefined || !validTarget"
      @click="plan"
      >生成预览（不写入）</a-button
    >
    <div v-if="preview" class="space-y-3">
      <a-checkbox v-model="acknowledged" :disabled="busy"
        >我理解可能黑屏或重启；已关闭其他调参程序，并先重置锁频</a-checkbox
      >
      <p class="text-xs text-amber-600">
        首次写入前保存原偏移；60
        秒未确认会尝试撤销。死机或强制退出时计时器可能无法恢复，备份会保留供下次手动恢复。
      </p>
      <a-button type="primary" :disabled="busy || !acknowledged" @click="apply"
        >试用 60 秒</a-button
      >
    </div>
    <p
      role="status"
      class="text-xs leading-6 break-words"
      :class="failed ? 'text-red-500' : 'text-gray-500'"
    >
      {{ message }}
    </p>
    <div v-if="trial" class="text-xs space-y-3">
      <p>
        {{ trial.Message
        }}<span v-if="trial.SecondsRemaining !== null"
          >（剩余 {{ trial.SecondsRemaining }} 秒）</span
        >
      </p>
      <div v-if="trial.Active" class="flex flex-wrap gap-2">
        <a-button
          v-if="trial.SecondsRemaining !== null && trial.SecondsRemaining > 0"
          :disabled="busy"
          @click="run(() => NvidiaGpu.KeepVoltageFrequencyCurve(true))"
          >保留本次会话（不代表稳定）</a-button
        >
        <a-button
          status="warning"
          :disabled="busy"
          @click="run(() => NvidiaGpu.RestoreVoltageFrequencyCurve(true))"
          >恢复备份中的原偏移</a-button
        >
      </div>
    </div>
    <p class="text-xs text-gray-500">
      曲线不随开机应用，正常退出时恢复；显存超频与电压提升不在本功能范围内。
    </p>
  </section>
</template>
