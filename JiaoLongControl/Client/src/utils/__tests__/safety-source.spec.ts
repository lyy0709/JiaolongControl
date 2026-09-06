// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

const server = fileURLToPath(new URL('../../../../Server/', import.meta.url))
function read(path: string) {
  return readFileSync(server + path, 'utf8')
}
function method(source: string, name: string) {
  const start = source.indexOf(` ${name}(`)
  if (start < 0) throw new Error(`Method not found: ${name}`)
  const i = source.indexOf('{', start)
  let depth = 1
  let end = i + 1
  for (; depth > 0 && end < source.length; end++) {
    if (source[end] === '{') depth++
    if (source[end] === '}') depth--
  }
  return source.slice(i + 1, end - 1)
}
it('capability endpoint is read-only and contains no probe calls', () => {
  const body = method(read('Core/Controllers/NvidiaGpuController.cs'), 'GetOverclockCapabilities')
  expect(body).not.toMatch(/Probe|NvApiOverclock\.|SetClock|SetVoltage|SetThermal/)
})
it('curve application has disk backup, exact readback and a backend-owned timer', () => {
  const source = read('Core/Controllers/NvidiaGpuCurveController.cs')
  const apply = method(source, 'ApplyVoltageFrequencyCurve')
  expect(apply.indexOf('FileMode.CreateNew')).toBeLessThan(
    apply.indexOf('GpuCurveTransaction.Apply'),
  )
  expect(apply).toContain('file.Flush(true)')
  expect(apply).toContain('new System.Threading.Timer')
  expect(apply).toContain('acknowledgeRisk')
  expect(source).toContain('_curveAppliedVerified')
  expect(read('Core/Utils/GpuCurve.cs')).toContain('SequenceEqual(plan.Offsets)')
})
it('GPU startup requires explicit lock enablement', () => {
  expect(read('Core/Utils/SelfStart.cs')).toContain('if (!gpu.ClockLockEnabled) return;')
})
it('experimental fork startup cannot invoke the upstream installer updater', () => {
  expect(read('App.xaml.cs')).not.toContain('new InnoUpdater(')
  expect(read('App.xaml.cs')).not.toContain('CheckForUpdatesAsync(')
})
it('SMU full mailbox transaction is locked and unknown families cannot write', () => {
  const source = read('Core/Controllers/RyzenSmuController.cs')
  expect(source).toContain('lock (SmuTransactionLock) return SendCore(')
  expect(source).toContain('if (CurrentFamily == RyzenSmuFamily.Unknown) return')
  expect(source).toContain('return RyzenSmuFamily.AM5_V1;')
})
it('PawnIO cannot expose raw I/O through the public COM API or load from PATH', () => {
  const source = read('Core/Drivers/PawnIO.cs')
  expect(source).toContain('protected ulong[] Execute(')
  expect(source).toContain('protected ulong ReadMsr(')
  expect(source).not.toContain('LoadLibrary(DllName)')
  expect(source).toContain('NativeLibrary.GetExport(_dllHandle')
})
