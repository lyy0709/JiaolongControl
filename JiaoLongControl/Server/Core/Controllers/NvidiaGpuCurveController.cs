using System.IO;
using System.Management;
using System.Text.Json;
using JiaoLongControl.Server.Core.Native;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace JiaoLongControl.Server.Core.Controllers;

public partial class NvidiaGpuController
{
    private readonly object _curveGate = new();
    private CurvePlan? _curvePreview;
    private CurveSnapshot? _curveOriginal;
    private System.Threading.Timer? _curveTimer;
    private DateTime? _curveDeadline;
    private string _curveMessage = "尚未应用曲线";
    private bool _curveDisposed;
    private bool _curveAppliedVerified;
    private static string CurveBackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JiaoLongControl", "curve-backup-v1.json");
    private bool CurveChangesActive => _curveOriginal != null || File.Exists(CurveBackupPath);

    private sealed class CurveDriver(IntPtr handle) : ICurveOffsetDriver
    {
        public int[] ReadOffsets() => NvApiOverclock.ReadTable(handle).Entries.Select(p => p.FrequencyOffsetKHz).ToArray();
        public void WriteOffset(int point, int offsetKHz) => NvApiOverclock.SetClockPointOffset(handle, point, offsetKHz);
    }

    private static string CurveDevice()
    {
        // Never assume NVAPI and nvidia-smi use the same ordering on multi-GPU machines.
        if (PhysicalGPU.GetPhysicalGPUs().Length != 1)
            throw new NotSupportedException("实验性曲线仅支持单 NVIDIA GPU，禁止猜测设备顺序");
        using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
        var ids = searcher.Get().Cast<ManagementObject>()
            .Select(x => x["PNPDeviceID"]?.ToString() ?? "")
            .Where(x => x.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ids.Length != 1) throw new NotSupportedException("无法唯一识别 NVIDIA 设备");
        return ids[0] + "|" + NVIDIA.DriverVersion;
    }

    private static CurveSnapshot ReadCurve()
    {
        string device = CurveDevice();
        var handle = NvApiOverclock.GetGpuHandle(0);
        var status = NvApiOverclock.ReadClockVfStatus(handle);
        var table = NvApiOverclock.ReadTable(handle);
        var offsets = table.Entries.Select(p => p.FrequencyOffsetKHz).ToArray();
        var range = NvApiOverclock.GetClockOffsetRange(handle);
        var ids = NvApiOverclock.GetGraphicsCurvePoints(status);
        if (!status.Mask.SequenceEqual(table.Mask) || ids.Any(id => table.Entries[id].ClockType != 0))
            throw new InvalidOperationException("状态表与控制表掩码/时钟域不一致");
        var points = ids.Select(id => new CurvePoint(id, checked((int)status.Entries[id].VoltageMicroV),
            checked((int)status.Entries[id].FrequencyKHz), offsets[id])).ToArray();
        var snapshot = new CurveSnapshot(device, points, offsets, checked(range.CoreMinMhz * 1000), checked(range.CoreMaxMhz * 1000));
        GpuCurvePlanner.Validate(snapshot);
        return snapshot;
    }

    public CommandResult GetVoltageFrequencyCurve()
    {
        lock (_curveGate)
        {
            try { return new CommandResult(true, "只读曲线；读取成功不代表支持写入", ReadCurve()); }
            catch (Exception ex) { return new CommandResult(false, ex.Message); }
        }
    }

    public CommandResult PreviewVoltageFrequencyCurve(int anchorId, int targetMhz)
    {
        lock (_curveGate)
        {
            _curvePreview = null;
            try
            {
                if (CurveChangesActive) throw new InvalidOperationException("请先恢复上一份曲线备份");
                _curvePreview = GpuCurvePlanner.Create(ReadCurve(), anchorId, targetMhz);
                return new CommandResult(true, "仅预览，未修改 GPU", new {
                    Token = _curvePreview.Original.Fingerprint(), Plan = _curvePreview
                });
            }
            catch (Exception ex) { return new CommandResult(false, ex.Message); }
        }
    }

    public CommandResult Preview4060LaptopPreset()
    {
        lock (_curveGate)
        {
            _curvePreview = null;
            try
            {
                if (CurveChangesActive) throw new InvalidOperationException("请先恢复上一份曲线备份");
                _curvePreview = GpuCurvePresets.Create4060LaptopConservative(ReadCurve(), GetGPU(0).FullName);
                return new CommandResult(true, "4060 Laptop 保守试用：900 mV 原有频率平台，不提升任何曲线点；尚未写入，不保证稳定", new {
                    Token = _curvePreview.Original.Fingerprint(), Plan = _curvePreview
                });
            }
            catch (Exception ex) { return new CommandResult(false, ex.Message); }
        }
    }

