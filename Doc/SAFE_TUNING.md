# 安全调参修复分支

基于上游 10.13.25。本分支修复可从代码确认的缺陷，**不据此认定某次蓝屏一定由本软件造成**。一次游戏正常也不等于 CO 或 GPU 曲线已通过稳定性验证。

## 修复内容

- GPU 不再用“写入 +100 MHz，再写回 0”的方式探测能力；读取和预览不写 GPU。读取失败不猜测范围，不将私有功耗百分比当瓦数。
- GPU 锁频增加 `ClockLockEnabled`：旧配置缺省关闭，显式应用后启用；重置先关闭启动恢复，再分别请求解除核心/显存锁频并报告结果。
- CPU / SMU 页面使用本地输入副本，失败值不会被其他配置保存顺带保存。CPU 多步骤操作仍不是原子事务：中途失败时前面成功的步骤可能生效，界面明确提示。
- CPU 基本功耗/温度走原有 OEM 接口，最大频率/睿频走 Windows 电源计划；CO 需单独勾选。OEM 控制仍依赖机型支持，不等同于“任何电脑都无需驱动”。
- SMU 对完整 mailbox 收发序列加进程内锁；拒绝越界、非有限功耗、0 限值和未知 CPU 家族。仅在明确返回未知指令时尝试备用指令，繁忙/超时后不继续试写。锁不能协调其他调参程序，请勿同时运行。
- 固定频率、直接电压、单核 CO 的编号/CCD 映射未经验证，禁用写入。全核 CO -30～0 仅是输入保护，不是推荐值或稳定性保证；使用 CO 不需启用固定超频模式。
- 不再将 CPU 核心功耗（W）冒充 TDC（A），或用 TDC×1.3 伪造 EDC；缺失电流显示“未提供”。SMU 的 0 是未填写输入，不是硬件实际值或自动模式（CO 的 0 表示无曲线偏移）。
- PawnIO 模块内置；仅从官方安装位置加载客户端库，函数绑定实际 DLL 句柄，取消当前目录/PATH 回退；原始寄存器 API 不对 COM 页面公开。SMU 初始化失败不卸载并发电压读取仍可能使用的 DLL/句柄。

## GPU 曲线与恢复

保存工作、停止游戏、关闭其他调参程序并解除既有锁频。点击“读取曲线”，选择驱动原有电压点并填写目标频率，再生成预览。低于锚点的曲线不变，锚点及右侧成为目标频率平台。低电压侧已高于目标、偏移越界、数据布局异常或范围不可读时拒绝应用。不预填通用 4060 降压参数。

勾选风险确认并点击“试用 60 秒”才写入。写入前将原始偏移、设备和驱动标识保存到 `%LOCALAPPDATA%/JiaoLongControl/curve-backup-v1.json` 并刷新到磁盘。逐点写入后完整读回必须匹配；部分失败会尝试恢复原偏移，包括失败调用可能已经修改的点。恢复失败保留备份并报错，不做假成功。

计时器在 C# 后端，离开页面不会取消。确认后仅保留本次进程会话，正常退出仍尝试恢复，不随开机应用。**不是硬电压上限，也不是黑屏救援保证**：驱动挂起、系统死机或进程被强杀时计时器可能无法工作。

下次启动发现备份不会自动覆盖硬件，须用户明确选择恢复。设备、驱动或点布局改变时拒绝套用旧备份。若驱动更新后无法恢复，请使用显卡工具/驱动的默认设置恢复途径，勿篡改备份绕过检查。不要同时与 MSI Afterburner 等工具写曲线。

### 兼容性界限

safe.2 修正曲线 v1 的 256-bit 请求掩码、核心/显存域边界及 255 槽控制表布局。状态条目位于 0x40、步长 28；控制条目位于 0x40、步长 36，频率偏移在 +24。请求掩码从驱动读取，写入采用完整读改写，不再使用猜测的零填充表。参考 `simple-nvidia-undervolt` 并在 RTX 4060 Laptop / 616.64 上独立验证：900 mV 的单个核心点从 2250 降到 2235 MHz，再恢复原始控制表、偏移及显存/电压设置。详见 [测试记录](CURVE_COMPATIBILITY_4060.md)。

这仅验证单点协议与恢复，**不等于整条曲线、降压收益或游戏稳定性通过**；没有测试驱动挂起或蓝屏恢复。仅开放单 NVIDIA GPU 实验路径；驱动拒绝、静默忽略或取整导致读回不匹配均为失败。偏移单位仅在驱动报告 −1000～+1000 MHz 的已验证范围签名下允许写入。

