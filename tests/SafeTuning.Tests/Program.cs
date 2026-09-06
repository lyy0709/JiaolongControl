using System.Runtime.InteropServices;
using System.Text.Json;
using JiaoLongControl.Server.Core.Controllers;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Utils;

// No application entrypoint, native GPU calls, driver loading, stress tests or valid SMU writes.
int passed = 0;
void Test(string name, Action test)
{
    test(); passed++; Console.WriteLine("PASS " + name);
}
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
Exception Reject(Action action)
{
    try { action(); } catch (Exception error) { return error; }
    throw new Exception("Expected rejection");
}
CurveSnapshot Snapshot()
{
    var points = Enumerable.Range(0, 32).Select(i => new CurvePoint(i, 600000 + i * 12500, 1200000 + i * 45000, 30000)).ToArray();
    var offsets = new int[128];
    foreach (var p in points) offsets[p.Id] = p.OffsetKHz;
    return new("fake-device|fake-driver", points, offsets, -1000000, 1000000);
}
Test("planner preserves lower-voltage side and creates plateau without writes", () => {
    var original = Snapshot(); var before = original.Fingerprint();
    var plan = GpuCurvePlanner.Create(original, 16, 1950);
    Check(original.Fingerprint() == before);
    foreach (var p in original.Points)
        Check(p.Id < 16 ? plan.Offsets[p.Id] == p.OffsetKHz : p.FrequencyKHz + plan.Offsets[p.Id] - p.OffsetKHz == 1950000);
});
Test("invalid anchor, out-of-bounds target and excessive uplift are rejected", () => {
    Reject(() => GpuCurvePlanner.Create(Snapshot(), 125, 1950));
    Reject(() => GpuCurvePlanner.Create(Snapshot(), 16, int.MaxValue));
    Reject(() => GpuCurvePlanner.Create(Snapshot(), 16, 2400));
    Reject(() => GpuCurvePlanner.Create(Snapshot(), 16, 1000));
});
Test("unknown ranges, duplicate/invalid layout and too few points fail closed", () => {
    var s = Snapshot();
    Reject(() => GpuCurvePlanner.Validate(s with { MaxOffsetKHz = s.MinOffsetKHz }));
    Reject(() => GpuCurvePlanner.Validate(s with { Points = [s.Points[0]] }));
    var points = (CurvePoint[])s.Points.Clone(); points[1] = points[0];
    Reject(() => GpuCurvePlanner.Validate(s with { Points = points }));
});
Test("driver bounds are applied to every planned point", () => {
    Reject(() => GpuCurvePlanner.Create(Snapshot() with { MinOffsetKHz = -100000 }, 16, 1950));
});
Test("successful transaction reads back and restores nonzero original offsets", () => {
    var s = Snapshot(); var plan = GpuCurvePlanner.Create(s, 16, 1950); var driver = new FakeDriver(s.Offsets);
    GpuCurveTransaction.Apply(driver, plan); Check(driver.ReadOffsets().SequenceEqual(plan.Offsets));
    GpuCurveTransaction.Restore(driver, s); Check(driver.ReadOffsets().SequenceEqual(s.Offsets));
});
Test("stale curve causes zero writes", () => {
    var s = Snapshot(); var driver = new FakeDriver(s.Offsets); driver.Values[0]++;
    Reject(() => GpuCurveTransaction.Apply(driver, GpuCurvePlanner.Create(s, 16, 1950)));
    Check(driver.Writes == 0);
});
Test("write-then-throw midway still rolls back the throwing point", () => {
    var s = Snapshot(); var driver = new FakeDriver(s.Offsets) { FailOnceAt = 27 };
    var error = Reject(() => GpuCurveTransaction.Apply(driver, GpuCurvePlanner.Create(s, 16, 1950)));
    Check(error.Message.Contains("原偏移已恢复")); Check(driver.ReadOffsets().SequenceEqual(s.Offsets));
});
Test("silent driver refusal is not reported as success", () => {
    var s = Snapshot(); var driver = new FakeDriver(s.Offsets) { Ignore = true };
    Reject(() => GpuCurveTransaction.Apply(driver, GpuCurvePlanner.Create(s, 16, 1950)));
    Check(driver.ReadOffsets().SequenceEqual(s.Offsets));
});
Test("unexpected mutation outside active points is detected", () => {
    var s = Snapshot(); var driver = new FakeDriver(s.Offsets) { CorruptOutside = true };
    var error = Reject(() => GpuCurveTransaction.Apply(driver, GpuCurvePlanner.Create(s, 16, 1950)));
    Check(error.Message.Contains("恢复也失败"));
});
Test("rollback failure remains actionable, and later recovery can succeed", () => {
    var s = Snapshot(); var driver = new FakeDriver(s.Offsets) { FailOnceAt = 27, FailRestore = true };
    var error = Reject(() => GpuCurveTransaction.Apply(driver, GpuCurvePlanner.Create(s, 16, 1950)));
    Check(error.Message.Contains("恢复也失败")); Check(!driver.ReadOffsets().SequenceEqual(s.Offsets));
    driver.FailRestore = false; GpuCurveTransaction.Restore(driver, s); Check(driver.ReadOffsets().SequenceEqual(s.Offsets));
});
Test("recovery JSON roundtrip preserves fingerprint", () => {
    var s = Snapshot(); var parsed = JsonSerializer.Deserialize<CurveSnapshot>(JsonSerializer.Serialize(s))!;
    Check(parsed.Fingerprint() == s.Fingerprint());
});
Test("backup schema or content corruption cannot silently change restoration offsets", () => {
    var s = Snapshot(); var backup = new CurveBackup(1, s, s.Fingerprint()); backup.Validate();
    Reject(() => (backup with { Version = 999 }).Validate());
    s.Offsets[20]++;
    Reject(() => backup.Validate());
});
Test("SMU finite bounds reject zero, wraps and unsafe direct OC commands", () => {
    Check(!TuningValidation.InRange(double.NaN, 5, 150));
    Check(!TuningValidation.InRange(double.PositiveInfinity, 5, 150));
    foreach (string name in new[] { "Fast Limit", "VRM Current (MP1)", "Temp Limit (RSMU)", "STAPM Time", "PBO Scalar" })
        Check(TuningValidation.SmuError(name, 0) != null && TuningValidation.SmuError(name, uint.MaxValue) != null);
    foreach (string name in new[] { "OC Clock", "OC Voltage", "Per Core OC Clock", "Enable OC Mode", "Disable OC Mode" })
        Check(TuningValidation.SmuError(name, 0) != null);
    Check(TuningValidation.SmuError("Fast Limit", 45000) == null);
});
Test("invalid SMU endpoints reject before loading PawnIO", () => {
    using var smu = new RyzenSmuController(); // Registry/WMI CPU identification only.
    Check(!smu.SetCurveOptimizerAll(-38).Success);
    Check(!smu.SetFastLimit(double.NaN).Success);
    Check(!smu.SetOcClk(-100).Success);
    Check(!smu.SetPerCoreOcClk(0, 5200).Success);
    Check(!smu.SetCurveOptimizerPerCore(0, -23).Success);
    Check(!smu.EnableOc().Success);
    Check(!smu.IsInitialized);
});
Test("CPU matching cannot confuse desktop/future/unknown models with known mobile protocols", () => {
    Check(RyzenSmuController.DetectFamily("AMD Ryzen 9 7945HX with Radeon Graphics") == RyzenSmuFamily.AM5_V1);
    Check(RyzenSmuController.DetectFamily("AMD Ryzen 9 5900HX") == RyzenSmuFamily.FP6);
    Check(RyzenSmuController.DetectFamily("AMD Ryzen 9 5900X") == RyzenSmuFamily.Unknown);
    Check(RyzenSmuController.DetectFamily("AMD Ryzen AI 9 HX 999") == RyzenSmuFamily.Unknown);
    Check(RyzenSmuController.DetectFamily("Intel 7945HX") == RyzenSmuFamily.Unknown);
    Check(RyzenSmuController.DetectFamily("") == RyzenSmuFamily.Unknown);
});
Test("old GPU config does not silently re-enable startup locks", () => {
    Check(!new GpuSection().ClockLockEnabled);
    Check(!JsonSerializer.Deserialize<GpuSection>("{\"GpuClock\":2250}")!.ClockLockEnabled);
});
Test("bundled installer integrity and absent consent are enforced without executing", () => {
    var bytes = PawnIOSetup.ReadResource("PawnIO_setup.exe"); Check(PawnIOSetup.VerifyInstaller(bytes));
    bytes[bytes.Length / 2] ^= 1; Check(!PawnIOSetup.VerifyInstaller(bytes));
    Check(!PawnIOSetup.Install(false).Success);
    Check(PawnIOSetup.ReadResource("RyzenSMU.bin").Length > 0);
    Check(PawnIOSetup.ReadResource("AMDFamily17.bin").Length > 0);
});
Test("native managed layouts match expected buffer sizes (not a hardware ABI validation)", () => {
    var assembly = typeof(GpuCurvePlanner).Assembly;
    Check(Marshal.SizeOf(assembly.GetType("JiaoLongControl.Server.Core.Native.NvApiOverclock+ClockBoostTable")!) == 9248);
    Check(Marshal.SizeOf(assembly.GetType("JiaoLongControl.Server.Core.Native.NvApiOverclock+ClockVfStatus")!) == 7208);
});
Console.WriteLine($"{passed} tests passed; no hardware writes or installer execution.");

sealed class FakeDriver : ICurveOffsetDriver
{
    public int[] Values; public int Writes; public int FailOnceAt = -1;
    public bool Ignore, FailRestore, CorruptOutside;
    private readonly int[] original;
    public FakeDriver(int[] values) { Values = (int[])values.Clone(); original = (int[])values.Clone(); }
    public int[] ReadOffsets() => (int[])Values.Clone();
    public void WriteOffset(int point, int offset)
    {
        Writes++;
        if (Ignore) return;
        if (FailRestore && offset == original[point]) throw new Exception("restore rejected");
        Values[point] = offset;
        if (CorruptOutside) Values[127] = 123;
        if (point == FailOnceAt) { FailOnceAt = -1; throw new Exception("write-then-throw"); }
    }
}
