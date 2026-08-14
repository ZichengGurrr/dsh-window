# dsh-window（插件）

DSH 插件：安装并启动 [dsh-window](https://github.com/ZichengGurrr/dsh-window) 桌面应用——DeepSeek Harness 的 Windows 原生窗口（WebView2）。

- 插件激活时自动确保桌面应用就绪：缺失或版本落后时从 GitHub Releases 下载精简 zip 到 `%LOCALAPPDATA%\Programs\dsh-window\`，并在桌面创建/刷新**不带版本号**的快捷方式「DeepSeek Harness Window」；已是最新则跳过下载
- 注册 `desktop_launch` 工具：对话里说"打开桌面应用"即可安装并拉起，agent 可直接调用
- 安装/下载失败不影响 DSH 启动，错误只记录日志，下次激活或调用工具时重试
- 下载走精简版（约 256KB）；便携版（自带 Node/DSH/Git）请从 Releases 手动下载

## 安装

```sh
dsh plugin --profile web add dsh-window
# 或从 GitHub 源安装：
dsh plugin --profile web add github:ZichengGurrr/dsh-window#path:/plugin
```

重启 DSH（`dsh web`）后生效。要求 Windows + Node ^22.19 或 ≥ 24。

## 配置（cordis.patch.yml）

| 字段 | 默认 | 说明 |
|---|---|---|
| autoInstall | true | 激活时自动安装/升级 |
| createShortcut | true | 创建桌面快捷方式 |
| installDir | %LOCALAPPDATA%\Programs\dsh-window | 应用安装目录 |
| shortcutName | DeepSeek Harness Window | 快捷方式名称（无版本号） |
| repoSlug | ZichengGurrr/dsh-window | 应用 zip 的 GitHub Release 来源 |

# English

DSH plugin that installs and launches [dsh-window](https://github.com/ZichengGurrr/dsh-window) — the native Windows (WebView2) window for DeepSeek Harness.

- On activation it ensures the desktop app is ready: downloads the slim zip from GitHub Releases into `%LOCALAPPDATA%\Programs\dsh-window` when missing or outdated and creates/refreshes a version-less desktop shortcut "DeepSeek Harness Window"; up-to-date installs are left alone
- Registers the `desktop_launch` tool — say "open the desktop app" in chat and the agent installs/launches it
- A failed install never blocks DSH startup; errors are logged and retried on next activation or tool call
- Uses the slim build (~256KB); the portable bundle (Node/DSH/Git included) stays a manual download from Releases

## Install

```sh
dsh plugin --profile web add dsh-window
# or from the GitHub source:
dsh plugin --profile web add github:ZichengGurrr/dsh-window#path:/plugin
```

Restart DSH (`dsh web`) to activate. Requires Windows + Node ^22.19 or ≥ 24.

## Configuration

See the table above; all keys are optional with those defaults.