备份内容版本升级为 2（255 个偏移），保留原文件路径以发现历史备份。版本 1 或 128 槽的旧备份不能按新协议恢复，会被拒绝并保留，不猜测转换、不自动清除。

## PawnIO：内置安装器，仍需要驱动

用户态程序不能仅靠内置 C# 代码取得 SMU/MSR 内核权限。内置的是未修改的官方签名安装器 **2.2.0** 和所需模块；安装页只读检测状态，明确勾选后才打开交互式安装器和管理员确认。不自动安装、静默重启、关闭签名检查或降级更高版本。

SHA-256：`1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`；运行前再次校验。**请选择官方签名版，不选 Unrestricted**。安装后刷新状态并重新打开本应用；“安装器已打开”不代表安装成功。不建议反作弊运行时安装或调参，不提供反作弊规避。

开发期间核对的用户安装文件与该官方包哈希相同，文件/产品版本均为 `2.2.0.0`，签名有效；未验证到该文件为 3.1.0 的证据。

## 构建、测试、回滚

实验性 ZIP 的首次部署见 [运行前必读](RELEASE_FIRST_RUN.md)。从新目录运行，不迁移旧配置；本 fork 已关闭上游自动安装更新。ZIP 内含 .NET 8 Windows x64 运行时，但仍需 WebView2 Runtime，主程序未经代码签名。

需要 Windows x64、.NET 8 SDK、支持当前依赖的 Node.js。从 Client 目录运行 `npm ci`、`npm test`、`npm run build`。从仓库根目录运行：

```text
dotnet build JiaoLongControl/JiaoLongControl.csproj -c Release -p:Platform=x64
dotnet run --project tests/SafeTuning.Tests -c Release
```

准备前端依赖后，在仓库根目录用 PowerShell 运行 `scripts/build-safe-release.ps1`。脚本重跑前后端测试、类型检查和生产构建，发布 self-contained Windows x64 包，检查必需文件/网页资源/个人配置排除，并生成 ZIP 与 `SHA256SUMS.txt`。可通过 `-DotNet` 指定 SDK 可执行文件；为避免误覆盖，已存在的版本输出目录会直接拒绝打包。

后端测试仅使用纯逻辑、模拟 GPU、读取 CPU 型号和必定拒绝的 SMU 输入；不启动 WPF 应用、不加载 PawnIO、不调频、不运行安装器。覆盖部分失败、静默拒绝、回滚失败、完整读回、边界、迁移、资源完整性和托管缓冲区布局；布局测试不是驱动 ABI 实测。

UI 冒烟测试：在 Client 目录执行 `npm run dev -- --host 127.0.0.1 --port 5179 --strictPort --mode safe-preview`，另一个终端从根目录运行 `node tests/ui-smoke.cjs`（需可解析的 Playwright 包及本机 Edge）。脚本在临时无头浏览器验证实际 Vue/Arco、确认门槛、恢复按钮及 1024/560px 布局；截图在被 Git 忽略的 `bin/`。开发预览全为模拟数据，不进入正常生产入口，并拒绝在真实 WebView 硬件宿主运行。

开发验证不改动已安装的软件或配置。源代码用 `git revert` 回退提交；若自行测试过硬件写入，**先恢复偏移，再退出/替换程序**，只回退代码不会恢复硬件。

本次未审计风扇、显示模式、DB 解锁等全部上游功能，不能将整个程序视为已完成安全认证。

## 一手参考

- [MSI 曲线编辑指南](https://www.msi.com/blog/msi-afterburner-overclocking-undervolting-guide)：参考操作思路，不复制软件或套用参数。
- [LACT #936](https://github.com/ilya-zlobintsev/LACT/issues/936)：结构和单点掩码参考，不是本机兼容认证。
- [simple-nvidia-undervolt 协议说明](https://github.com/vuplea/simple-nvidia-undervolt/blob/beace06d9b17d0e614d0925360f84456d1320d2a/DEVELOPMENT.md)：safe.2 的原始缓冲区、域边界和频率偏移位置交叉验证依据，保留独立实现。
- [NVIDIA-smi 文档](https://docs.nvidia.com/deploy/nvidia-smi/index.html)：锁频及解除锁频。
- [PawnIO 官网](https://pawnio.eu/)、[模块集成说明](https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules)、[安装器发布页](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)。
- [Microsoft 用户态与内核态说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/gettingstarted/user-mode-and-kernel-mode)。
