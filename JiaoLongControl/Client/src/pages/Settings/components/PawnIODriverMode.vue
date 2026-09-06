<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Message } from '@arco-design/web-vue'
import SettingCardComponent from '@/components/common/SettingCardComponent.vue'
import { SystemInfo, type PawnIOStatus } from '@/utils/bridge'

const PAWNIO_URL = 'https://pawnio.eu/'
const loading = ref(false)
const status = ref<PawnIOStatus | null>(null)
const acknowledged = ref(false)
async function refresh() {
  try {
    const res = await SystemInfo.GetPawnIOStatus()
    if (res.Success && res.Data) status.value = res.Data
    else Message.error(res.Message)
  } catch { Message.error('无法读取驱动状态') }
}
onMounted(refresh)
async function install() {
  if (!acknowledged.value || loading.value) return
  loading.value = true
  try {
    const res = await SystemInfo.InstallPawnIO(true)
    if (res.Success) Message.info(res.Message)
    else Message.error(res.Message)
  } catch { Message.error('无法启动安装器') }
  finally { loading.value = false; acknowledged.value = false; await refresh() }
}

async function openWebsite() {
  loading.value = true
  try {
    const res = await SystemInfo.OpenUrl(PAWNIO_URL)
    if (res.Success) {
      Message.success('已打开 PawnIO 官网')
    } else {
      Message.error(res.Message || '打开失败')
    }
  } catch (e) {
    Message.error('打开失败')
    console.error(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <setting-card-component
    title="PawnIO 驱动"
    description="CO / Ryzen SMU 仍需要内核驱动。本程序内置官方签名安装包与模块；只有点击安装才会启动安装器。安装时请选择官方签名版，不要选择 Unrestricted 版。普通 CPU 功耗与 Windows 频率设置不要求 PawnIO。"
  >
    <template #extra>
      <div class="space-y-3 max-w-sm text-xs">
        <p v-if="status">已安装：{{ status.Version }} · 服务：{{ status.ServiceState }} · 内置：{{ status.BundledVersion }}</p>
        <a-checkbox v-model="acknowledged" :disabled="loading">我同意打开官方内核驱动安装器并自行确认管理员授权</a-checkbox>
        <div class="flex flex-wrap gap-2">
          <a-button type="primary" :disabled="!acknowledged" :loading="loading" @click="install">打开内置安装器</a-button>
          <a-button :disabled="loading" @click="refresh">刷新状态</a-button>
          <a-button :disabled="loading" @click="openWebsite">官网</a-button>
        </div>
      </div>
    </template>
  </setting-card-component>
</template>

<style scoped></style>
