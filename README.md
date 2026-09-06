<h1 align="center">JiaoLongControl</h1>

> 本分支是 **10.13.25 的实验性安全修复 fork**，不是已验证稳定的正式发行版。
> 新增 GPU V/F 曲线读取/预览、限时试用与备份恢复，内置官方签名 PawnIO 安装器。
> **safe.2 已在 4060 Laptop / 616.64 验证单点 −15 MHz 写入及恢复；不代表整条降压曲线或游戏稳定性通过。**
> 详细变更、限制、构建与恢复方法见 [安全调参说明](Doc/SAFE_TUNING.md)。
> safe.1 的核心/显存曲线域和控制表条目布局存在兼容缺陷，见 [本机交叉测试与修复记录](Doc/CURVE_COMPATIBILITY_4060.md)。

<p align="center">
  <strong>蛟龙 16 PRO 笔记本硬件控制中心</strong><br>
  <em>基于 7945HX + RTX 4060 版本开发，理论兼容其他 16 PRO [2023] 版本</em>
</p>

<p align="center">
  <img src="Doc/Main.png" alt="主界面" width="800" />
</p>

<p align="center">
  <a href="https://qm.qq.com/q/4ase4LoAJi">
    <img src="https://img.shields.io/badge/QQ%20群-蛟龙工具箱问题反馈-EB1923?logo=tencentqq&logoColor=white" alt="QQ Group">
  </a>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET">
  <img src="https://img.shields.io/badge/Vue-3.5-4FC08D?logo=vuedotjs" alt="Vue">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
</p>

---

## 功能

### CPU
- **功率控制** — 短时功率 (SPL) / 长时功率 (SPP) 调节
- **温度墙** — 本分支限制为 60°C ~ 100°C
- **睿频开关** — 通过 `powercfg` 修改电源计划
- **最大频率限制** — 支持 AC / DC 分别设定
- **实时监控** — 温度、使用率、频率、电压

### GPU
- **显卡模式切换** — 混合输出 / 独显直连
- **核心频率锁定** — 锁定指定频率，支持范围检测
- **显存频率锁定** — 同上
- **V/F 曲线（实验性）** — 原生点选择、平台预览、60 秒试用、原偏移备份及恢复；不是硬电压锁
- **功耗限制** — 不把私有接口百分比误报为瓦数，相关旧接口禁用
- **解锁 DB** — 通过 NVPCF 驱动解锁 GPU 功率上限
- **实时监控** — 使用率、显存占用、核心/显存频率、温度、风扇转速

### Ryzen SMU（高级 CPU 调校）
- **功耗限制** — STAPM / Fast PPT / Slow PPT / PPT
- **电流限制** — VRM / TDC / EDC
- **温度限制** — MP1 / RSMU
- **协议保护** — 固定 OC 频率/电压、单核映射未经验证，暂时禁用；未知家族禁止回退写寄存器
- **Curve Optimizer** — 全核 -30～0 的输入保护（不保证稳定）；普通 CPU 设置不再强制附带 CO 写入
- 自动识别已列出的 CPU 家族（Dragon Range / FP7 / FP8 / Strix / FP6），未识别时禁止 SMU 写入

### 风扇
- **手动控制** — CPU / GPU 风扇独立调速
- **高级自动风扇** — 温度驱动的智能调速：
  - 交叉散热算法（CPU/GPU 温度互相影响）
  - 温度平滑滤波
  - 爬升/下降速率限制
  - 共享热管同步
- **风扇曲线编辑器** — 可视化编辑温度-转速曲线
- **开机自启恢复** — 启动时自动恢复风扇策略

### RGB 键盘
- 颜色自定义（R / G / B ）
- 亮度 4 级调节
- 固定色 / 关闭模式

### 环境光
- Logo 灯开关控制

---

## 使用说明

1. 从 [本 fork 的 Releases](https://github.com/lyy0709/JiaolongControl/releases) 下载标注为实验性预发布的 Windows x64 ZIP，核对 SHA-256。
2. 从托盘退出旧版，将整个 ZIP 解压到新目录，不覆盖旧版、不复制旧配置。
3. 先阅读包内 `READ-ME-FIRST.md`，再运行 `JiaoLongControl.exe`。包含 .NET 8，仍需 WebView2 Runtime。
4. 首次只读检查，不启用开机自动调参。本分支关闭上游自动更新；SMU / CO 仍需 PawnIO。

详见 [首次部署和恢复说明](Doc/RELEASE_FIRST_RUN.md)。

> **注意：** 修改硬件参数有一定风险，请确保理解各项设置的含义后再操作。使用前建议备份当前配置。

---

## 开发

```bash
# 前端开发（建议受支持的 Node.js 22.22.2+ 或 24.15+，锁定依赖含 jsdom 30）
cd JiaoLongControl/Client
npm ci
npm run dev

# 后端构建（需要 .NET 8 SDK）
dotnet build JiaoLongControl/JiaoLongControl.csproj

# 从仓库根目录以 PowerShell 打包（先在 Client 执行 npm ci）
# powershell -File scripts/build-safe-release.ps1
```

前端开发时 Vite dev server 运行在 `localhost:5173`，后端 WebView2 在开发模式下指向该地址。

---

## 许可证

[MIT](LICENSE.md) © 2025 GaoXanSheng

PawnIO 安装包和模块保留其原许可；安装包并非 MIT。见 [安装包说明](JiaoLongControl/Drivers/PawnIO/SETUP-NOTICE.md)。

---

<p align="center">
  <sub>使用风险自负 · 非官方工具 · 与机械革命/清华同方无关联</sub>
</p>
