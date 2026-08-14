# DeepSeek Harness Window（社区版）

把 [DeepSeek Harness](https://github.com/deepseek-ai/DeepSeek-Harness) 的 Web UI 装进 **Windows 原生独立窗口**（WebView2，即 Edge 内核）的极简启动器。

- 没有浏览器标签栏、地址栏；任务栏上是自己的图标和窗口
- 不需要 Electron；精简版整个应用约 1MB
- **便携版双击即用**：自带 Node.js + DSH + Git，无需安装任何环境
- 双击即用：自动检测/启动本地 DSH 服务，就绪后弹窗；关窗即停（服务由它启动时）
- 单实例：重复双击只聚焦已有窗口；记住窗口大小和位置；连不上服务时窗口内一键重试

> ⚠️ **非官方社区项目**，与 DeepSeek 公司无隶属关系。"DeepSeek" 商标归其所有者所有。

## 两种形态

| | 精简版 slim | 便携版 portable |
| --- | --- | --- |
| 大小 | ~256KB | ~180MB |
| 适合 | 开发者、已有 Node 环境的人 | **任何人**，解压双击即用 |
| 前提 | 已装 Node + `npm i -g @deepseek-ai/dsh` | 无 |
| 文件名 | `DeepSeek-Harness-Window-v*-win-x64.zip` | `...-portable-v*-win-x64.zip` |

## 原理

DeepSeek Harness 的图形界面本质是一个本地网页（默认 `http://127.0.0.1:3080`）。本程序负责起服务（`dsh --profile web --host 127.0.0.1 --port 3080`），并把"打开浏览器标签页"这一步换成内嵌的 WebView2 窗口。便携版把运行环境（Node、DSH 包、Git）放在程序目录的 `runtime\` 下，优先使用，找不到再回退到系统安装。

## 使用（便携版，零门槛）

1. 从 [Releases](../../releases) 下载 `DeepSeek-Harness-Window-portable-v*-win-x64.zip`
2. 解压到任意目录
3. 双击 `DeepSeek Harness Window.exe`

## 使用（精简版）

1. 下载 `DeepSeek-Harness-Window-v*-win-x64.zip` 并解压（4 个文件）
2. 前提：Windows 10/11、Node.js 装在默认位置 `%LOCALAPPDATA%\Programs\nodejs`、`npm install -g @deepseek-ai/dsh`
3. 双击 `DeepSeek Harness Window.exe`

## 可选配置

在 exe 同目录放一个 `dsh-window.config.json`（可省略，全部有默认值）：

```json
{
  "host": "127.0.0.1",
  "port": 3080,
  "waitTimeoutMs": 30000,
  "killServerOnClose": true,
  "permissionMode": "danger-full-access"
}
```

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `host` / `port` | `127.0.0.1` / `3080` | 服务地址；改端口即可与其它实例共存 |
| `waitTimeoutMs` | `30000` | 等待服务就绪的超时 |
| `killServerOnClose` | `true` | 服务由本程序启动时，关窗是否一并停止 |
| `permissionMode` | `danger-full-access` | 传给服务的 DSH 权限模式（也可在 Web UI 里改） |

## 命令行参数

| 参数 | 作用 |
| --- | --- |
| （无） | 确保服务在线，然后打开窗口 |
| `--check` | 只检查运行环境，不启动任何东西 |
| `--no-window` | 只确保服务在线，不打开窗口 |

## 从源码构建

依赖：Windows + PowerShell + .NET Framework 4.x（自带 `csc.exe`）。构建脚本自动下载 WebView2 SDK（NuGet）。

```powershell
.\build.ps1                          # 精简版
.\build.ps1 -Portable                # 精简版 + 便携版（自动下载 Node/MinGit、npm 安装 DSH）
.\build.ps1 -Version 1.1.0 -IconPath .\my-icon.ico
.\build.ps1 -Portable -DshVersion 0.1.0-rc.6 -NpmRegistry https://registry.npmmirror.com
```

产物：`dist\`（精简版目录）、`dist-portable\`（便携版目录）及仓库根目录下的 zip。

> 图标是可选的。仓库内不带任何 DeepSeek 官方 logo 素材，请勿在公开分发中使用官方鲸鱼 logo，除非你确认有权使用。

## 常见问题

**便携版为什么这么大？**
Node.js 运行时 + 完整 `@deepseek-ai/dsh` 包 + MinGit。这是"解压即用"的代价；不需要零安装体验就用精简版。

**和直接开浏览器有什么区别？**
服务完全一样，会话数据完全共用（`~/.dsh`）。区别只在窗口形态：独立窗口、独立任务栏图标、关窗即停。

**关掉窗口后还能用浏览器访问吗？**
如果服务本来就在外面跑着（比如命令行 `dsh web`），关窗不影响；服务是这个程序起的且 `killServerOnClose` 为 `true`，会一起停。

**为什么不是单文件 exe？**
WebView2 需要 3 个运行库文件随 exe 分发（含一个原生 DLL）。4 个文件放一个文件夹，体感与单文件无异。

## Roadmap

- [ ] 一键安装器（Inno Setup / winget 包）
- [ ] 自动检查新版本并提示更新
- [ ] 托盘常驻与最小化到托盘

## 相关链接

- 官方仓库：[deepseek-ai/DeepSeek-Harness](https://github.com/deepseek-ai/DeepSeek-Harness)（MIT，Discussions 开放）
- 官方 npm 包：[`@deepseek-ai/dsh`](https://www.npmjs.com/package/@deepseek-ai/dsh)
- 其他社区桌面壳（Electron 路线）：[salathleizhang/deepseek-harness-desktop](https://github.com/salathleizhang/deepseek-harness-desktop)、[ChisaAlter/Deepseek-Harness-Desktop](https://github.com/ChisaAlter/Deepseek-Harness-Desktop)

## License

MIT（见 [LICENSE](LICENSE)）
