# DeepSeek Harness Window（社区版）

一个约 1MB 的轻量级启动器：把 [DeepSeek Harness](https://github.com/deepseek-ai/DeepSeek-Harness) 的 Web UI 装进 **Windows 原生独立窗口**（WebView2，即 Edge 内核）。

- 没有浏览器标签栏、地址栏，任务栏上是自己的图标和窗口
- 不需要安装 Electron（对比 Electron 方案动辄 200MB+）
- 双击即用：自动检测/启动本地 DSH 服务，就绪后弹窗
- 关窗即停：服务若由本程序启动，关闭窗口会一并停止
- 窗口标题跟随当前会话；外部链接交给默认浏览器

> ⚠️ **非官方社区项目**，与 DeepSeek 公司无隶属关系。"DeepSeek" 商标归其所有者所有。

## 原理

DeepSeek Harness 的图形界面本质是一个本地网页（默认 `http://127.0.0.1:3080`）。官方启动器负责起服务、开浏览器；本程序复用同一套起服务逻辑（`dsh --profile web --host 127.0.0.1 --port 3080`），只把"打开浏览器标签页"这一步换成内嵌的 WebView2 窗口。

## 使用前提

- Windows 10/11（自带 Microsoft Edge WebView2 Runtime；未自带时窗口启动会弹提示安装）
- Node.js 安装在默认位置 `%LOCALAPPDATA%\Programs\nodejs`
- 已全局安装 Harness：`npm install -g @deepseek-ai/dsh`

## 安装（预编译）

1. 从 [Releases](../../releases) 下载 `DeepSeek-Harness-Window-v*-win-x64.zip`
2. 解压到任意目录（共 4 个文件：exe + 3 个 WebView2 运行库）
3. 双击 `DeepSeek Harness Window.exe`，或为其创建桌面快捷方式

## 命令行参数

| 参数 | 作用 |
| --- | --- |
| （无） | 确保服务在线，然后打开窗口 |
| `--check` | 只检查环境（node / dsh 是否存在），不启动任何东西 |
| `--no-window` | 只确保服务在线，不打开窗口 |

## 从源码构建

依赖：Windows + PowerShell + .NET Framework 4.x（自带 `csc.exe`）。构建脚本会自动下载 WebView2 SDK（NuGet）。

```powershell
.\build.ps1                     # 默认：v1.0.0，不带图标
.\build.ps1 -Version 1.1.0 -IconPath .\my-icon.ico
```

产物：`dist\`（可运行目录）和仓库根目录下的发布 zip。

> 图标是可选的。仓库内不带任何 DeepSeek 官方 logo 素材，请勿在公开分发中使用官方鲸鱼 logo，除非你确认有权使用。

## 常见问题

**和直接开浏览器有什么区别？**
服务完全一样，会话数据也完全共用（都存在 `~/.dsh`）。区别只在窗口形态：独立窗口、独立任务栏图标、关窗即停（服务由它启动时）。

**关掉窗口后还能用浏览器访问吗？**
如果服务本来就是你在外面起的（比如命令行 `dsh web`），关窗不影响；如果服务是这个程序起的，关窗会一起停掉。想保留服务就加 `--no-window` 启动，或直接用官方 CLI。

**为什么没有打包成单文件 exe？**
WebView2 需要 3 个运行库文件随 exe 分发（其中一个是原生 DLL，无法嵌入托管程序集）。4 个文件放一个文件夹即可，体感与单文件无异。

## Roadmap

- [ ] 便携版：内置 portable Node + `@deepseek-ai/dsh`，接收者无需安装 npm/Node
- [ ] 提交 [winget-pkgs](https://github.com/microsoft/winget-pkgs)，支持 `winget install` 一键安装
- [ ] 记住窗口大小/位置；多会话快速切换快捷键

## 相关链接

- 官方仓库：[deepseek-ai/DeepSeek-Harness](https://github.com/deepseek-ai/DeepSeek-Harness)（MIT，Discussions 开放）
- 官方 npm 包：[`@deepseek-ai/dsh`](https://www.npmjs.com/package/@deepseek-ai/dsh)
- 其他社区桌面壳（Electron 路线）：[salathleizhang/deepseek-harness-desktop](https://github.com/salathleizhang/deepseek-harness-desktop)、[ChisaAlter/Deepseek-Harness-Desktop](https://github.com/ChisaAlter/Deepseek-Harness-Desktop)

## License

MIT（见 [LICENSE](LICENSE)）
