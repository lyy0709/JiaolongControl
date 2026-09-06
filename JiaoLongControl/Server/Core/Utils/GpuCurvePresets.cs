namespace JiaoLongControl.Server.Core.Utils;

/// <summary>A no-uplift starting point, never a universal stability guarantee.</summary>
public static class GpuCurvePresets
{
    public static CurvePlan Create4060LaptopConservative(CurveSnapshot snapshot, string gpuName)
    {
        if (!string.Equals(gpuName, "NVIDIA GeForce RTX 4060 Laptop GPU", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("此预设仅适用于 RTX 4060 Laptop GPU；其他型号请手动预览");
        GpuCurvePlanner.Validate(snapshot);
        if (snapshot.Offsets.Any(offset => offset != 0))
            throw new InvalidOperationException("检测到已有曲线偏移；请先恢复原设置，不在未知调参基础上叠加预设");
        var anchor = snapshot.Points.SingleOrDefault(p => p.VoltageMicroV == 900000)
            ?? throw new NotSupportedException("驱动曲线没有 900 mV 点，拒绝猜测替代锚点");
        if (anchor.FrequencyKHz % 1000 != 0)
            throw new NotSupportedException("原有频率不是整数 MHz，拒绝猜测取整");
        var plan = GpuCurvePlanner.Create(snapshot, anchor.Id, anchor.FrequencyKHz / 1000);
        if (snapshot.Points.Any(p => plan.Offsets[p.Id] > p.OffsetKHz))
            throw new InvalidOperationException("此曲线无法构成只降不升的平台，请改用手动预览");
        return plan;
    }
}
