using System.Globalization;

namespace JiaoLongControl.Server.Core.Utils;

/// <summary>Discrete clocks reported by the driver, not a continuous OC range or a write-support probe.</summary>
public sealed record GpuMemoryClockOptions(int Min, int Max, int[] Values)
{
    public static GpuMemoryClockOptions Parse(string output)
    {
        var clocks = new SortedSet<int>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var mhz)
                || mhz < 100 || mhz > 12000)
                throw new InvalidOperationException("驱动未返回有效的显存档位列表，已禁止猜测范围");
            clocks.Add(mhz);
        }
        if (clocks.Count == 0)
            throw new InvalidOperationException("驱动未返回显存档位，已禁止猜测范围");
        return new(clocks.Min, clocks.Max, clocks.ToArray());
    }

    public bool Contains(int mhz) => Values.Contains(mhz);
}
