using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using JiaoLongControl.Server.Core.Drivers;
using JiaoLongControl.Server.Core.Utils;

namespace JiaoLongControl.Server.Core.Controllers;

public enum RyzenSmuFamily
{
    Unknown = -1,
    AM5_V1,         // Dragon Range 
    FP7_FP8,        // Rembrandt / Phoenix / HawkPoint
    FP7_FP8_Strix,  // Strix Point / Krackan Point / Strix Halo 
    FP6             // Cezanne / Lucienne / Renoir
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class RyzenSmuController : PawnIO 
{
    private static readonly object SmuTransactionLock = new();
    public RyzenSmuFamily CurrentFamily { get; private set; } = RyzenSmuFamily.Unknown;

    public RyzenSmuController()
    {
        try
        {
            string cpuName = GetCpuNameFast();
            if (string.IsNullOrWhiteSpace(cpuName))
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    cpuName = obj["Name"]?.ToString() ?? "";
                    break;
                }
            }

            CurrentFamily = DetectFamily(cpuName);
        }
        catch { }
    }

    private static string GetCpuNameFast()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private CommandResult Send(uint cmd, uint arg, bool isMp1, string name)
    {
        if (CurrentFamily == RyzenSmuFamily.Unknown) return new CommandResult(false, "未知 CPU 协议，禁止回退到 AM5 寄存器写入");
        var error = TuningValidation.SmuError(name, arg);
        if (error != null) return new CommandResult(false, error);
        lock (SmuTransactionLock) return SendCore(cmd, arg, isMp1, name);
    }

    [ComVisible(false)]
    public static RyzenSmuFamily DetectFamily(string cpuName)
    {
        // Do not match desktop 5900X as mobile 5900HX, or every future "AI 9".
        if (string.IsNullOrWhiteSpace(cpuName) || !Regex.IsMatch(cpuName, @"\bAMD\b", RegexOptions.IgnoreCase))
            return RyzenSmuFamily.Unknown;
        bool Match(string pattern) => Regex.IsMatch(cpuName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Match(@"\b(7945HX3D|7945HX|7845HX|7745HX)\b")) return RyzenSmuFamily.AM5_V1;
        if (Match(@"\bAI 9 (HX 370|365)\b")) return RyzenSmuFamily.FP7_FP8_Strix;
        if (Match(@"\b(7735(H|HS|U)|6800(H|HS|U)|6900(HX|HS)|7840(H|HS|U)|7940HS|8840(HS|U)|8845HS)\b")) return RyzenSmuFamily.FP7_FP8;
        if (Match(@"\b(5800(H|HS|U)|5900(HX|HS)|5600(H|HS|U)|4800(H|HS|U)|4600(H|HS|U))\b")) return RyzenSmuFamily.FP6;
        return RyzenSmuFamily.Unknown;
    }

    private CommandResult SendCore(uint cmd, uint arg, bool isMp1, string name)
    {
        uint addrMsg, addrRsp, addrArg;

        switch (CurrentFamily)
        {
            case RyzenSmuFamily.FP6:
                addrMsg = isMp1 ? 0x3B10528u : 0x03B10A20u;
                addrRsp = isMp1 ? 0x3B10564u : 0x03B10A80u;
                addrArg = isMp1 ? 0x3B10998u : 0x03B10A88u;
                break;
            case RyzenSmuFamily.FP7_FP8:
                addrMsg = isMp1 ? 0x3B10528u : 0x03B10A20u;
                addrRsp = isMp1 ? 0x3B10578u : 0x03B10A80u;
                addrArg = isMp1 ? 0x3B10998u : 0x03B10A88u;
                break;
            case RyzenSmuFamily.FP7_FP8_Strix:
                addrMsg = isMp1 ? 0x3B10928u : 0x03B10A20u;
                addrRsp = isMp1 ? 0x3B10978u : 0x03B10A80u;
                addrArg = isMp1 ? 0x3B10998u : 0x03B10A88u;
                break;
            case RyzenSmuFamily.AM5_V1:
            default:
                addrMsg = isMp1 ? 0x3B10530u : 0x03B10524u;
                addrRsp = isMp1 ? 0x3B1057Cu : 0x03B10570u;
                addrArg = isMp1 ? 0x3B109C4u : 0x03B10A40u;
                break;
        }

        try
        {
            uint rsp = 0;
            for (int i = 0; i < 8096; ++i)
            {
                rsp = (uint)Execute("ioctl_read_smu_register", new ulong[] { addrRsp }, 1)[0];
                if (rsp != 0) break;
            }
            if (rsp == 0) return new CommandResult(false, $"{name} 设置失败: SMU 忙碌超时");
            
            Execute("ioctl_write_smu_register", new ulong[] { addrRsp, 0 }, 0);
            
            // 写入主要参数
            Execute("ioctl_write_smu_register", new ulong[] { addrArg, arg }, 0);
            // 清空其余 5 个参数槽，确保无脏数据
            for (uint i = 1; i < 6; i++)
            {
                Execute("ioctl_write_smu_register", new ulong[] { addrArg + (i * 4), 0 }, 0);
            }

            Execute("ioctl_write_smu_register", new ulong[] { addrMsg, cmd }, 0);
            
            rsp = 0;
            for (int i = 0; i < 8096; ++i)
            {
                rsp = (uint)Execute("ioctl_read_smu_register", new ulong[] { addrRsp }, 1)[0];
                if (rsp != 0) break;
            }
            
            if (rsp == 0) return new CommandResult(false, $"{name} 设置失败: SMU 响应超时");
            if (rsp == 1) return new CommandResult(true, $"{name} 设置成功");
            if (rsp == 0xFD) return new CommandResult(false, $"{name} 设置失败: 条件不满足");
            if (rsp == 0xFC) return new CommandResult(false, $"{name} 设置失败: 指令被拒绝(繁忙)");
            if (rsp == 0xFE) return new CommandResult(false, $"{name} 设置失败: 未知指令", rsp);

            return new CommandResult(false, $"{name} 设置失败: SMU 错误码 0x{rsp:X}");
        }
        catch (Exception ex)
        {
            return new CommandResult(false, $"{name} 设置失败: 底层驱动异常 {ex.Message}");
        }
    }

    private CommandResult TrySend(uint arg, string name, params (uint cmd, bool isMp1)[] commands)
    {
        CommandResult? lastResult = null;
        foreach (var (cmd, isMp1) in commands)
        {
            lastResult = Send(cmd, arg, isMp1, name);
            if (lastResult.Success || lastResult.Data is not uint status || status != 0xFE)
            {
                return lastResult;
            }
        }
        return lastResult ?? new CommandResult(false, $"{name} 设置失败: 无可用指令");
    }

    #region (Power Limits - PPT)
    public CommandResult SetStapmLimit(double watts)
    {
        if (!TuningValidation.InRange(watts, 5, 150)) return new CommandResult(false, "功耗必须为 5–150 W");
        uint arg = (uint)(watts * 1000);
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(arg, "STAPM Limit", (0x14, true), (0x31, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(arg, "STAPM Limit", (0x14, true), (0x31, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(arg, "STAPM Limit", (0x14, true), (0x31, false)),
            _ => TrySend(arg, "STAPM Limit", (0x4F, true))
        };
    }

    public CommandResult SetStapmTime(uint seconds)
    {
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(seconds, "STAPM Time", (0x18, true), (0x36, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(seconds, "STAPM Time", (0x18, true), (0x36, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(seconds, "STAPM Time", (0x18, true), (0x36, false)),
            _ => TrySend(seconds, "STAPM Time", (0x53, true))
        };
    }

    public CommandResult SetFastLimit(double watts)
    {
        if (!TuningValidation.InRange(watts, 5, 150)) return new CommandResult(false, "功耗必须为 5–150 W");
        uint arg = (uint)(watts * 1000);
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(arg, "Fast Limit", (0x15, true), (0x32, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(arg, "Fast Limit", (0x15, true), (0x32, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(arg, "Fast Limit", (0x15, true), (0x32, false)),
            _ => TrySend(arg, "Fast Limit", (0x3E, true))
        };
    }

    public CommandResult SetSlowLimit(double watts)
    {
        if (!TuningValidation.InRange(watts, 5, 150)) return new CommandResult(false, "功耗必须为 5–150 W");
        uint arg = (uint)(watts * 1000);
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(arg, "Slow Limit", (0x16, true), (0x33, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(arg, "Slow Limit", (0x16, true), (0x33, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(arg, "Slow Limit", (0x16, true), (0x33, false)),
            _ => TrySend(arg, "Slow Limit", (0x5F, true), (0xCB, false))
        };
    }

    public CommandResult SetSlowTime(uint seconds)
    {
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(seconds, "Slow Time", (0x17, true), (0x35, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(seconds, "Slow Time", (0x17, true), (0x35, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(seconds, "Slow Time", (0x17, true), (0x35, false)),
            _ => TrySend(seconds, "Slow Time", (0x60, true))
        };
    }

    public CommandResult SetPptLimitRsmu(double watts)
    {
        if (!TuningValidation.InRange(watts, 5, 150)) return new CommandResult(false, "功耗必须为 5–150 W");
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x33u, 
            RyzenSmuFamily.FP7_FP8 => 0x31u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x31u, 
            _ => 0x56u 
        };
        return Send(cmd, (uint)(watts * 1000), false, "PPT Limit (RSMU)");
    }
    #endregion

    #region (Current & Temp Limits)
    public CommandResult SetVrmCurrentMp1(uint milliamps)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x1Au, 
            RyzenSmuFamily.FP7_FP8 => 0x1Au, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x1Au, 
            _ => 0x3Cu 
        };
        return Send(cmd, milliamps, true, "VRM Current (MP1)");
    }

    public CommandResult SetVrmCurrentRsmu(uint milliamps)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x38u, 
            RyzenSmuFamily.FP7_FP8 => 0x38u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x38u, 
            _ => 0x57u 
        };
        return Send(cmd, milliamps, false, "VRM Current (RSMU)");
    }

    public CommandResult SetEdcLimitMp1(uint milliamps)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x1Cu, 
            RyzenSmuFamily.FP7_FP8 => 0x1Cu, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x1Cu, 
            _ => 0x3Du 
        };
        return Send(cmd, milliamps, true, "EDC Limit (MP1)");
    }

    public CommandResult SetEdcLimitRsmu(uint milliamps)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x3Au, 
            RyzenSmuFamily.FP7_FP8 => 0x3Au, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x3Au, 
            _ => 0x58u 
        };
        return Send(cmd, milliamps, false, "EDC Limit (RSMU)");
    }

    public CommandResult SetTempLimitMp1(uint celsius)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x19u, 
            RyzenSmuFamily.FP7_FP8 => 0x19u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x19u, 
            _ => 0x3Fu 
        };
        return Send(cmd, celsius, true, "Temp Limit (MP1)");
    }

    public CommandResult SetTempLimitRsmu(uint celsius)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x37u, 
            RyzenSmuFamily.FP7_FP8 => 0x37u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x37u, 
            _ => 0x59u 
        };
        return Send(cmd, celsius, false, "Temp Limit (RSMU)");
    }
    #endregion

    #region (PBO & Overclocking)
    public CommandResult SetPboScalar(uint value)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x3Fu, 
            RyzenSmuFamily.FP7_FP8 => 0x3Eu, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x3Eu, 
            _ => 0x5Bu 
        };
        return Send(cmd, value, false, "PBO Scalar");
    }

    public CommandResult SetOcClk(int mhz)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x19u, 
            RyzenSmuFamily.FP7_FP8 => 0x19u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x19u, 
            _ => 0x5Fu 
        };
        return Send(cmd, (uint)mhz, false, "OC Clock");
    }

    public CommandResult SetPerCoreOcClk(uint coreIdx, uint mhz)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x1Au, 
            RyzenSmuFamily.FP7_FP8 => 0x1Au, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x1Au, 
            _ => 0x60u 
        };
        return Send(cmd, (coreIdx << 8) | (mhz & 0xFF), false, "Per Core OC Clock");
    }

    public CommandResult SetOcVolt(uint millivolts)
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x1Bu, 
            RyzenSmuFamily.FP7_FP8 => 0x1Bu, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x1Bu, 
            _ => 0x61u 
        };
        return Send(cmd, millivolts, false, "OC Voltage");
    }

    public CommandResult EnableOc()
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x17u, 
            RyzenSmuFamily.FP7_FP8 => 0x17u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x17u, 
            _ => 0x5Du 
        };
        return Send(cmd, 0, false, "Enable OC Mode");
    }

    public CommandResult DisableOc()
    {
        uint cmd = CurrentFamily switch { 
            RyzenSmuFamily.FP6 => 0x18u, 
            RyzenSmuFamily.FP7_FP8 => 0x18u, 
            RyzenSmuFamily.FP7_FP8_Strix => 0x18u, 
            _ => 0x5Eu 
        };
        return Send(cmd, 0, false, "Disable OC Mode");
    }
    #endregion

    #region (Curve Optimizer)
    public CommandResult SetCurveOptimizerAll(int value)
    {
        if (value is < -30 or > 0) return new CommandResult(false, "安全模式 CO 范围为 -30–0；范围内仍须验证稳定性");
        uint arg = (uint)value & 0xFFFFFu;
        return CurrentFamily switch {
            RyzenSmuFamily.FP6 => TrySend(arg, "Curve Optimizer All", (0x55, true), (0xB1, false)),
            RyzenSmuFamily.FP7_FP8 => TrySend(arg, "Curve Optimizer All", (0x4C, true), (0x5D, false)),
            RyzenSmuFamily.FP7_FP8_Strix => TrySend(arg, "Curve Optimizer All", (0x4C, true), (0x5D, false)),
            _ => TrySend(arg, "Curve Optimizer All", (0x36, true), (0x07, false))
        };
    }

    public CommandResult SetCurveOptimizerPerCore(uint coreIdx, int value)
    {
        return new CommandResult(false, "单核编号/CCD 映射尚未验证，已禁用单核写入；请使用全核 CO");
    }
    #endregion

    #region (Power Telemetry)
    private static LibreHardwareMonitor.Hardware.Computer? _lhmComputer;
    private static readonly object _lhmLock = new();
    
    private const uint MsrFidvidStatus = 0xC0010293;

    public double? GetCoreVoltage()
    {
        try
        {
            IntPtr thread = Native.Kernel32.GetCurrentThread();
            IntPtr originalMask = Native.Kernel32.SetThreadAffinityMask(thread, new IntPtr(unchecked((long)-1)));
            if (originalMask == IntPtr.Zero)
                return ReadVidVoltage();

            try
            {
                double? minVolts = null;
                int coreCount = Environment.ProcessorCount;
                for (int i = 0; i < coreCount; i++)
                {
                    IntPtr prev = Native.Kernel32.SetThreadAffinityMask(thread, new IntPtr(1L << i));
                    if (prev == IntPtr.Zero)
                        continue; // 进程亲和性不允许该核心

                    double? volts = ReadVidVoltage();
                    if (volts.HasValue && (minVolts == null || volts.Value < minVolts.Value))
                        minVolts = volts.Value;
                }

                return minVolts;
            }
            finally
            {
                Native.Kernel32.SetThreadAffinityMask(thread, originalMask);
            }
        }
        catch
        {
            return null;
        }
    }

    private double? ReadVidVoltage()
    {
        try
        {
            ulong raw = ReadMsr(MsrFidvidStatus);
            uint vid = (uint)((raw >> 6) & 0xFF);
            double volts = 1.550 - vid * 0.00625;
            return volts is >= 0.4 and <= 1.8 ? volts : null;
        }
        catch
        {
            return null;
        }
    }

    private static LibreHardwareMonitor.Hardware.Computer GetOrCreateLhm()
    {
        if (_lhmComputer != null) return _lhmComputer;
        lock (_lhmLock)
        {
            if (_lhmComputer != null) return _lhmComputer;
            var computer = new LibreHardwareMonitor.Hardware.Computer
            {
                IsCpuEnabled = true,
            };
            computer.Open();
            _lhmComputer = computer;
            return computer;
        }
    }

    public CommandResult GetSmuTelemetry()
    {
        try
        {
            double ppt = 0;
            double? tdc = null;
            double? edc = null;
            double temp = 0;
            double freq = 0;
            int usage = 0;
            try
            {
                var computer = GetOrCreateLhm();

                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                        continue;

                    hardware.Update();

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.Value == null) continue;
                        float val = sensor.Value.Value;

                        switch (sensor.SensorType)
                        {
                            case LibreHardwareMonitor.Hardware.SensorType.Power:
                                if (sensor.Name.Contains("Package") && ppt == 0)
                                    ppt = Math.Round(val, 1);
                                break;

                            case LibreHardwareMonitor.Hardware.SensorType.Temperature:
                                if ((sensor.Name.Contains("Core") && sensor.Name.Contains("Max")) ||
                                     sensor.Name.Contains("Tctl") || sensor.Name.Contains("Tdie"))
                                {
                                    if (val > temp) temp = Math.Round(val, 1);
                                }
                                break;

                            case LibreHardwareMonitor.Hardware.SensorType.Frequency:
                                if (sensor.Name.Contains("Core #1") || sensor.Name.Contains("Bus Speed"))
                                    freq = Math.Round(val, 0);
                                break;

                            case LibreHardwareMonitor.Hardware.SensorType.Load:
                                if (sensor.Name.Contains("Total"))
                                    usage = (int)Math.Round(val);
                                break;
                        }
                    }

                    // Power (W) is not current (A). Do not fabricate TDC/EDC.

                    break;
                }
            }
            catch (Exception lhmEx)
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                    double maxTemp = 0;
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        uint raw = Convert.ToUInt32(obj["CurrentTemperature"]);
                        double t = (raw - 2732) / 10.0;
                        if (t > maxTemp) maxTemp = t;
                    }
                    temp = Math.Round(maxTemp, 1);
                }
                catch { }

                try
                {
                    using var freqCounter = new System.Diagnostics.PerformanceCounter(
                        "Processor Information", "% Processor Performance", "_Total");
                    freqCounter.NextValue();
                    System.Threading.Thread.Sleep(100);
                    float perfPct = freqCounter.NextValue();
                    using var wmi = new System.Management.ManagementObjectSearcher(
                        "SELECT MaxClockSpeed FROM Win32_Processor");
                    foreach (System.Management.ManagementObject obj in wmi.Get())
                    {
                        freq = Math.Round(perfPct / 100.0 * Convert.ToUInt32(obj["MaxClockSpeed"]), 0);
                        break;
                    }
                }
                catch { }

                try
                {
                    using var usageCounter = new System.Diagnostics.PerformanceCounter(
                        "Processor", "% Processor Time", "_Total");
                    usageCounter.NextValue();
                    System.Threading.Thread.Sleep(100);
                    usage = (int)Math.Round(usageCounter.NextValue());
                }
                catch { }
            }

            return new CommandResult(true, "获取成功", new
            {
                Ppt = ppt,
                Tdc = tdc,
                Edc = edc,
                Temp = temp,
                FreqMhz = (int)freq,
                Usage = usage,
            });
        }
        catch (Exception ex)
        {
            return new CommandResult(false, $"遥测读取失败: {ex.Message}");
        }
    }
    #endregion
}
