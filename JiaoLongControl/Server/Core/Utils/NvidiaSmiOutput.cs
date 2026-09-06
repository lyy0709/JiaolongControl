using System.Text.RegularExpressions;

namespace JiaoLongControl.Server.Core.Utils;

public static class NvidiaSmiOutput
{
    // Some command/device combinations print a refusal while exiting with 0.
    // Do not hide that diagnostic or persist a setting as successfully applied.
    public static bool IsFailure(int exitCode, string output, string error) =>
        exitCode != 0 || !string.IsNullOrWhiteSpace(error) || Regex.IsMatch(output,
            @"not supported|not permitted|insufficient permissions|\berror\b|\bfailed\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
