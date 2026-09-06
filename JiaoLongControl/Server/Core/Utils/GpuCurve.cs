using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace JiaoLongControl.Server.Core.Utils;

public sealed record CurvePoint(int Id, int VoltageMicroV, int FrequencyKHz, int OffsetKHz);
public sealed record CurveSnapshot(string Device, CurvePoint[] Points, int[] Offsets, int MinOffsetKHz, int MaxOffsetKHz)
{
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
}
public sealed record CurvePlan(CurveSnapshot Original, int AnchorId, int TargetMhz, int[] Offsets);
public sealed record CurveBackup(int Version, CurveSnapshot Original, string Fingerprint)
{
    public void Validate()
    {
        if (Version != 2 || Original == null || Fingerprint != Original.Fingerprint())
            throw new InvalidDataException("曲线备份版本/完整性校验失败，旧版布局不能用于恢复");
        GpuCurvePlanner.Validate(Original);
    }
}

/// <summary>Pure planning only. A plateau is NOT a hard voltage limit or stability guarantee.</summary>
public static class GpuCurvePlanner
{
    public static void Validate(CurveSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Device) || snapshot.Offsets.Length != 255 ||
            snapshot.Points.Length < 16 || snapshot.Points.Length > 255 ||
            snapshot.MinOffsetKHz >= snapshot.MaxOffsetKHz ||
            snapshot.MinOffsetKHz > 0 || snapshot.MaxOffsetKHz < 0)
            throw new InvalidOperationException("驱动返回的曲线/偏移范围不完整，禁止写入");
        var ids = new HashSet<int>();
        int previousVoltage = 0;
        foreach (var point in snapshot.Points)
        {
            if (point.Id is < 0 or >= 255 || !ids.Add(point.Id) ||
                point.VoltageMicroV is < 400000 or > 1500000 || point.VoltageMicroV <= previousVoltage ||
                point.FrequencyKHz is < 100000 or > 4000000 ||
                point.OffsetKHz != snapshot.Offsets[point.Id] || Math.Abs((long)point.OffsetKHz) > 1000000)
                throw new InvalidOperationException($"核心曲线点 #{point.Id} 布局/数值异常：{point.VoltageMicroV} µV / {point.FrequencyKHz} kHz；禁止写入");
            previousVoltage = point.VoltageMicroV;
        }
    }

    public static CurvePlan Create(CurveSnapshot snapshot, int anchorId, int targetMhz)
    {
        Validate(snapshot);
        var anchor = snapshot.Points.SingleOrDefault(p => p.Id == anchorId)
            ?? throw new ArgumentException("请选择驱动返回的电压点");
        if (anchor.VoltageMicroV is < 650000 or > 1050000 || targetMhz is < 1000 or > 3000 ||
            targetMhz * 1000 > anchor.FrequencyKHz + 200000)
            throw new ArgumentException("试用范围：650–1050 mV、1000–3000 MHz，锚点提升不超过 200 MHz；这不是推荐参数");
        int target = checked(targetMhz * 1000);
        if (snapshot.Points.Any(p => p.VoltageMicroV < anchor.VoltageMicroV && p.FrequencyKHz > target))
            throw new ArgumentException("低电压侧已有频率高于目标，请选择更早的锚点或更高的目标频率");
        var offsets = (int[])snapshot.Offsets.Clone();
        foreach (var point in snapshot.Points.Where(p => p.VoltageMicroV >= anchor.VoltageMicroV))
        {
            int offset = checked(point.OffsetKHz + target - point.FrequencyKHz);
            if (offset < Math.Max(snapshot.MinOffsetKHz, -1000000) ||
                offset > Math.Min(snapshot.MaxOffsetKHz, 1000000))
                throw new ArgumentException("预览超出驱动偏移范围，未写入任何参数");
            offsets[point.Id] = offset;
        }
        if (offsets.SequenceEqual(snapshot.Offsets)) throw new ArgumentException("曲线没有变化");
        return new CurvePlan(snapshot, anchorId, targetMhz, offsets);
    }
}

public interface ICurveOffsetDriver
{
    int[] ReadOffsets();
    void WriteOffset(int point, int offsetKHz);
}

/// <summary>Keep the backup until a complete readback confirms restoration.</summary>
public static class GpuCurveTransaction
{
    public static void Apply(ICurveOffsetDriver driver, CurvePlan plan)
    {
        if (!driver.ReadOffsets().SequenceEqual(plan.Original.Offsets))
            throw new InvalidOperationException("曲线已被其他程序修改，请重新读取和预览");
        try
        {
            // Higher-voltage points first: do not raise the anchor before lowering the plateau.
            foreach (var point in plan.Original.Points.Reverse())
                if (plan.Offsets[point.Id] != plan.Original.Offsets[point.Id])
                    driver.WriteOffset(point.Id, plan.Offsets[point.Id]);
            if (!driver.ReadOffsets().SequenceEqual(plan.Offsets))
                throw new InvalidOperationException("驱动读回与预览不一致（拒绝、取整或布局不兼容）");
        }
        catch (Exception applyError)
        {
            try { Restore(driver, plan.Original); }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException($"应用失败：{applyError.Message}；恢复也失败：{restoreError.Message}。备份已保留，请停止游戏并手动恢复", applyError);
            }
            throw new InvalidOperationException($"应用失败，原偏移已恢复并读回确认：{applyError.Message}", applyError);
        }
    }

    public static void Restore(ICurveOffsetDriver driver, CurveSnapshot original)
    {
        GpuCurvePlanner.Validate(original);
        var errors = new List<string>();
        foreach (var point in original.Points)
        {
            // Anchor zero is not editable. Verify it in the full readback instead.
            if (point.Id == 0) continue;
            // Also attempt the point whose write threw: a failed call may have changed hardware.
            try { driver.WriteOffset(point.Id, original.Offsets[point.Id]); }
            catch (Exception ex) { errors.Add($"#{point.Id}: {ex.Message}"); }
        }
        if (!driver.ReadOffsets().SequenceEqual(original.Offsets))
            throw new InvalidOperationException("原始偏移读回不一致；" + string.Join("; ", errors.Take(3)));
    }
}
