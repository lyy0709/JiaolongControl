# 10.13.25-safe.1 — 实验性安全调参修复

基于 GaoXanSheng/JiaolongControl 10.13.25 的独立修复分支，**不是稳定性认证或黑屏重启已修复的承诺**。

## 下载与部署

- 下载下方 `JiaoLongControl-10.13.25-safe.1-win-x64.zip`，不是 GitHub 自动生成的 Source code 压缩包；校验值见 `SHA256SUMS.txt`。
- Windows x64 解压即用，内含 .NET 8；仍需要 Microsoft Edge WebView2 Runtime。
- 先从托盘退出旧版，将整个压缩包解压到新目录。不要覆盖旧版或复制旧配置，不要先开机自动调参。
- 先阅读包内 `READ-ME-FIRST.md`，再运行 `JiaoLongControl.exe`。主程序未签名并按上游设计要求管理员权限，请核对来源，不要关闭系统安全功能。

## 主要变化

- GPU 曲线读取和预览、显式确认后试用 60 秒、完整读回校验、原始偏移备份与恢复；不随开机自动套用曲线。
- 删除写入式 GPU 能力探测、虚构读取范围、错误功耗单位；解除锁频同时关闭其启动恢复标志。
- SMU mailbox 事务串行化、输入边界与 CPU 型号保护；失败参数不再自动保存。未验证的直接电压/固定超频/单核写入暂时禁用。
- 内置 PawnIO 模块和未修改的官方签名安装器 **2.2.0（文件版本 2.2.0.0）**。SMU / CO 仍需要驱动，安装必须用户确认，不自动安装或重启。
- 关闭上游自动安装更新，避免实验性修复被上游安装包替换。

## 验证与限制

后端 18 项回归、前端 16 项测试、Vue 类型检查、Windows x64 发布构建及模拟桥接 UI 冒烟通过。ZIP 文件完整性另行校验。构建仍有上游既有风格的警告和前端大分包警告。

**没有执行本机 GPU / CPU 调参、安装驱动或真实 WPF 应用启动。** 私有 NVAPI 曲线接口未完成 RTX 4060 Laptop 实机写入验证。参数边界不是推荐值；先只读检查，有“不支持/读取失败”就停止。不要同时用小飞机等工具改曲线，也不要同时修改 CPU CO 和 GPU 以免混淆不稳定原因。

60 秒计时器与正常退出恢复均不能保证在驱动挂起、系统崩溃或进程被强杀时生效。测试后回退旧版之前，应先在新版恢复 GPU 原偏移并确认成功；保留 `%LOCALAPPDATA%/JiaoLongControl/curve-backup-v1.json`，不要靠删除备份绕过检查。只更换 EXE 不能恢复硬件。

[详细说明与测试边界](https://github.com/lyy0709/JiaolongControl/blob/v10.13.25-safe.1/Doc/SAFE_TUNING.md) · [首次运行与恢复](https://github.com/lyy0709/JiaolongControl/blob/v10.13.25-safe.1/Doc/RELEASE_FIRST_RUN.md)
