using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceProcess;
using Microsoft.Win32;

namespace JiaoLongControl.Server.Core.Utils;

public static class PawnIOSetup
{
    public const string Version = "2.2.0";
    public const string Sha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private static readonly object Gate = new();
    private static Process? _installer;

    public static byte[] ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JiaoLongControl.PawnIO." + name)
            ?? throw new FileNotFoundException("缺少内置 PawnIO 资源：" + name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static bool VerifyInstaller(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)) == Sha256;

    public static CommandResult Status()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            string version = key?.GetValue("DisplayVersion")?.ToString() ?? "未检测到";
            string state = "未安装";
            try { using var service = new ServiceController("PawnIO"); state = service.Status.ToString(); }
            catch (InvalidOperationException) { }
            return new CommandResult(true, "只读检测，不加载或安装驱动", new {
                Version = version, ServiceState = state, BundledVersion = Version
            });
        }
        catch (Exception ex) { return new CommandResult(false, "检测失败：" + ex.Message); }
    }

    public static CommandResult Install(bool acknowledgeDriverInstallation)
    {
        lock (Gate)
        {
            try
            {
                if (!acknowledgeDriverInstallation) return new CommandResult(false, "必须明确同意安装内核驱动");
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (System.Version.TryParse(key?.GetValue("DisplayVersion")?.ToString(), out var installed) &&
                    installed > new System.Version(2, 2, 0, 0))
                    return new CommandResult(false, "已安装更高版本，禁止用内置包降级；请从官网维护现有安装");
                if (_installer != null && !_installer.HasExited) return new CommandResult(false, "安装器正在运行");
                var bytes = ReadResource("PawnIO_setup.exe");
                if (!VerifyInstaller(bytes)) return new CommandResult(false, "内置安装包 SHA-256 校验失败，已阻止运行");
                string directory = Path.Combine(Path.GetTempPath(), "JiaoLongControl-PawnIO-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "PawnIO_setup.exe");
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes); file.Flush(true);
                }
                // Image loading requires the writer to be closed. Hold a read-only handle
                // denying modification/deletion from verification through process creation.
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (Convert.ToHexString(SHA256.HashData(file)) != Sha256)
                        throw new InvalidDataException("释放后的安装包校验失败");
                    _installer?.Dispose();
                    _installer = Process.Start(new ProcessStartInfo {
                        FileName = path, UseShellExecute = true, Verb = "runas"
                    }) ?? throw new InvalidOperationException("无法启动安装器");
                }
                // Official interactive UI owns consent/restart options. Never silently install or restart.
                return new CommandResult(true, "已打开官方安装器（并非安装成功）。完成后刷新状态并重新打开本应用；不会自动重启电脑");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            { return new CommandResult(false, "已取消管理员授权，未由本应用安装驱动"); }
            catch (Exception ex) { return new CommandResult(false, "启动安装器失败：" + ex.Message); }
        }
    }
}
