using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using JiaoLongControl.Server.Core.Native;
using JiaoLongControl.Server.Core.Utils;

namespace JiaoLongControl.Server.Core.Drivers;

/// <summary>
/// PawnIO 客户端。
/// 官方安装器与签名模块内置；驱动仍须由用户明确安装。
/// 仅负责加载官方安装目录的客户端库与内置脚本，不自动安装驱动。
/// 官方下载：https://pawnio.eu/
/// </summary>
public class PawnIO : IDisposable
{
    private const string DllName = "PawnIOLib.dll";
    private const string ScriptBlobName = "RyzenSMU.bin";
    private const string Amd17BlobName = "AMDFamily17.bin";
    private const string PawnIOUrl = "https://pawnio.eu/";

    private readonly object _initLock = new();
    private readonly object _executeLock = new();
    private IntPtr _dllHandle = IntPtr.Zero;
    private IntPtr _executorHandle = IntPtr.Zero;
    private IntPtr _amd17ExecutorHandle = IntPtr.Zero;
    private bool _disposed;

    public bool IsInitialized { get; private set; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenDelegate(out IntPtr handle);
    private OpenDelegate pawnio_open = null!;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LoadDelegate(IntPtr handle, byte[] blob, UIntPtr size);
    private LoadDelegate pawnio_load = null!;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ExecuteDelegate(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        [In] ulong[] input,
        UIntPtr inSize,
        [Out] ulong[] output,
        UIntPtr outSize,
        out UIntPtr returnSize
    );
    private ExecuteDelegate pawnio_execute = null!;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloseDelegate(IntPtr handle);
    private CloseDelegate pawnio_close = null!;

    /// <summary>
    /// 构造时不做任何加载操作，避免在应用启动阶段因驱动问题导致整个程序无法打开（白屏/闪退）。
    /// 驱动连接在第一次 Execute 时惰性初始化。
    /// </summary>
    protected PawnIO()
    {
    }

    protected ulong[] Execute(string functionName, ulong[] inputs, int expectedOutputCount)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PawnIO));

        EnsureInitialized();
        ulong[] outputs = new ulong[expectedOutputCount];
        lock (_executeLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PawnIO));

            int result = pawnio_execute(
                _executorHandle,
                functionName,
                inputs,
                (UIntPtr)inputs.Length,
                outputs,
                (UIntPtr)outputs.Length,
                out var returned
            );

            if (result != 0)
                throw new Exception($"Execute {functionName} failed, ErrorCode: 0x{result:X}");
            if (returned.ToUInt64() != (ulong)expectedOutputCount)
                throw new Exception($"Execute {functionName} 返回长度异常");
        }

        return outputs;
    }

    /// <summary>
    /// 读取指定 MSR 寄存器。由 AMDFamily17.bin 模块提供 ioctl_read_msr。
    /// </summary>
    protected ulong ReadMsr(uint msrIndex)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PawnIO));

        lock (_initLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PawnIO));

            EnsureDriver();

            if (_amd17ExecutorHandle == IntPtr.Zero)
            {
                byte[] blobData = PawnIOSetup.ReadResource(Amd17BlobName);
                if (pawnio_open(out var pendingHandle) != 0)
                    throw new Exception("未检测到 PawnIO 驱动服务。请从 https://pawnio.eu/ 下载安装 PawnIO 后重启应用。");

                try
                {
                    if (pawnio_load(pendingHandle, blobData, (UIntPtr)blobData.Length) != 0)
                        throw new Exception("加载 AMDFamily17 脚本失败");
                    _amd17ExecutorHandle = pendingHandle;
                }
                catch { pawnio_close(pendingHandle); throw; }
            }
        }

        lock (_executeLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PawnIO));

            ulong[] outputs = new ulong[1];
            int result = pawnio_execute(
                _amd17ExecutorHandle,
                "ioctl_read_msr",
                new ulong[] { msrIndex },
                (UIntPtr)1,
                outputs,
                (UIntPtr)1,
                out var returned
            );

            if (result != 0)
                throw new Exception($"Execute ioctl_read_msr failed, ErrorCode: 0x{result:X}");
            if (returned.ToUInt64() != 1) throw new Exception("MSR 返回长度异常");

            return outputs[0];
        }
    }

    private void EnsureInitialized()
    {
        if (IsInitialized)
            return;

        lock (_initLock)
        {
            if (IsInitialized)
                return;
            if (_disposed)
                throw new ObjectDisposedException(nameof(PawnIO));

            try
            {
                InitCore();
                IsInitialized = true;
            }
            catch
            {
                // A telemetry reader may already be waiting on _executeLock. Do not
                // unload its DLL/AMDFamily17 handle when only RyzenSMU loading failed.
                lock (_executeLock)
                {
                    if (_executorHandle != IntPtr.Zero)
                    {
                        try { pawnio_close(_executorHandle); } catch { }
                        _executorHandle = IntPtr.Zero;
                    }
                }
                throw;
            }
        }
    }
    
    private void EnsureDriver()
    {
        if (_executorHandle != IntPtr.Zero)
            return;

        // 内核驱动由用户从官网（https://pawnio.eu/）安装，本软件只连接系统已运行的 PawnIO 服务

        // Only HKLM's official installation location and Program Files; never cwd/PATH.
        if (_dllHandle == IntPtr.Zero) _dllHandle = LoadPawnIOLib(out _);

        if (_dllHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Exception($"未找到 {DllName}（ErrorCode: {err}）。请从 {PawnIOUrl} 下载安装 PawnIO 后重启应用。");
        }

        pawnio_open = Marshal.GetDelegateForFunctionPointer<OpenDelegate>(NativeLibrary.GetExport(_dllHandle, "pawnio_open"));
        pawnio_load = Marshal.GetDelegateForFunctionPointer<LoadDelegate>(NativeLibrary.GetExport(_dllHandle, "pawnio_load"));
        pawnio_execute = Marshal.GetDelegateForFunctionPointer<ExecuteDelegate>(NativeLibrary.GetExport(_dllHandle, "pawnio_execute"));
        pawnio_close = Marshal.GetDelegateForFunctionPointer<CloseDelegate>(NativeLibrary.GetExport(_dllHandle, "pawnio_close"));

        if (pawnio_open(out _executorHandle) != 0)
        {
            throw new Exception($"未检测到 PawnIO 驱动服务。请从 {PawnIOUrl} 下载安装 PawnIO 后重启应用。");
        }
    }

    private void InitCore()
    {
        EnsureDriver();

        byte[] blobData = PawnIOSetup.ReadResource(ScriptBlobName);
        if (pawnio_load(_executorHandle, blobData, (UIntPtr)blobData.Length) != 0)
            throw new Exception("加载 RyzenSMU 脚本失败");
    }

    /// <summary>
    /// 按官方用例（https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules）定位并加载 PawnIOLib.dll。
    /// 候选顺序：注册表 InstallLocation → Program Files\PawnIO。导出从实际加载句柄解析。
    /// </summary>
    /// <param name="loadedFrom">实际加载成功的路径（失败时为空）。</param>
    private IntPtr LoadPawnIOLib(out string loadedFrom)
    {
        List<string> candidates = new();

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            string? installLocation = key?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                installLocation = installLocation.Trim().Trim('"');
                if (installLocation.Length > 0)
                    candidates.Add(Path.Combine(installLocation, DllName));
            }
        }
        catch
        {
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PawnIO",
            DllName));

        foreach (string path in candidates)
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                continue;

            IntPtr handle = Kernel32.LoadLibrary(path);
            if (handle != IntPtr.Zero)
            {
                loadedFrom = path;
                return handle;
            }
        }

        // Never search the working directory or PATH for a privileged client library.
        loadedFrom = string.Empty;
        return IntPtr.Zero;
    }

    private void CleanupHandles()
    {
        if (_amd17ExecutorHandle != IntPtr.Zero)
        {
            try
            {
                pawnio_close(_amd17ExecutorHandle);
            }
            catch
            {
            }

            _amd17ExecutorHandle = IntPtr.Zero;
        }

        if (_executorHandle != IntPtr.Zero)
        {
            try
            {
                pawnio_close(_executorHandle);
            }
            catch
            {
            }

            _executorHandle = IntPtr.Zero;
        }

        if (_dllHandle != IntPtr.Zero)
        {
            Kernel32.FreeLibrary(_dllHandle);
            _dllHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        // 先取 _initLock 再取 _executeLock（与 Execute 路径锁序一致，无死锁）：
        // 确保与进行中的首次初始化互斥
        lock (_initLock)
        {
            lock (_executeLock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                CleanupHandles();
            }
        }

        // 不再管理任何内核服务（驱动由官网安装包负责），无需卸载

        IsInitialized = false;
        GC.SuppressFinalize(this);
    }

    ~PawnIO() => Dispose();
}