    public CommandResult ApplyVoltageFrequencyCurve(string token, bool acknowledgeRisk)
    {
        lock (_curveGate)
        {
            try
            {
                if (_curveDisposed || !acknowledgeRisk || CurveChangesActive || _curvePreview == null ||
                    token != _curvePreview.Original.Fingerprint())
                    throw new InvalidOperationException("需要新的预览和风险确认；已有备份时须先恢复");
                if (_clockLocksEnabled())
                    throw new InvalidOperationException("请先在 GPU 锁频区重置锁频，并关闭其他调参程序");
                var plan = _curvePreview;
                var current = ReadCurve();
                if (current.Fingerprint() != token)
                    throw new InvalidOperationException("曲线已变化（可能是温度变化或其他程序），请重新预览");
                Directory.CreateDirectory(Path.GetDirectoryName(CurveBackupPath)!);
                // Exclusive creation: an existing recovery record must never be overwritten.
                using (var file = new FileStream(CurveBackupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(file, new CurveBackup(2, plan.Original, plan.Original.Fingerprint()));
                    file.Flush(true);
                }
                _curveOriginal = plan.Original;
                _curveAppliedVerified = false;
                _curvePreview = null;
                _curveDeadline = DateTime.UtcNow.AddSeconds(60);
                _curveTimer = new System.Threading.Timer(_ => RestoreVoltageFrequencyCurve(true), null, 60000, Timeout.Infinite);
                GpuCurveTransaction.Apply(new CurveDriver(NvApiOverclock.GetGpuHandle(0)), plan);
                _curveAppliedVerified = true;
                _curveMessage = "偏移读回已确认；60 秒内未确认将尝试恢复。不能保证黑屏/死机时计时器仍运行";
                return new CommandResult(true, _curveMessage);
            }
            catch (Exception ex)
            {
                _curveMessage = ex.Message;
                return new CommandResult(false, ex.Message);
            }
        }
    }

    public CommandResult GetCurveTrialStatus()
    {
        lock (_curveGate) return new CommandResult(true, _curveMessage, new {
            Active = CurveChangesActive,
            SecondsRemaining = _curveDeadline.HasValue ? Math.Max(0, (int)Math.Ceiling((_curveDeadline.Value - DateTime.UtcNow).TotalSeconds)) : (int?)null,
            Message = _curveOriginal == null && File.Exists(CurveBackupPath)
                ? "发现上次遗留的恢复备份；不会自动套用，请检查后手动恢复" : _curveMessage
        });
    }

    public CommandResult KeepVoltageFrequencyCurve(bool acknowledgeRisk)
    {
        lock (_curveGate)
        {
            if (!acknowledgeRisk || !_curveAppliedVerified || _curveOriginal == null || !_curveDeadline.HasValue || DateTime.UtcNow >= _curveDeadline)
                return new CommandResult(false, "没有可确认的有效试用");
            _curveTimer?.Dispose(); _curveTimer = null; _curveDeadline = null;
            _curveMessage = "本次会话保留；这不是稳定性验证。正常退出时恢复，不会开机自动应用";
            return new CommandResult(true, _curveMessage);
        }
    }

    public CommandResult RestoreVoltageFrequencyCurve(bool acknowledgeRisk)
    {
        lock (_curveGate)
        {
            if (!acknowledgeRisk) return new CommandResult(false, "请确认恢复原始偏移");
            _curveTimer?.Dispose(); _curveTimer = null; _curveDeadline = null;
            try
            {
                var original = _curveOriginal;
                if (original == null && File.Exists(CurveBackupPath))
                {
                    if (new FileInfo(CurveBackupPath).Length > 65536) throw new InvalidDataException("备份大小异常");
                    var backup = JsonSerializer.Deserialize<CurveBackup>(File.ReadAllText(CurveBackupPath))
                        ?? throw new InvalidDataException("曲线备份为空");
                    backup.Validate();
                    original = backup.Original;
                }
                if (original == null) return new CommandResult(false, "没有曲线备份");
                GpuCurvePlanner.Validate(original);
                var current = ReadCurve();
                if (original.Device != current.Device ||
                    !original.Points.Select(p => (p.Id, p.VoltageMicroV)).SequenceEqual(current.Points.Select(p => (p.Id, p.VoltageMicroV))))
                    throw new InvalidOperationException("设备、驱动或曲线布局已变化，拒绝套用旧备份");
                GpuCurveTransaction.Restore(new CurveDriver(NvApiOverclock.GetGpuHandle(0)), original);
                File.Delete(CurveBackupPath); // Our recovery record only, after verified restoration.
                _curveOriginal = null; _curvePreview = null;
                _curveAppliedVerified = false;
                _curveMessage = "已恢复修改前的偏移，并读回确认";
                return new CommandResult(true, _curveMessage);
            }
            catch (Exception ex)
            {
                _curveMessage = "恢复未完成，备份已保留：" + ex.Message;
                return new CommandResult(false, _curveMessage);
            }
        }
    }

    private void DisposeCurveTrial()
    {
        lock (_curveGate)
        {
            _curveDisposed = true;
            // A previous process's backup is only restored with explicit user confirmation.
            if (_curveOriginal != null) RestoreVoltageFrequencyCurve(true);
            _curveTimer?.Dispose();
        }
    }
}
