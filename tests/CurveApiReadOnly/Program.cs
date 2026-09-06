using System.Text.Json;
using JiaoLongControl.Server.Core.Controllers;
using JiaoLongControl.Server.Core.Utils;

// Explicit real-GPU read/preview integration check; not part of automated unit
// tests. Do not initialize WPF, Bridge.Instance, PawnIO or any setter.
using var gpu = new NvidiaGpuController();
var read = gpu.GetVoltageFrequencyCurve();
if (!read.Success || read.Data is not CurveSnapshot snapshot) throw new InvalidOperationException(read.Message);
var anchor = snapshot.Points.Single(p => p.VoltageMicroV == 900000);
var preview = gpu.PreviewVoltageFrequencyCurve(anchor.Id, anchor.FrequencyKHz / 1000);
if (!preview.Success) throw new InvalidOperationException(preview.Message);
var after = gpu.GetVoltageFrequencyCurve();
if (!after.Success || after.Data is not CurveSnapshot second || !second.Offsets.SequenceEqual(snapshot.Offsets))
    throw new InvalidOperationException("Read-only API check failed or external offsets changed");
Console.WriteLine(JsonSerializer.Serialize(new
{
    ReadSuccess = true, PreviewSuccess = true, CorePoints = snapshot.Points.Length,
    OffsetSlots = snapshot.Offsets.Length, AnchorMv = 900, PreviewMhz = anchor.FrequencyKHz / 1000,
    OffsetsUnchanged = true, HardwareWrites = false
}, new JsonSerializerOptions { WriteIndented = true }));
