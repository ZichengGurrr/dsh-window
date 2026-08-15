# dsh-window-kit（三件套全家桶）

DeepSeek Harness 一条命令装齐三件事：

| 组件 | 作用 | 来源 |
| --- | --- | --- |
| **桌面** `dsh-window` | Windows 原生独立窗口（WebView2），自动安装 exe、创建快捷方式、注册 `desktop_launch` 工具 | 本仓库 [plugin/](../plugin) |
| **眼睛** `dsh-plugin-deepeye` | 视觉插件：为纯文本模型外挂看图能力（vision_describe / vision_ocr / vision_ask / vision_layout / vision_clipboard），默认智谱 **GLM-4V-Flash** | [Favio8/dsh-plugin-deepeye](https://github.com/Favio8/dsh-plugin-deepeye) |
| **嘴巴** `@nn12138/dsh-voice` | 语音输入：输入框麦克风按钮（或 Ctrl+Space）说话 → 文本进输入框，纯输入、与 agent preset 解耦 | [3274375092/dsh-voice](https://github.com/3274375092/dsh-voice) |

## 安装（一条命令）

```sh
dsh plugin --profile web add github:ZichengGurrr/dsh-window#path:/kit
```

重启 `dsh web`（或刷新页面）后生效。

## 配置

### 1. GLM 视觉 API Key（必配）

在 `~/.dsh/.env` 里设置（智谱开放平台 https://open.bigmodel.cn 的 API Key）：

```
DEEPEYE_API_KEY=你的key
```

默认模型 `glm-4v-flash`。想换接口/模型，在 `~/.dsh/profiles/web/cordis.patch.yml` 里覆盖：

```yaml
- id: deepeye-vision
  config:
    baseUrl: https://open.bigmodel.cn/api/paas/v4
    model: glm-4v-flash
    pasteCompat: auto
    maxTokens: 1024
```

### 2. 语音输入（开箱即用）

默认走浏览器 Web Speech（Edge=Azure / Chrome=Google），零配置。想要**离线本地识别**（隐私、国内网络更稳）：

```sh
dsh plugin --profile web add sherpa-onnx-node
dsh-voice-models        # 下载模型 ~100MB
```

并在 profile 的 cordis.patch.yml 里给 voice 配置模型目录：

```yaml
- id: voice
  config:
    modelDir: './dsh-voice-models'
```

详见 [@nn12138/dsh-voice 文档](https://github.com/3274375092/dsh-voice)。

### 3. 桌面窗口（自动）

插件激活时自动从 GitHub Releases 安装应用并创建桌面快捷方式「DeepSeek Harness Window」，版本更新会自动升级；对话里说"打开桌面应用"即可拉起。详见 [plugin/README.md](../plugin/README.md)。

## 卸载

```sh
dsh plugin --profile web remove dsh-window-kit
```

## 说明

- 本套件是聚合 bundle：本身不含业务代码，只负责安装三个插件并写入默认配置。
- 各组件均为 MIT 协议，由各自作者维护；本套件与 DeepSeek 公司无隶属关系。
- npm 发布后安装命令可简化为 `dsh plugin --profile web add dsh-window-kit`。

## License

MIT（见仓库根 [LICENSE](../LICENSE)）
