# DogDesk（v0.7.0）

仓库：https://github.com/yyc-i/dogdesk  ·  显示名 `dogdesk`（内部标识 DogDesk）。

**任务栏媒体控制条 + WorkerW 动态壁纸 + libmpv 全格式视频 + 多源桌面歌词 + Folia 风格全屏歌词 + Mineradio 风格沉浸播放 + 桌面 AI 桌宠（本地 Ollama 双通道 LLM + 工具调用）+ 自定义开机动画 + 桌面整理 + 系统优化 + macOS 版** —— Windows 10/11 桌面美化与陪伴工具（macOS 版同步提供）。

> 注：v0.5.0 起由 MediaBarWallpaper 更名，旧 `%AppData%\MediaBarWallpaper` 配置（settings.json / deskbox.json）在首次启动时自动迁移到 `%AppData%\DogDesk\`，旧文件保留不删除。

形态参考开源项目 [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（任务栏媒体控制条），并集成动态壁纸能力。v0.5.0 起引入跨平台共享核心库（`DogDesk.Core`），Windows（WPF）与 macOS（Avalonia）双端共用同一套模型/设置/歌词/频谱/播放队列/音源/均衡器/环绕/歌单/电台/记忆播放/LLM 客户端逻辑。

## 功能（v0.7.0）

| 模块 | 说明 |
|---|---|
| 任务栏媒体条 | 无边框透明悬浮窗，吸附任务栏（靠左/居中/靠右，可拖进度条、滚轮调音量）；未播放自动淡出；封面主色生成玻璃感渐变背景 |
| SMTC 真实会话 | 读取真实曲目/封面/播放状态/时间线；无播放器时自动回退演示数据 |
| **内置播放内核** | 独立 libmpv 音频会话，驱动本地曲库/在线音源/Navidrome/电台/榜单；**会话路由**在内置与 SMTC 间透明切换 |
| 本地曲库 / 在线音源 / Navidrome / 均衡器 / 3D 环绕+音效预设 / 3D 歌单架 / 底部实时频谱 / 节拍视觉 / 电台+榜单 / 毛玻璃+视频背景 / 歌词增强 / 记忆播放 / 倍速+音质 / 自建歌单+下一首+收藏 / 歌单导入 | v0.5–v0.6 全部能力保留（网易云等非官方接口已标注"仅供学习、稳定性不保证"） |
| 全屏沉浸式歌词动画 / 动态配色 / 视频壁纸 / 多屏壁纸 / 图片轮播 / 动画壁纸 / 频谱律动 / 桌面歌词 | v0.4–v0.5 全部能力保留 |
| **桌面桌宠（v0.7，Windows）** | BongoCat 风格自研：桌面悬浮卡通猫，敲击键盘（A/空格等）实时拍爪动画；透明无边框 `WS_EX_TOOLWINDOW\|NOACTIVATE` 不抢焦点、可拖动；**输入捕捉仅本机读取键盘状态（GetAsyncKeyState），离线运行、不收集不上传**（UI 标注）；可整体开关，未启用零实例（懒加载） |
| **AI 桌宠（v0.7，Windows）** | 桌宠即 AI 角色：**点击桌宠**弹出对话气泡（文字 + TTS 语音朗读）；非对话时拍爪照常 |
| **LLM 双通道（v0.7）** | **本地优先**：Ollama（http://localhost:11434/v1，OpenAI 兼容、无 key、免费离线、数据不出本机；自动检测服务与已拉取模型，未安装给出 `ollama serve` + `ollama pull qwen2.5:7b` 指引）；**云端可选**：OpenAI 兼容 API（豆包/DeepSeek/OpenAI，用户自备 base_url+key）；统一 `IChatProvider` 抽象，通道/模型/人设可切换 |
| **工具调用（v0.7，Windows）** | function calling 白名单工具（**默认关闭**，设置页显式开启）：播放控制（上一首/播放暂停/下一首，接媒体条会话）、系统信息（CPU/内存/磁盘/电池，只读）、打开文件/文件夹/URL（白名单扩展名，可执行需确认弹窗）、截图保存（主屏 PNG 存 `%AppData%\DogDesk\screenshots\`）、定时提醒（Timer+弹窗）；全部含参数校验 + 日志「桌宠调用了 XX 工具」；**不提供删除/任意命令执行等危险工具** |
| **AI 角色人设** | 内置 5 种性格预设（元气少女/知性姐姐/冷淡助手/幽默伙伴/猫娘），system prompt 可自定义编辑；对话上下文保留最近 10 轮；TTS 用 Windows 系统语音（SAPI，离线）朗读回复 |
| **ZenDesktop 视觉效果（v0.7，Windows，实验性）** | 系统优化 Tab 新增「视觉效果」分区：Windhawk 液态玻璃 mod 的**一键应用/卸载脚本**（scripts/zen-desktop/apply.ps1、uninstall.ps1，提权执行）；🔴 红风险「修改 Explorer 进程，可能影响稳定性，建议先建还原点」；**默认不勾选、绝不自动注入、仅封装安装/卸载流程，不复制 Windhawk/ZenDesktop 任何 mod 代码**（GPL-3.0） |
| **自定义开机动画（v0.7）** | 启动时显示开机动画窗口（居中无边框，Esc/点击/超时自动关，默认开启可关）：**Aurora 极光 / Gradient 渐变呼吸 / FlowText 流光文字 / Image 图片** 四种内置样式；BootAnimDurationSec 可调 |
| 设置持久化 / 系统托盘 / 开机自启 | `%AppData%\DogDesk\settings.json`；托盘；启动文件夹快捷方式（非侵入） |
| 控制台 | 深色分页 UI（壁纸/媒体条/桌面歌词/频谱/曲库/在线音源/均衡器/电台榜单/歌单架/桌宠/AI 伙伴/开机动画/桌面整理/系统优化/设置），实时日志 |
| **macOS 版（v0.7）** | Avalonia 11：播放/曲库/在线音源(网易云+Navidrome+电台+榜单)/歌单(封面轮播)/均衡器(环绕+倍速+音质)/AI 伙伴(双通道 LLM 对话)/设置 七 Tab + 置顶桌面歌词 + 全屏流光歌词 + **开机动画（Aurora/Particle/FlowText/Image）** + 动态配色 + 记忆播放；libmpv 动态加载（`brew install mpv`） |

## AI 桌宠安全边界（务必阅读）

- **默认关闭**：`AiEnabled`、`AiToolsEnabled` 均默认关闭；首次开启 AI 伙伴弹隐私确认「对话内容发送到你自行配置的第三方 LLM 服务（或本地 Ollama），由你自行负责；本应用不收集、不上传额外数据」
- **本地优先**：默认通道为本地 Ollama（免费离线、数据不出本机）；云端通道需用户自备 key
- **工具白名单**：仅注册白名单工具（播放/系统信息/打开文件/截图/提醒），含参数校验；可执行文件、提醒等写操作需确认弹窗；**不提供删除、任意命令执行等危险工具**；所有工具调用写入日志
- **桌宠钩子**：仅本机读取键盘状态，离线运行、不收集数据、可一键关闭
- **Ollama 未安装**：设置页给出安装指引（官网 / `brew install ollama` + `ollama serve` + `ollama pull qwen2.5:7b`）；本环境未安装 Ollama，双通道代码路径已编译通过，**实际对话需本机 Ollama 或云端 key 实测**

## 系统优化模块的安全边界（务必阅读）

- **所有修改项默认全不勾选**，只有用户显式勾选并点击「执行勾选项」才生效；后台逻辑**绝不自动执行**
- 每项带**风险等级**：🟢 安全可逆 / 🟡 谨慎 / 🔴 不建议（如 ZenDesktop 视觉效果 = 🔴 修改 Explorer 进程）
- 执行前弹窗确认 + 检测管理员权限（不足时弹 UAC 提权）；逐项执行日志；「撤销勾选项」执行反向脚本
- UI 常驻提示：**系统修改有风险，建议先创建系统还原点，后果自负**；不 hook Explorer、不驻留后台修改
- ZenDesktop 仅封装 Windhawk mod 的安装/卸载脚本（GPL-3.0 只封装流程、不复制 mod 代码）

## 内存/性能优化（v0.7，Windows）

| 优化项 | 机制 |
|---|---|
| 空闲工作集压缩 | 45s 定时器判定真正空闲 → `EmptyWorkingSet` + `GC.Collect(2, Optimized)` |
| 频谱按需启停 / 底部频谱空闲跳绘 | 无活跃音频时 `Stop()` WASAPI 采集与 FFT；空闲跳过绘制 |
| 高帧率模式 | `HighFpsMode` 全局切换频谱/歌词/动画 tick 60fps / 30fps（默认 30fps） |
| 视频背景空闲暂停 / 封面友好解码 / 歌词 Run 复用 / 窗口懒创建 / mpv 句柄释放 | v0.5–v0.6 全部保留 |
| **桌宠/AI/开机动画懒加载（v0.7）** | 未启用零实例零定时器；开机动画用完即释放；桌宠空闲降低重绘频率 |

> 空闲内存目标 <60MB（后台驻留）。注：Linux 交叉编译环境无法实测 Windows 运行时内存，上表为机制与预期方向，最终数值以真机为准。

## macOS 版（Avalonia）

- 技术路线：**Avalonia 11**（官方 WPF 跨平台迁移框架，可在 Linux 交叉发布 osx-x64/osx-arm64）—— 来源：[Avalonia UI for macOS](https://avaloniaui.net/avalonia/macos/)
- 共享核心库 `DogDesk.Core`（net8.0，无 WPF 依赖）承载模型/设置/歌词/频谱/播放队列/音源/均衡器/环绕/歌单/电台/记忆播放/LLM 双通道逻辑
- **运行时依赖**：libmpv（`brew install mpv`）；Ollama（`brew install ollama`，AI 本地通道）
- **未签名/未公证**：Linux 环境无法签名与 notarization，首次打开需**右键 → 打开**放行 Gatekeeper
- macOS 版不含：壁纸（需 NSWorkspace）、系统频谱回采（无 WASAPI）、桌宠（Win32 钩子）、ZenDesktop（Windows-only）、TTS/STT（需原生语音桥接）

### macOS 构建（在 Linux 或 macOS 上）

```bash
dotnet build src/DogDesk.Mac/DogDesk.Mac.csproj -c Release
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-x64 --self-contained true -o artifacts-mac/dogdesk-publish-osx-x64
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-arm64 --self-contained true -o artifacts-mac/dogdesk-publish-osx-arm64
```

## 技术要点

- **跨平台共享核心库**：`src/DogDesk.Core/`（net8.0，无 WPF 依赖）：AppSettings、SettingsService、LyricsService、SpectrumAnalyzer、PlaybackQueue、NeteaseMusicSource、NavidromeClient、Equalizer、SurroundEffect/EffectPreset/EffectAf、BeatDetector、RadioService、ChartsCatalog、PlaylistStore、ResumeStore、**IChatProvider/LlmClient/OllamaClient（LLM 双通道）**、**ToolRegistry/ToolDescriptor（工具白名单）**、**CharacterPresets（角色人设）**、**PetStore（桌宠持久化）**、**BootAnimStore（开机动画素材库索引）**
- **LLM 双通道**：`IChatProvider` 统一抽象；`LlmClient`（云端 OpenAI 兼容 /chat/completions，Bearer key）；`OllamaClient`（本地 localhost:11434，无 key，`/api/tags` 检测与列模型）；`ChatCompletions.Parse` 统一解析文本与 tool_calls
- **工具调用**：请求携带 `tools`（function schema）→ 响应 `tool_calls` → `ToolRegistry.ValidateJson` + 确认弹窗 → 平台执行 → 结果回灌二次请求
- **内置播放内核**：独立 libmpv context（`vo=null`），事件线程观察 `time-pos/pause/duration/eof-reached`；与壁纸视频播放器各自独立
- **非侵入式**（壁纸/媒体条/歌词/桌面格子/桌宠）：不 hook Explorer、不改注册表；系统优化模块除外（用户显式触发）
- **壁纸宿主**：`Progman` → `0x052C` → 枚举含 `SHELLDLL_DefView` 的顶层窗口 → WorkerW（Win8+ 经典方案）
- **桌面格子**：每屏透明叠加窗 `SetParent` 到 WorkerW；格子为文件链接（不移动原位）
- **桌宠**：透明无边框置顶窗 + `GetAsyncKeyState` 轮询驱动拍爪动画（非全局钩子，无权限风险）；点击（拖动距离 <4px）弹对话气泡
- **开机动画**：App 启动时非模态展示（不阻塞主窗口初始化），Esc/点击/超时关闭，`BootAnimEnabled=false` 零创建
- **媒体条定位**：`FindWindow("Shell_TrayWnd")` → `ComputeAnchor` → `SetWindowPos`
- **libmpv 集成**：Windows 用 NuGet `Endpne.LibMPV.Windows`；macOS 动态加载系统 `libmpv.dylib`
- **歌词**：`Lyricify.Lyrics.Helper` 多源检索 + 逐字音节级高亮

## 系统要求与构建（Windows）

- 系统：**Windows 10 1809+ / Windows 11**（x64）；SDK：.NET 8 SDK（或 VS 2022 17.8+）
- 首次构建会经 NuGet 自动下载 libmpv（约 80MB）与 System.Speech（TTS），需联网

```powershell
cd DogDesk
dotnet build src/DogDesk/DogDesk.csproj -c Release
```

> 代码已在 Linux 通过 `EnableWindowsTargeting` 交叉编译验证（0 警告 0 错误），WPF 程序只能在 Windows 运行。

## 运行（Windows）

1. 运行 `DogDesk.exe`；首次可勾选「设置 → 开机自启 / 最小化到托盘」；启动动画默认显示（Esc 跳过）
2. 「壁纸」「频谱」「媒体条」「桌面歌词」「曲库」「在线音源」「均衡器」「电台/榜单」「歌单架」「桌面整理」「系统优化」各页用法同 v0.5–v0.6
3. **「桌宠」页**：勾选启用 → 桌面出现卡通猫；敲键盘看拍爪；**点击猫弹出 AI 对话气泡**
4. **「AI 伙伴」页**：默认本地通道——点「检测 Ollama」，未安装按提示装好并 `ollama pull qwen2.5:7b` 后刷新模型下拉；或切「云端」填 base_url/key/model（默认 doubao-seed-1.6-lite）；选人设、开 TTS/工具开关 → 对话
5. **「开机动画」页**：选样式（Aurora/Gradient/FlowText/Image）、调时长、「预览」；导入图片做 Image 素材
6. **「系统优化」→ ③ 视觉效果（实验性）**：🔴 红风险，阅读警告后按需「应用」/「卸载」（提权）

## 已知限制

- **v0.7 降级项（诚实）**：桌宠当前单实例、位置不持久化（多实例/帧序列导入未做）；开机动画仅 4 种代码样式 + Image 静态图（视频样式/素材库列表/AI 生成素材未做）；STT「按住说话」未接线（缺识别程序集与语言包）；截图仅主屏；TTS 依赖 Windows 语音包（缺包时降级日志提示）；Ollama 需本机安装（未实测端到端）
- **垂直任务栏**：歌词/壁纸已做基础兜底，未深度适配
- **频谱精度**：loopback 为系统输出混音，非单应用音频
- **libmpv 体积**：全功能静态构建 dll 较大（~117MB）
- **非商业签名**：SmartScreen 可能提示「未知发布者」；macOS 版未签名需右键打开
- **在线音源依赖网络**：网易云/QQ音乐/LRCLIB 接口可能变更（非官方接口，已标注）；LLM 云端通道依赖用户 key 与网络
- **系统优化命令**：未在每台机器逐一验证，执行前务必先建还原点；ZenDesktop 修改 Explorer 进程，风险更高
- **macOS 版**：需 `brew install mpv`（及 Ollama 用于 AI 本地通道）；未在真机实测（Linux 交叉发布，仅保证编译/打包正确）

## 目录结构

```
DogDesk/
├── src/
│   ├── DogDesk.Core/      # 跨平台共享核心库（net8.0，无 WPF 依赖）
│   │   ├── Models/                  # AppSettings / MediaItem / PetInfo / AudioQuality
│   │   └── Services/
│   │       ├── Ai/                  # IChatProvider / LlmClient(云端) / OllamaClient(本地) /
│   │       │                        #   ChatCompletions / ToolRegistry / CharacterPresets / ImageGenClient
│   │       ├── Pets/                # PetStore（桌宠持久化 JSON+备份）
│   │       ├── BootAnim/            # BootAnimStore（开机动画素材库索引）
│   │       ├── Effect/ Playback/ MusicSource/ Playlists/ Settings/ Lyrics/ SpectrumAnalyzer/ Equalizer ...
│   ├── DogDesk/           # Windows WPF 版
│   │   ├── App.xaml(.cs)            # 入口 + 开机动画展示 + 单例 + 会话路由
│   │   ├── MainWindow.xaml(.cs)     # 控制面板（15 Tab）+ MainWindow.v06.cs / MainWindow.v07.cs
│   │   ├── Views/                   # 媒体条/壁纸/频谱/歌词/全屏歌词/歌单架/粒子/
│   │   │                            #   PetWindow(桌宠) / PetChatWindow(点击对话) / BootAnimWindow(开机动画)
│   │   ├── Services/                # MpvAudioPlayer / MediaSessionRouter / DeskBox / Optimizer / Mpv / ...
│   │   └── Interop/                 # Win32 P/Invoke / 任务栏 / WorkerW
│   └── DogDesk.Mac/       # macOS Avalonia 版（net8.0 + Avalonia 11）
│       ├── Services/                # MpvLib / MpvAudioPlayer / AppServices(含双通道 LLM) / 曲库 / 封面
│       └── Views/                   # MainWindow(7 Tab) / BootAnimWindow / LyricsOverlay / FullscreenLyrics
├── scripts/zen-desktop/   # Windhawk 液态玻璃 mod 安装/卸载封装脚本（GPL-3.0 仅封装流程）
├── DogDesk.sln
└── README.md
```

## 合规与致谢（许可证声明）

- **Folia**（[chthollyphile/folia-major](https://github.com/chthollyphile/folia-major)，AGPL-3.0）：仅**功能级自研复刻**，未复制任何代码
- **DeskBox**（[Tianyu199509/DeskBox](https://github.com/Tianyu199509/DeskBox)，GPL-3.0）：仅参考功能，**功能级自研复刻**
- **Winhance**（[memstechtips/Winhance](https://github.com/memstechtips/Winhance)，PolyForm Shield 1.0.0）：仅参考功能分区，自研实现
- **Win11Debloat**（[Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat)，MIT）：参考批量卸载与撤销还原思路，自研实现
- **Mineradio**（[XxHuberrr/Mineradio](https://github.com/XxHuberrr/Mineradio)，GPL）：仅参考功能形态，**功能级自研复刻**（mpv af 链/Viewport3D/频谱节拍检测）
- **BongoCat**（[ayangweb/BongoCat](https://github.com/ayangweb/BongoCat)，MIT；[vladelaina/BongoCat](https://github.com/vladelaina/BongoCat) 重构版）：仅参考"键盘驱动桌宠拍打"交互形态，**自绘卡通猫 + GetAsyncKeyState 自研实现，未复制代码/素材**
- **Open-LLM-VTuber**（[Open-LLM-VTuber/Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber)）：仅参考"AI 角色 + LLM/TTS/STT"产品形态，**功能级简化自研**（LLM 双通道 + 工具调用 + SAPI TTS），未复制代码
- **ZenDesktop**（[Liset999/ZenDesktop](https://github.com/Liset999/ZenDesktop)，Windhawk mod）：仅提供**安装/卸载封装脚本**（GPL-3.0 只封装流程，不复制其 mod 代码）
- **Ollama**（[ollama/ollama](https://github.com/ollama/ollama)，MIT）：本地 LLM 通道运行时依赖，用户自行安装
- AF Media Bar（MIT）、Lively Wallpaper（GPL-3.0，仅参考思路）、libmpv（LGPL-2.1，动态链接）、Lyricify Lyrics Helper（见其仓库协议）、Avalonia（MIT）
- 网易云在线音源/榜单/歌单导入为非官方第三方接口，仅供学习，稳定性不保证；Navidrome 凭据与 AI key 明文存本机 settings.json，注意保管

## License

MIT。
