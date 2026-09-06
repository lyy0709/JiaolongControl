namespace JiaoLongControl.Server.Core.Utils;

/// <summary>Conservative input policy, not a promise of silicon stability.</summary>
public static class TuningValidation
{
    public static bool InRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    public static string? SmuError(string command, uint value) => command switch
    {
        "STAPM Limit" or "Fast Limit" or "Slow Limit" or "PPT Limit (RSMU)" =>
            InRange(value, 5000, 150000) ? null : "功耗必须为 5–150 W；0 不表示自动",
        "STAPM Time" or "Slow Time" =>
            InRange(value, 1, 3600) ? null : "时间必须为 1–3600 秒；0 不表示自动",
        "VRM Current (MP1)" or "VRM Current (RSMU)" or "EDC Limit (MP1)" or "EDC Limit (RSMU)" =>
            InRange(value, 1000, 200000) ? null : "电流必须为 1000–200000 mA；0 不表示自动",
        "Temp Limit (MP1)" or "Temp Limit (RSMU)" =>
            InRange(value, 60, 100) ? null : "温度限制必须为 60–100°C；0 不表示自动",
        "PBO Scalar" => value == 1 ? null : "安全模式仅允许 PBO Scalar = 1",
        "OC Clock" or "Per Core OC Clock" or "OC Voltage" or "Enable OC Mode" or "Disable OC Mode" =>
            "固定频率/电压协议未经本机验证，已禁用；请使用 Windows 最大频率或 CO",
        _ => null
    };
}
