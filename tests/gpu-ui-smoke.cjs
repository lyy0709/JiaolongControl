// Full real Vue/Arco page, fake host only. Never attaches to the installed app.
const { chromium } = require('playwright')
const path = require('node:path')
const assert = require('node:assert/strict')
;(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1024, height: 1100 } })
    const errors = []
    page.on('pageerror', e => errors.push(e.message))
    await page.goto('http://127.0.0.1:5179/safe-tuning-preview.html?gpu=1')
    // The production page scrolls inside the WPF viewport; expand only the mock
    // document's height for full-page screenshots, without changing its width/layout.
    await page.addStyleTag({ content: 'html, body, #app {height:auto!important;overflow:visible!important} #app .h-full.overflow-y-auto {height:auto!important;overflow:visible!important}' })
    await page.getByLabel('显存锁频档位', { exact: true }).click()
    await page.getByText('7001 MHz', { exact: true }).click()
    assert.deepEqual(await page.evaluate(() => window.mockWrites), [])
    await page.getByRole('button', { name: '应用', exact: true }).click()
    await page.waitForFunction(() => window.mockWrites.length === 3)
    assert.deepEqual(await page.evaluate(() => window.mockWrites), [['core', 2250], ['memory', 7001], ['save']])
    await page.getByRole('button', { name: '读取曲线（只读）' }).click()
    await page.getByRole('button', { name: '生成 4060 保守预设（仅预览）' }).click()
    await page.getByRole('button', { name: '试用 60 秒' }).waitFor()
    assert(await page.getByRole('button', { name: '试用 60 秒' }).isDisabled())
    await page.getByText('驱动已接受锁频请求并保存；实际频率请查看实时监控', { exact: true }).waitFor({ state: 'hidden' })
    await page.screenshot({ path: path.join(__dirname, '../bin/gpu-safe3-desktop.png'), fullPage: true })
    await page.setViewportSize({ width: 560, height: 1050 })
    await page.screenshot({ path: path.join(__dirname, '../bin/gpu-safe3-narrow.png'), fullPage: true })
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    await page.goto('http://127.0.0.1:5179/safe-tuning-preview.html?gpu=1&memory=unsupported')
    await page.getByText('模拟：此驱动未返回显存档位', { exact: true }).waitFor()
    assert(await page.getByRole('button', { name: '应用', exact: true }).isDisabled())
    assert.deepEqual(await page.evaluate(() => window.mockWrites), [])
    assert.deepEqual(errors, [])
    console.log('PASS full GPU UI: discrete memory choices, explicit apply, no writes during reads/preset, failed query gate, 1024/560px')
  } finally { await browser.close() }
})().catch(e => { console.error(e); process.exitCode = 1 })
