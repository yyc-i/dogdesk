# DogDesk（v0.5.0）

仓库：https://github.com/yyc-i/dogdesk  ·  显示名 `dogdesk`（内部标识 DogDesk）。

**任务栏媒体控制条 + WorkerW 动态壁纸 + libmpv 全格式视频 + 多源桌面歌词 + Folia 风格全屏歌词动画 + 内置播放内核 + 桌面整理 + 系统优化 + macOS 版** —— Windows 10/11 桌面美化工具（macOS 版同步提供）。

> 注：v0.5.0 起由 MediaBarWallpaper 更名，旧 `%AppData%\MediaBarWallpaper` 配置（settings.json / deskbox.json）在首次启动时自动迁移到 `%AppData%\DogDesk\`，旧文件保留不删除。

形态参考开源项目 [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（任务栏媒体控制条），并集成动态壁纸能力。v0.5.0 起引入跨平台共享核心库（`DogDesk.Core`），Windows（WPF）与 macOS（Avalonia）双端共用同一套模型/设置/歌词/频谱/播放队列/音源/均衡器逻辑。

## 功能（v0.5.0）

| 模块 | 说明 |
|---|---|
| 任务栏媒体条 | 无边框透明悬浮窗，`Shell_TrayWnd` 定位 + `SetWindowPos` 吸附任务栏（靠左/居中/靠右，可拖进度条、滚轮调系统音量）；未播放自动淡出；封面主色生成玻璃感渐变背景 |
| SMTC 真实会话 | `GlobalSystemMediaTransportControlsSessionManager` 读取真实曲目/封面/播放状态/时间线，支持播放/暂停/切歌/跳转进度；无播放器时自动回退演示数据 |
| **内置播放内核（v0.5）** | 独立 libmpv 音频会话（`vo=null` 纯音频），驱动本地曲库/在线音源/Navidrome 播放；**会话路由**在「内置播放」与「系统播放器(SMTC)」之间透明切换：内置播放时媒体条/歌词/封面主色跟随内置，内置停止后自动切回系统会话 |
| **本地曲库（v0.5）** | 递归扫描 mp3/flac/wav/m4a/ogg/aac，文件名解析「标题 - 艺人」，探测 cover.jpg/folder.jpg 封面；双击入队播放、当前曲高亮、循环模式（单曲/列表/关闭） |
| **在线音源（v0.5）** | 网易云搜索/播放（**第三方非官方接口，仅供学习，稳定性不保证**，UI 已标注）；封面异步加载 |
| **Navidrome（v0.5）** | Subsonic API 客户端：登录校验、专辑列表/搜索、双击播放；凭据仅存本机 settings.json（明文，注意保管） |
| **均衡器（v0.5）** | 10 段图形均衡器（31Hz..16kHz，±12dB），经 mpv af 滤镜链（ffmpeg lavfi equalizer）实时生效，可复位 |
| **全屏沉浸式歌词动画（v0.5）** | Folia 风格自研复刻：**流光**（渐变横向扫过）/ **浮名**（逐字放大浮现）/ **群唱**（当前行高亮脉冲）三样式；复用逐字引擎随播放进度同步；Esc/点击退出；背景遮罩可调 |
| **动态配色（v0.5）** | 封面主色扩展为渐变主题，联动媒体条渐变、桌面歌词、全屏歌词动画 |
| **libmpv 视频壁纸** | 全格式（mkv/mp4/avi/mov/webm/flv…）+ `hwdec=auto-safe` 硬解，Cover/Contain/Stretch 三种填充，静音循环；dll 缺失自动回退 WPF MediaElement |
| 多显示器壁纸 | 每台物理显示器独立实例（`Screen.AllScreens` + 物理像素 Rect），各屏独立内容与状态 |
| 图片壁纸与轮播 | 单图（Cover/Contain/Stretch）+ 文件夹轮播（间隔 5–60s 可调） |
| 内置动画壁纸 | 极光流动与粒子星空二选一，可空闲降帧 |
| 壁纸亮度调节 | 亮度遮罩（0.3–1.0），无需改系统设置 |
| WASAPI 频谱律动 | NAudio loopback + FFT + 16 段对数频段；柱状 / 圆环 / 波形三样式；灵敏度与回落速度可调 |
| 桌面歌词 | 任务栏上方当前行+下一行；多源检索：网易云 → QQ音乐 → LRCLIB；逐字卡拉OK 高亮（音节级），无逐字数据自动降级逐行 |
| **桌面整理 DeskBox 风格（v0.5，Windows）** | 每屏壁纸上叠加自绘**文件格子**：虚拟收纳（文件不移动原位），拖入文件/文件夹即添加格子、双击打开原文件、右键重命名/删格/折叠/换色；小组件：**待办 / 随记 / 音乐控制**；每屏独立布局记忆（`deskbox.json`），退出与定时自动快照（`deskbox.backup.json`）；不 hook Explorer、不驻留修改系统 |
| **系统优化模块（v0.5，Windows）** | 独立 Tab，三分区：① 软件与应用管理——Appx 应用列表/卸载（系统包黄色标注）、winget 搜索/安装；② **30 项系统优化**（隐私 7 / 安全 6 / 游戏性能 5 / 更新 4 / 电源 4 / 声音 4，每项含中文说明+风险等级+执行/撤销脚本）；③ Win11Debloat 风格 UWP 批量移除与去遥测 |
| 设置持久化 | 全部设置写入 `%AppData%\DogDesk\settings.json`，重启自动恢复 |
| 系统托盘 | 关闭窗口最小化到托盘（可关），右键：显示主界面 / 应用壁纸 / 停止壁纸 / 退出 |
| 开机自启 | 非侵入：仅创建「启动」文件夹快捷方式，不写注册表 |
| 控制台 | 深色分页 UI（壁纸/媒体条/桌面歌词/频谱/曲库/在线音源/均衡器/桌面整理/系统优化/设置），实时日志 |
| **macOS 版（v0.5）** | Avalonia 11 原生界面：播放 / 曲库 / 在线音源(网易云+Navidrome) / 均衡器 / 设置五 Tab + 置顶桌面歌词 + 全屏流光歌词 + 动态配色；libmpv 动态加载（`brew install mpv` 后自动识别） |

## 系统优化模块的安全边界（务必阅读）

系统优化本质是**系统级修改**，与项目"非侵入式"旧原则冲突，因此独立成模块并强制以下约束：

- **所有修改项默认全不勾选**，只有用户显式勾选并点击「执行勾选项」才生效；启动/切歌/空闲等任何后台逻辑**绝不自动执行**系统修改
- 每项带**风险等级**：🟢 绿=安全可逆 / 🟡 黄=谨慎 / 🔴 红=不建议，并有中文说明
- 执行前弹窗确认 + 检测管理员权限（不足时按项弹 UAC 提权）
- 逐项执行日志（成功/失败/撤销），「撤销勾选项」按记录执行反向脚本还原
- UI 常驻提示：**系统修改有风险，建议先创建系统还原点，后果自负**
- 不 hook Explorer、不驻留后台修改；全部为用户触发的单次操作

## 内存/性能优化（v0.5，Windows）

| 优化项 | 机制 | 预期收益 |
|---|---|---|
| 空闲工作集压缩 | 45s 低频定时器判定真正空闲（无播放+媒体条/歌词/全屏歌词均未开）→ `EmptyWorkingSet` + `GC.Collect(2, Optimized)` | 后台驻留内存显著下降 |
| 频谱按需启停 | 无活跃音频会话/未播放时 `Stop()` WASAPI 采集与 FFT，恢复播放再 `Start()` | 无音频时省 CPU 与内存分配 |
| 定时器合并 | 空闲判定统一由单一 45s 定时器驱动 | 减少零散高频唤醒 |
| 封面/图片友好解码 | `BitmapCacheOption.OnLoad` + 解码后释放源流；HTTP 封面走 MemoryStream；按屏幕上限 `DecodePixelWidth` | 不长期占用文件句柄、超大图不全尺寸解码 |
| 歌词 Run 复用 | 逐字高亮只更新 `Run.Foreground/FontSize`，切行才重建 Inlines | 避免每帧 new Run |
| 窗口懒创建 | 媒体条/桌面歌词/全屏歌词开关首次开启才实例化，关闭置空 | 未启用零实例零定时器 |
| mpv 句柄释放 | 壁纸停止/内置退出时 `mpv_terminate_destroy` + 事件线程 Join | 无 mpv 上下文泄漏 |

> 空闲内存目标 <60MB（后台驻留）。注：Linux 交叉编译环境无法实测 Windows 运行时内存，上表为机制与预期方向，最终数值以真机为准。

## macOS 版（Avalonia）

- 技术路线：**Avalonia 11**（官方 WPF 跨平台迁移框架，原生 macOS 后端，官方宣称 "no Mac needed to compile"，可在 Linux 交叉发布 osx-x64/osx-arm64）—— 来源：[Avalonia UI for macOS](https://avaloniaui.net/avalonia/macos/)
- 共享核心库 `DogDesk.Core`（net8.0，**无 WPF 依赖**）承载模型/设置/歌词检索/频谱数学/播放队列/网易云与 Navidrome 客户端/均衡器参数，Windows 与 macOS 双端复用
- **运行时依赖**：libmpv（`brew install mpv`）。应用启动时依次探测 `/opt/homebrew/lib`（Apple Silicon）→ `/usr/local/lib`（Intel）→ `/usr/lib` 下的 `libmpv.dylib`，未安装则 UI 提示并禁用内置播放
- **未签名/未公证**：Linux 环境无法签名与 notarization，首次打开需在 macOS 上**右键 → 打开**放行 Gatekeeper
- v0.5 macOS 版不含壁纸（桌面壁纸需 NSWorkspace 系统接口，后续迭代）

### macOS 构建（在 Linux 或 macOS 上）

```bash
# 构建（0 警告 0 错误）
dotnet build src/DogDesk.Mac/DogDesk.Mac.csproj -c Release
# 交叉发布（Intel / Apple Silicon）
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-x64 --self-contained true
dotnet publish src/DogDesk.Mac/DogDesk.Mac.csproj -c Release -r osx-arm64 --self-contained true
```

## 技术要点

- **跨平台共享核心库**：`src/DogDesk.Core/`（net8.0，无 WPF/Windows 依赖）：AppSettings、SettingsService、LyricsService、SpectrumAnalyzer（FFT 数学）、PlaybackQueue（纯逻辑播放队列）、NeteaseMusicSource、NavidromeClient（Subsonic）、EqualizerPreset/EqualizerAf（mpv af 链生成）
- **内置播放内核**：独立 libmpv context（`vo=null`），事件线程观察 `time-pos/pause/duration/eof-reached`，实现 IMediaSession；与壁纸视频播放器各自独立 context
- **非侵入式**（壁纸/媒体条/歌词/桌面格子）：不 hook Explorer、不改注册表、不修改系统文件，全部走公开 Win32 API；系统优化模块除外（见上节，用户显式触发）
- **壁纸宿主**：`Progman` → 发送 `0x052C` → 枚举含 `SHELLDLL_DefView` 的顶层窗口 → 取其父窗口即 WorkerW（Win8+ 经典方案）
- **桌面格子**：每屏透明叠加窗 `SetParent` 到 WorkerW，与壁纸共存；格子项为文件/文件夹链接（不移动原位）
- **媒体条定位**：`FindWindow("Shell_TrayWnd")` → `ComputeAnchor` → `SetWindowPos`
- **libmpv 集成**：NuGet `Endpne.LibMPV.Windows` 分发 native dll 到 `libmpv/win-x64/`；启动时预加载；自写薄 P/Invoke
- **频谱**：WASAPI loopback → FFT → 对数频段归一化 → 灵敏度放大 + 指数回落平滑
- **歌词**：`Lyricify.Lyrics.Helper` 多源检索 + 逐字 `FullSyllableLineInfo.Syllables` 音节级高亮
- **窗口样式**：媒体条/歌词 `WS_EX_TOOLWINDOW`（不进 Alt+Tab）；壁纸窗口 `WS_EX_NOACTIVATE`（不抢焦点）
- **DPI**：PerMonitorV2，物理像素定位

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
3. 「频谱」页：勾选「壁纸频谱律动」→ 重新「应用壁纸」→ 放音乐看律动
4. 「媒体条」页：勾选「显示任务栏媒体条」；系统播放器开「系统媒体控制」显示真实曲目
5. 「桌面歌词」页：勾选显示 → 逐字卡拉OK；「全屏歌词」页选样式（流光/浮名/群唱）全屏沉浸
6. 「曲库」页：选音乐文件夹 → 双击播放（媒体条/歌词自动跟随内置播放）；「在线音源」页：搜索网易云/Navidrome
7. 「均衡器」页：10 段滑杆实时调音；「桌面整理」页：开启每屏文件格子与小组件
8. 「系统优化」页：**按需勾选**（默认全不勾选）→ 执行/撤销，建议先建还原点
9. 「设置」页：主题强调色、空闲内存压缩等；日志区有诊断信息

## 已知限制

- **垂直任务栏**：歌词/壁纸已做基础兜底，未深度适配
- **频谱精度**：loopback 为系统输出混音，非单应用音频
- **libmpv 体积**：全功能静态构建 dll 较大（~117MB）
- **非商业签名**：SmartScreen 可能提示「未知发布者」；macOS 版未签名需右键打开
- **歌词/在线音源依赖网络**：网易云/QQ音乐/LRCLIB 接口可能变更；网易云在线播放为非官方接口，随时可能失效（UI 已标注"仅供学习、稳定性不保证"）
- **系统优化命令**：为通用注册表/powercfg/PowerShell 命令，未在每台机器逐一验证，执行前务必先建还原点
- **macOS 版**：需 `brew install mpv`；未在真机实测（Linux 交叉发布，仅保证编译/打包正确）

## 目录结构

```
DogDesk/
├── src/
│   ├── DogDesk.Core/      # 跨平台共享核心库（net8.0，无 WPF 依赖）
│   │   ├── Models/                  # AppSettings / MediaItem
│   │   └── Services/                # Settings / Lyrics / SpectrumAnalyzer /
│   │                                # Playback / MusicSource(网易云,Navidrome) / Equalizer
│   ├── DogDesk/           # Windows WPF 版
│   │   ├── App.xaml(.cs)            # 入口 + 单例 + 会话路由接线
│   │   ├── MainWindow.xaml(.cs)     # 控制面板（10 Tab）
│   │   ├── Interop/                 # Win32 P/Invoke / 任务栏 / WorkerW
│   │   ├── Services/
│   │   │   ├── MpvAudioPlayer.cs    # 内置音频播放内核（libmpv，IMediaSession）
│   │   │   ├── MediaSessionRouter.cs # 内置 ↔ SMTC 会话路由
│   │   │   ├── MusicLibraryScanner.cs / CoverLoader.cs / IdleMemoryManager.cs
│   │   │   ├── DeskBox/             # 桌面整理：DeskBoxStore（JSON+快照）
│   │   │   ├── Optimizer/           # 系统优化：OpsCatalog(30项) / ElevationRunner / SystemAppsService
│   │   │   └── Mpv/ WallpaperEngine / Lyrics / Spectrum / Tray / AutoStart ...
│   │   └── Views/                   # 媒体条 / 壁纸 / 频谱 / 歌词 / 全屏歌词动画 /
│   │                                # DeskBoxOverlay / 粒子 / CoverPalette
│   └── DogDesk.Mac/       # macOS Avalonia 版（net8.0 + Avalonia 11）
│       ├── Services/                # MpvLib(dylib 探测) / MpvAudioPlayer / AppServices / 曲库 / 封面
│       └── Views/                   # MainWindow(5 Tab) / LyricsOverlay / FullscreenLyrics
├── DogDesk.sln
└── README.md
```

## 合规与致谢（许可证声明）

- **Folia**（[chthollyphile/folia-major](https://github.com/chthollyphile/folia-major)，AGPL-3.0）：本项目仅**功能级自研复刻**其全屏歌词动画（流光/浮名/群唱）、智能歌词同步、动态配色、本地曲库、Navidrome 客户端与均衡器思路，**未复制其任何代码**（AGPL 传染性不适用于本项目 MIT 代码）
- **DeskBox**（[Tianyu199509/DeskBox](https://github.com/Tianyu199509/DeskBox)，GPL-3.0）：仅参考功能（文件格子/待办/随记/音乐小组件/多屏布局记忆），**功能级自研复刻，未搬运代码**
- **Winhance**（[memstechtips/Winhance](https://github.com/memstechtips/Winhance)，PolyForm Shield 1.0.0）：仅参考其功能分区（应用管理/优化/定制），**自研实现**，未使用其代码
- **Win11Debloat**（[Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat)，MIT）：参考其批量卸载 UWP 与撤销还原思路，自研实现
- AF Media Bar（MIT）、Lively Wallpaper（GPL-3.0，仅参考思路）、libmpv（LGPL-2.1，动态链接）、Lyricify Lyrics Helper（见其仓库协议）、Avalonia（MIT）
- 网易云在线音源为非官方第三方接口，仅供学习，稳定性不保证；Navidrome 凭据明文存本机 settings.json

## License

MIT。
