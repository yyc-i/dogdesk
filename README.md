# DogDesk（v0.6.0）

仓库：https://github.com/yyc-i/dogdesk  ·  显示名 `dogdesk`（内部标识 DogDesk）。

**任务栏媒体控制条 + WorkerW 动态壁纸 + libmpv 全格式视频 + 多源桌面歌词 + Folia 风格全屏歌词动画 + Mineradio 风格沉浸播放（3D 环绕 / 歌单架 / 电台榜单 / 节拍视觉 / 记忆播放）+ 内置播放内核 + 桌面整理 + 系统优化 + macOS 版** —— Windows 10/11 桌面美化工具（macOS 版同步提供）。

> 注：v0.5.0 起由 MediaBarWallpaper 更名，旧 `%AppData%\MediaBarWallpaper` 配置（settings.json / deskbox.json）在首次启动时自动迁移到 `%AppData%\DogDesk\`，旧文件保留不删除。

形态参考开源项目 [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（任务栏媒体控制条），并集成动态壁纸能力。v0.5.0 起引入跨平台共享核心库（`DogDesk.Core`），Windows（WPF）与 macOS（Avalonia）双端共用同一套模型/设置/歌词/频谱/播放队列/音源/均衡器/环绕/歌单/电台/记忆播放逻辑。

## 功能（v0.6.0）

| 模块 | 说明 |
|---|---|
| 任务栏媒体条 | 无边框透明悬浮窗，`Shell_TrayWnd` 定位 + `SetWindowPos` 吸附任务栏（靠左/居中/靠右，可拖进度条、滚轮调系统音量）；未播放自动淡出；封面主色生成玻璃感渐变背景 |
| SMTC 真实会话 | `GlobalSystemMediaTransportControlsSessionManager` 读取真实曲目/封面/播放状态/时间线；无播放器时自动回退演示数据 |
| **内置播放内核** | 独立 libmpv 音频会话（`vo=null` 纯音频），驱动本地曲库/在线音源/Navidrome/电台/榜单播放；**会话路由**在「内置播放」与「系统播放器(SMTC)」之间透明切换 |
| **本地曲库** | 递归扫描 mp3/flac/wav/m4a/ogg/aac，文件名解析「标题 - 艺人」，探测 cover.jpg/folder.jpg 封面；双击入队播放、循环模式（单曲/列表/关闭） |
| **在线音源** | 网易云搜索/播放（**第三方非官方接口，仅供学习，稳定性不保证**，UI 已标注）；封面异步加载 |
| **Navidrome** | Subsonic API 客户端：登录校验、专辑列表/搜索、双击播放；凭据仅存本机 settings.json（明文，注意保管） |
| **均衡器** | 10 段图形均衡器（31Hz..16kHz，±12dB），经 mpv af 滤镜链实时生效，可复位 |
| **3D 环绕音效 + 自定义音效（v0.6）** | 环绕开关/立体声宽度（0–6）/环绕速度（0.1–2Hz），经 ffmpeg `extrastereo` + `aphaser` lavfi 链实时生效；**音效预设**：内置预设 + 导出/导入 JSON 分享（含环绕参数 + 均衡器增益） |
| **3D 歌单架（v0.6）** | Viewport3D 卡片墙：本地歌单封面立体陈列，鼠标拖拽左右滑动（偏航旋转）、滚轮缩放、双击装入队列播放；支持网易云歌单 ID/URL 导入与 .m3u/.txt 文件导入 |
| **底部实时频谱（v0.6）** | 主界面底部常驻 16 柱 RGB 渐变频谱条，复用 WASAPI loopback；**高帧率模式** 60fps / 默认 30fps；空闲自动跳绘省电 |
| **节拍驱动视觉增强（v0.6）** | 低频能量阈值节拍检测（BeatDetector），命中触发：**白色闪烁** + **粒子径向爆发** + **泛光脉冲** + **歌词振动**，可整体开关 |
| **音乐电台 + 热歌榜（v0.6）** | 8 大风格（DJ/电音、流行、民谣、摇滚、古风、说唱、轻音乐、车载）一键生成 60 首推荐队列；网易云飙升/热歌/新歌/原创四大榜单一键播放（非官方接口，已标注风险） |
| **主页毛玻璃 + 视频背景（v0.6）** | 主面板半透明（0.3–1.0 可调）+ 毛玻璃效果；支持自定义本地视频背景（MediaElement，空闲自动暂停） |
| **歌词增强（v0.6）** | 双行歌词（当前+下一行）、竖排歌词、自定义位置（0–100%）、**随节拍振动**；原有逐字卡拉OK与三样式全屏歌词保留 |
| **记忆播放位置（v0.6）** | 退出时保存队列+当前曲+播放位置，下次启动自动续播并提示 |
| **倍速播放 + 音质切换（v0.6）** | 0.5×–2.0× 倍速（mpv speed 实时生效）；音质档位（标准/高/超清，在线接口可用时生效） |
| **自建歌单 + 下一首 + 收藏（v0.6）** | 「我的收藏」+ 自建歌单（JSON 持久化 + 备份）；队列右键「下一首播放」；播放页 ♥ 收藏联动 |
| **全屏沉浸式歌词动画** | Folia 风格自研复刻：**流光**（渐变横向扫过）/ **浮名**（逐字放大浮现）/ **群唱**（当前行高亮脉冲）；Esc/点击退出；背景遮罩可调 |
| **动态配色** | 封面主色扩展为渐变主题，联动媒体条渐变、桌面歌词、全屏歌词动画 |
| **libmpv 视频壁纸** | 全格式 + `hwdec=auto-safe` 硬解，Cover/Contain/Stretch 三种填充，静音循环；dll 缺失自动回退 WPF MediaElement |
| 多显示器壁纸 | 每台物理显示器独立实例，各屏独立内容与状态 |
| 图片壁纸与轮播 / 内置动画壁纸（极光、粒子）/ 壁纸亮度 | 单图+文件夹轮播；可空闲降帧；亮度遮罩 0.3–1.0 |
| WASAPI 频谱律动 | 柱状 / 圆环 / 波形三样式；灵敏度与回落速度可调 |
| 桌面歌词 | 当前行+下一行；多源检索：网易云 → QQ音乐 → LRCLIB；逐字卡拉OK 高亮，无逐字数据自动降级逐行 |
| **桌面整理 DeskBox 风格（v0.5，Windows）** | 每屏壁纸上叠加自绘**文件格子**（文件不移动原位，拖入添加、双击打开、右键重命名/删格/折叠/换色）+ 小组件（**待办/随记/音乐控制**）；每屏独立布局记忆 + 自动快照；不 hook Explorer |
| **系统优化模块（v0.5，Windows）** | 三分区：① Appx 应用列表/卸载（系统包黄色标注）+ winget 安装；② **30 项系统优化**（隐私 7 / 安全 6 / 游戏性能 5 / 更新 4 / 电源 4 / 声音 4，中文说明+风险等级+执行/撤销脚本）；③ Win11Debloat 风格批量移除与去遥测 |
| 设置持久化 / 系统托盘 / 开机自启 | `%AppData%\DogDesk\settings.json`；托盘（显示/应用壁纸/停止/退出）；启动文件夹快捷方式（非侵入） |
| 控制台 | 深色分页 UI（壁纸/媒体条/桌面歌词/频谱/曲库/在线音源/均衡器/电台榜单/歌单架/桌面整理/系统优化/设置），实时日志 |
| **macOS 版（v0.6）** | Avalonia 11：播放 / 曲库 / 在线音源(网易云+Navidrome+电台+榜单) / **歌单**(封面轮播歌单架) / 均衡器(含环绕/倍速/音质) / 设置 六 Tab + 置顶桌面歌词（双行/竖排/位置）+ 全屏流光歌词 + 动态配色 + 记忆播放 + 收藏；libmpv 动态加载（`brew install mpv`） |

> **可选功能（默认关闭，标注风险）**：弹幕（歌词区域弹幕层）、摄像头手势控制（隐私敏感，默认关）、天气氛围推歌（需位置/城市输入）——v0.6 保留设置项但**未实现**，后续版本迭代；网易云在线播放/榜单/歌单导入均为非官方第三方接口，仅供学习、稳定性不保证。

## 系统优化模块的安全边界（务必阅读）

系统优化本质是**系统级修改**，与项目"非侵入式"旧原则冲突，因此独立成模块并强制以下约束：

- **所有修改项默认全不勾选**，只有用户显式勾选并点击「执行勾选项」才生效；启动/切歌/空闲等任何后台逻辑**绝不自动执行**系统修改
- 每项带**风险等级**：🟢 绿=安全可逆 / 🟡 黄=谨慎 / 🔴 红=不建议，并有中文说明
- 执行前弹窗确认 + 检测管理员权限（不足时按项弹 UAC 提权）
- 逐项执行日志（成功/失败/撤销），「撤销勾选项」按记录执行反向脚本还原
- UI 常驻提示：**系统修改有风险，建议先创建系统还原点，后果自负**
- 不 hook Explorer、不驻留后台修改；全部为用户触发的单次操作

## 内存/性能优化（v0.6，Windows）

| 优化项 | 机制 | 预期收益 |
|---|---|---|
| 空闲工作集压缩 | 45s 低频定时器判定真正空闲 → `EmptyWorkingSet` + `GC.Collect(2, Optimized)` | 后台驻留内存显著下降 |
| 频谱按需启停 | 无活跃音频会话时 `Stop()` WASAPI 采集与 FFT，恢复播放再 `Start()` | 无音频时省 CPU |
| 定时器合并 | 空闲判定统一由单一 45s 定时器驱动 | 减少零散高频唤醒 |
| 底部频谱空闲跳绘 | 空闲时跳过 FFT 读取与绘制（显示"频谱空闲"） | 无音频时零采集零绘制 |
| **高帧率模式（v0.6）** | `HighFpsMode` 全局切换频谱/歌词/动画 tick 60fps / 30fps（默认 30fps） | 按需性能档位 |
| 视频背景空闲暂停 | 空闲时 `Pause()`，恢复播放再 `Play()` | 背景视频不白耗 CPU |
| 封面/图片友好解码 | `BitmapCacheOption.OnLoad` + 解码后释放源流；按屏幕上限 `DecodePixelWidth` | 不长期占用文件句柄 |
| 歌词 Run 复用 | 逐字高亮只更新 `Run.Foreground/FontSize`，切行才重建 Inlines | 避免每帧 new Run |
| 窗口懒创建 | 媒体条/桌面歌词/全屏歌词开关首次开启才实例化，关闭置空 | 未启用零实例零定时器 |
| mpv 句柄释放 | 壁纸停止/内置退出时 `mpv_terminate_destroy` + 事件线程 Join | 无 mpv 上下文泄漏 |

> 空闲内存目标 <60MB（后台驻留）。注：Linux 交叉编译环境无法实测 Windows 运行时内存，上表为机制与预期方向，最终数值以真机为准。

## macOS 版（Avalonia）

- 技术路线：**Avalonia 11**（官方 WPF 跨平台迁移框架，原生 macOS 后端，官方宣称 "no Mac needed to compile"，可在 Linux 交叉发布 osx-x64/osx-arm64）—— 来源：[Avalonia UI for macOS](https://avaloniaui.net/avalonia/macos/)
- 共享核心库 `DogDesk.Core`（net8.0，**无 WPF 依赖**）承载模型/设置/歌词检索/频谱数学/播放队列/音源/均衡器/环绕/歌单/电台/记忆播放逻辑，双端复用
- **运行时依赖**：libmpv（`brew install mpv`）。应用启动时依次探测 `/opt/homebrew/lib`（Apple Silicon）→ `/usr/local/lib`（Intel）→ `/usr/lib` 下的 `libmpv.dylib`，未安装则 UI 提示并禁用内置播放
- **未签名/未公证**：Linux 环境无法签名与 notarization，首次打开需在 macOS 上**右键 → 打开**放行 Gatekeeper
- macOS 版不含壁纸（桌面壁纸需 NSWorkspace 系统接口）与系统频谱回采（无 WASAPI），节拍视觉/底部频谱/歌词振动在 macOS 不生效

### macOS 构建（在 Linux 或 macOS 上）

```bash
# 构建（0 警告 0 错误）
dotnet build src/DogDesk.Mac/DogDesk.Mac.csproj -c Release
# 交叉发布（Intel / Apple Silicon）
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-x64 --self-contained true -o artifacts-mac/dogdesk-publish-osx-x64
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-arm64 --self-contained true -o artifacts-mac/dogdesk-publish-osx-arm64
```

## 技术要点

- **跨平台共享核心库**：`src/DogDesk.Core/`（net8.0，无 WPF/Windows 依赖）：AppSettings、SettingsService、LyricsService、SpectrumAnalyzer、PlaybackQueue（含 `InsertAfterCurrent` 下一首）、NeteaseMusicSource（搜索/歌单/榜单）、NavidromeClient、EqualizerPreset/EqualizerAf、**SurroundEffect/EffectPreset/EffectAf（3D 环绕与音效预设）**、**BeatDetector（节拍检测）**、**RadioService（电台聚合）**、**ChartsCatalog（网易云榜单）**、**PlaylistStore（自建歌单/收藏）**、**ResumeStore（记忆播放）**
- **3D 环绕音效**：ffmpeg lavfi `extrastereo`（宽度）+ `aphaser`（环绕速度），与 10 段均衡器组合为一条 mpv af 链，运行时 `set_property af` 实时生效
- **节拍检测**：频谱低频段能量 vs 滚动均值比值（默认 1.5 倍阈值 + 150ms 防抖），驱动白闪/粒子爆发/泛光/歌词振动
- **内置播放内核**：独立 libmpv context（`vo=null`），事件线程观察 `time-pos/pause/duration/eof-reached`；与壁纸视频播放器各自独立
- **非侵入式**（壁纸/媒体条/歌词/桌面格子）：不 hook Explorer、不改注册表、不修改系统文件；系统优化模块除外（用户显式触发）
- **壁纸宿主**：`Progman` → `0x052C` → 枚举含 `SHELLDLL_DefView` 的顶层窗口 → WorkerW（Win8+ 经典方案）
- **桌面格子**：每屏透明叠加窗 `SetParent` 到 WorkerW；格子为文件链接（不移动原位）
- **媒体条定位**：`FindWindow("Shell_TrayWnd")` → `ComputeAnchor` → `SetWindowPos`
- **libmpv 集成**：Windows 用 NuGet `Endpne.LibMPV.Windows`；macOS 动态加载系统 `libmpv.dylib`
- **歌词**：`Lyricify.Lyrics.Helper` 多源检索 + 逐字音节级高亮

## 系统要求与构建（Windows）

- 系统：**Windows 10 1809+ / Windows 11**（x64）
- SDK：.NET 8 SDK（或 Visual Studio 2022 17.8+）
- 首次构建会经 NuGet 自动下载 libmpv（约 80MB，含全编解码），需联网

```powershell
cd DogDesk
dotnet build src/DogDesk/DogDesk.csproj -c Release
```

> 代码已在 Linux 通过 `EnableWindowsTargeting` 交叉编译验证（0 警告 0 错误），WPF 程序只能在 Windows 运行。

## 运行（Windows）

1. 运行 `DogDesk.exe`；首次可勾选「设置 → 开机自启 / 最小化到托盘」
2. 「壁纸」页：选「内置动画」/「本地图片/视频」→ 调亮度/填充 → 「应用壁纸」
3. 「频谱」页：勾选「壁纸频谱律动」→ 重新「应用壁纸」→ 放音乐看律动；主界面底部常驻频谱条随播放律动
4. 「媒体条」页：勾选「显示任务栏媒体条」；系统播放器开「系统媒体控制」显示真实曲目
5. 「桌面歌词」页：勾选显示 → 逐字卡拉OK；双行/竖排/位置/节拍振动在「设置」页歌词增强区调；「全屏歌词」页选样式全屏沉浸
6. 「曲库」页：选音乐文件夹 → 双击播放；「在线音源」页：搜索网易云/Navidrome
7. 「均衡器」页：10 段滑杆 + **3D 环绕**（宽度/速度）+ **音效预设**（导出/导入 JSON）+ **倍速/音质**
8. 「电台/榜单」页：点风格一键 60 首电台队列；点榜单立即播放
9. 「歌单架」页：3D 卡片墙浏览歌单，拖拽/滚轮/双击播放；歌单 ID 导入与 .m3u/.txt 导入；「我的收藏」在播放页 ♥ 联动
10. 「桌面整理」页：开启每屏文件格子与小组件；「系统优化」页：**按需勾选**（默认全不勾选）→ 执行/撤销，建议先建还原点
11. 「设置」页：主题强调色、空闲内存压缩、高帧率模式、主页透明度/毛玻璃、记忆播放、视频背景等

## 已知限制

- **垂直任务栏**：歌词/壁纸已做基础兜底，未深度适配
- **频谱精度**：loopback 为系统输出混音，非单应用音频
- **libmpv 体积**：全功能静态构建 dll 较大（~117MB）
- **非商业签名**：SmartScreen 可能提示「未知发布者」；macOS 版未签名需右键打开
- **歌词/在线音源依赖网络**：网易云/QQ音乐/LRCLIB 接口可能变更；网易云在线播放/榜单/歌单导入均为非官方接口，随时可能失效（UI 已标注）
- **v0.6 降级项**：弹幕/摄像头手势/天气推歌保留设置但未实现（默认关闭）；视频背景为 MediaElement（仅本地文件路径）；歌单架双击按居中歌单播放（未精确命中单卡）；歌词竖排为整卡旋转；Mac 无系统频谱回采（节拍视觉/底部频谱不可用）
- **系统优化命令**：为通用注册表/powercfg/PowerShell 命令，未在每台机器逐一验证，执行前务必先建还原点
- **macOS 版**：需 `brew install mpv`；未在真机实测（Linux 交叉发布，仅保证编译/打包正确）

## 目录结构

```
DogDesk/
├── src/
│   ├── DogDesk.Core/      # 跨平台共享核心库（net8.0，无 WPF 依赖）
│   │   ├── Models/                  # AppSettings / MediaItem / AudioQuality
│   │   └── Services/
│   │       ├── Effect/              # SurroundEffect / EffectPreset / EffectAf（3D 环绕与音效预设）
│   │       ├── Playback/            # PlaybackQueue(含下一首插入) / BeatDetector / ResumeStore
│   │       ├── MusicSource/         # 网易云(搜索/歌单/榜单) / Navidrome / RadioService / ChartsCatalog
│   │       ├── Playlists/           # PlaylistStore / DogDeskPlaylist（收藏+自建歌单）
│   │       └── Settings / Lyrics / SpectrumAnalyzer / Equalizer ...
│   ├── DogDesk/           # Windows WPF 版
│   │   ├── App.xaml(.cs)            # 入口 + 单例 + 会话路由接线 + 记忆播放保存
│   │   ├── MainWindow.xaml(.cs)     # 控制面板（12 Tab）+ MainWindow.v06.cs（v0.6 功能）
│   │   ├── Interop/                 # Win32 P/Invoke / 任务栏 / WorkerW
│   │   ├── Services/
│   │   │   ├── MpvAudioPlayer.cs    # 内置播放内核（af 链/倍速/seek）
│   │   │   ├── MediaSessionRouter.cs / MusicLibraryScanner.cs / CoverLoader.cs / IdleMemoryManager.cs
│   │   │   ├── DeskBox/             # 桌面整理：DeskBoxStore（JSON+快照）
│   │   │   ├── Optimizer/           # 系统优化：OpsCatalog(30项) / ElevationRunner / SystemAppsService
│   │   │   └── Mpv/ WallpaperEngine / Lyrics / Spectrum / Tray / AutoStart ...
│   │   └── Views/                   # 媒体条 / 壁纸 / 频谱 / 歌词 / 全屏歌词 / 歌单架 / DeskBoxOverlay / 粒子
│   └── DogDesk.Mac/       # macOS Avalonia 版（net8.0 + Avalonia 11）
│       ├── Services/                # MpvLib(dylib 探测) / MpvAudioPlayer(af/倍速) / AppServices(协调) / 曲库 / 封面
│       └── Views/                   # MainWindow(6 Tab) / LyricsOverlay(双行/竖排/偏移) / FullscreenLyrics
├── DogDesk.sln
└── README.md
```

## 合规与致谢（许可证声明）

- **Folia**（[chthollyphile/folia-major](https://github.com/chthollyphile/folia-major)，AGPL-3.0）：仅**功能级自研复刻**其全屏歌词动画、智能歌词同步、动态配色、本地曲库、Navidrome 客户端与均衡器思路，**未复制其任何代码**（AGPL 传染性不适用于本项目 MIT 代码）
- **DeskBox**（[Tianyu199509/DeskBox](https://github.com/Tianyu199509/DeskBox)，GPL-3.0）：仅参考功能（文件格子/待办/随记/音乐小组件/多屏布局记忆），**功能级自研复刻，未搬运代码**
- **Winhance**（[memstechtips/Winhance](https://github.com/memstechtips/Winhance)，PolyForm Shield 1.0.0）：仅参考其功能分区（应用管理/优化/定制），**自研实现**，未使用其代码
- **Win11Debloat**（[Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat)，MIT）：参考其批量卸载 UWP 与撤销还原思路，自研实现
- **Mineradio**（[XxHuberrr/Mineradio](https://github.com/XxHuberrr/Mineradio)，GPL）：仅参考其功能形态（3D 环绕音效、3D 歌单架、底部实时频谱、节拍驱动视觉、电台与多平台热歌榜、主页透明/玻璃、歌词增强、记忆播放、倍速音质、自建歌单/下一首/收藏），全部**功能级自研复刻**（mpv af 滤镜链、WPF Viewport3D、频谱能量阈值节拍检测等），**未复制其任何代码或素材**（GPL 传染性不适用于本项目 MIT 代码）
- AF Media Bar（MIT）、Lively Wallpaper（GPL-3.0，仅参考思路）、libmpv（LGPL-2.1，动态链接）、Lyricify Lyrics Helper（见其仓库协议）、Avalonia（MIT）
- 网易云在线音源/榜单/歌单导入为非官方第三方接口，仅供学习，稳定性不保证；Navidrome 凭据明文存本机 settings.json

## License

MIT。
