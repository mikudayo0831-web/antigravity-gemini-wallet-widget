<p align="center">
  <img src="assets/mascot.png" alt="Gemini 额度猫娘挂件" width="200" />
</p>

<h1 align="center">Antigravity Gemini 额度猫娘挂件</h1>

<p align="center">
  专为 <strong>Google Antigravity</strong> 打造的 <strong>Gemini 模型剩余额度猫娘挂件</strong><br/>
  Windows 透明桌面悬浮挂件 + Antigravity 原生插件 / MCP App 二合一
</p>

---

## 功能亮点

- **打开 Antigravity 自动唤起 & 限定窗口显示**
  - 插件自包含桌面猫娘程序，打开 Antigravity 加载插件时自动唤起（静默防重复实例，不弹窗打扰）；
  - 默认**仅在 Antigravity 为当前活动窗口时显示**，切换到浏览器/游戏/桌面自动隐藏，切回 Antigravity 无感出现且不抢输入框焦点（随时可右键切回全局置顶）；
  - 可选开启「关闭 Antigravity 时自动退出桌面挂件」。
- **零额度消耗全自动同步真实 Gemini 剩余额度**
  - 基于官方 `agy` CLI 的 `statusLine` 与 `agy models` 静默触发（**不发送对话、不消耗任何模型额度**），自动读取 Windows 系统代理；
  - 启动后自动同步、后台定时同步（默认每 5 分钟，1–60 分钟可调）、Antigravity 对话完成后防抖自动同步、重置倒计时归零自动同步，也支持一键手动刷新或手动校准。
- **丰富的挂件交互体验**
  - 透明无边框 WPF 桌面猫娘，支持 Q 弹捏猫动画与可调音量合成音效；
  - 点击猫娘循环切换：**Gemini 剩余额度与渐变进度条 / 重置倒计时 → 加权随机「谷谷歌歌…」心情台词 → 今日 Antigravity 调用轮次统计**；
  - 支持主气泡窗口自由切换（5 小时额度 / 周额度 / 第三方模型额度）、拖动四边磁吸贴边、贴靠左边缘自动水平翻转猫娘（气泡文字不翻转）、缩放（60%–160%）、隐藏 `☰` 按钮（右键随时呼出菜单）、自定义加权台词库、系统托盘常驻。
- **Antigravity 原生 MCP App 面板 & Hooks 统计**
  - 支持在 Antigravity 右侧边栏（`aux_pane`）和全局导航栏（`global`）打开交互面板；
  - 通过 `hooks.json`（`SessionStart` / `PostInvocation`）自动记录每日调用轮次、会话数、活跃模型及 7 日活跃柱状图。

---

## 快速安装

### 方式一：下载 Release 成品包（推荐，免编译）

1. 前往本仓库 **Releases** 页面下载 `antigravity-gemini-wallet-widget-vX.X.X-win-x64.zip` 并解压；
2. 双击 **`一键安装到Antigravity插件.cmd`**（自动安装到 `%USERPROFILE%\.gemini\config\plugins\gemini-wallet-widget` 并配置 `agy statusLine`）；
3. 在 Antigravity 的 **Plugins Manager** 中启用 **`Gemini 额度猫娘挂件`**，重启 Antigravity 后桌面猫娘会自动唤起。

> 运行环境要求：Windows 10/11、Node.js 18+、[.NET Desktop Runtime 9/10](https://dotnet.microsoft.com/download/dotnet)（系统已安装 .NET 9 或 .NET 10 桌面运行时即可）。

### 方式二：从源码构建

```powershell
# 1. 安装 MCP 服务端与 UI 打包依赖
npm install --prefix server

# 2. 打包单文件 MCP App HTML 界面
npm run --prefix server build:ui

# 3. 编译 Windows 桌面悬浮猫娘挂件（需要 .NET 9+ SDK）
dotnet build desktop\GeminiWalletWidget.csproj -c Release -o desktop\dist

# 4. 安装到 Antigravity 插件目录
powershell -ExecutionPolicy Bypass -File scripts\install-plugin.ps1
```

---

## 接入官方 `agy` CLI 自动同步实时额度

Antigravity 桌面客户端目前没有向插件开放直接读取剩余额度百分比的 API，本挂件通过官方 `agy` CLI 的 `statusLine` 实现 100% 合规的实时额度同步：

1. 安装官方 Antigravity CLI（PowerShell）：
   ```powershell
   irm https://antigravity.google/cli/install.ps1 | iex
   ```
2. 新开 PowerShell 登录一次 `agy`（如使用本地代理，先设置代理环境变量）：
   ```powershell
   # 示例：若系统代理在 127.0.0.1:7890
   $env:HTTP_PROXY = "http://127.0.0.1:7890"
   $env:HTTPS_PROXY = "http://127.0.0.1:7890"
   agy
   ```
3. 登录完成后即可关闭终端，之后猫娘会在后台自动读取你的 Windows 系统代理并静默同步最新真实额度。

---

## 项目结构

```text
├── assets/                 # 猫娘透明主图、插件图标与合成 WAV 音效
├── desktop/                # Windows .NET WPF 透明桌面悬浮挂件源码
├── plugin/                 # Antigravity 插件清单 (plugin.json / mcp_config.json / hooks.json)
├── scripts/                # UI 构建、插件安装、Hooks 记录器、agy statusLine 桥接与静默同步脚本
├── server/                 # Antigravity MCP App 服务端 (Node.js stdio)
├── shared/                 # 共享状态/配置存储层 (~/.gemini/antigravity-gemini-wallet/)
└── ui/                     # MCP App 前端交互界面源码
```

## 致谢与说明

- 交互与视觉灵感源自 [DeepSeek-Balance-Whale-Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget) 及 [Codex-Whale-Public](https://github.com/Yang-huai406/Codex-Whale-Public)。
- 所有数据均来自 Antigravity 官方文档公开接口（`agy` `statusLine`、`hooks.json`、MCP Apps），不读取私有凭据、不探测未公开接口。
- 源码采用 [MIT License](LICENSE) 开源；如需替换自定义立绘，直接替换 `assets/mascot.png` 与 `assets/logo.png` 后重新打包即可。
