# MediaBarWallpaper（v0.3.0）

**任务栏媒体控制条 + WorkerW 动态壁纸 + libmpv 全格式视频 + 桌面歌词** —— Windows 10/11 桌面美化工具。

形态参考开源项目 [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（任务栏媒体控制条），已实现 SMTC 真实会话、WASAPI 频谱壁纸律动、**libmpv 全格式硬解视频** 与 **多源桌面歌词**。

## 功能（v0.3.0）

| 模块 | 说明 |
|---|---|
| 任务栏媒体条 | 无边框透明悬浮窗，`Shell_TrayWnd` 定位 + `SetWindowPos` 吸附任务栏（靠左/居中/靠右），定时检测任务栏变化自动重定位；未播放自动半透明 |
| SMTC 真实会话 | `GlobalSystemMediaTransportControlsSessionManager` 读取真实曲目/封面/播放状态/时间线，支持播放/暂停/切歌；无播放器时自动回退演示数据 |
| 封面显示 | 真实封面（SMTC Thumbnail → BitmapImage）；无封面显示音符占位 |
| **libmpv 视频壁纸** | 内置 libmpv 2.x：全格式（mkv/mp4/avi/mov/webm/flv…）+ `hwdec=auto-safe` 硬解，GPU 输出，cover 裁剪铺满，静音循环；dll 缺失自动回退 WPF MediaElement |
| WASAPI 频谱律动 | NAudio loopback + FFT + 16 段对数频段；WPF 源同窗口叠加，mpv 源用独立透明覆盖窗口（解决 airspace） |
| **桌面歌词** | 任务栏上方居中显示当前行（高亮）+ 下一行；多源检索：网易云 → QQ音乐 → LRCLIB，自动识别逐字/逐行，逐字降级逐行 |
| 控制台 | 深色面板：壁纸应用/停止、频谱/媒体条/歌词开关、会话状态、视频内核状态、实时日志 |

## 技术要点

- **非侵入式**：不 hook Explorer、不改注册表、不修改系统文件，全部走公开 Win32 API，降低 Defender 误报与系统更新失效风险
- **壁纸宿主**：`Progman` → 发送 `0x052C` → 枚举含 `SHELLDLL_DefView` 的顶层窗口 → 取其父窗口即 WorkerW（Win8+ 经典方案，Lively 同思路）
- **媒体条定位**：`FindWindow("Shell_TrayWnd")` 取矩形 → `ComputeAnchor` 计算吸附位置 → `SetWindowPos`，定时器检测变化
- **libmpv 集成**：NuGet 包 `Endpne.LibMPV.Windows` 分发 native dll 到 `libmpv/win-x64/`；启动时 `NativeLibrary.Load` 预加载；自写薄 P/Invoke（create/initialize/set_option_string/command/observe_property/wait_event），渲染走 `wid` 宿主句柄，mpv 内部自建 GPU 上下文（`vo=gpu`、`gpu-api=d3d11`）
- **SMTC**：WinRT 事件统一封送 UI 线程再广播
- **频谱**：WASAPI loopback → FFT → 对数频段归一化 → 平滑；mpv 源因独立 hwnd 的 airspace，频谱放独立分层窗口（`WS_EX_LAYERED|WS_EX_TRANSPARENT`，SetParent WorkerW，z 序置顶）
- **歌词**：`Lyricify.Lyrics.Helper` 的 SearchHelper/ProviderHelper/ParseHelper，解析为统一 `LyricsData`；按 `PositionSec` 定位当前行
- **窗口样式**：媒体条/歌词 `WS_EX_TOOLWINDOW`（不进 Alt+Tab）；壁纸窗口 `WS_EX_NOACTIVATE`（不抢焦点）
- **DPI**：app.manifest 声明 PerMonitorV2，定位用物理像素

## 系统要求与构建

- 系统：**Windows 10 1809+ / Windows 11**（x64）
- SDK：.NET 8 SDK（或 Visual Studio 2022 17.8+）
- 首次构建会经 NuGet 自动下载 libmpv（约 80MB，含全编解码），需联网
- 构建（PowerShell）：

```powershell
cd MediaBarWallpaper
dotnet build src/MediaBarWallpaper/MediaBarWallpaper.csproj -c Release
```

> 代码已在 Linux 通过 `EnableWindowsTargeting` 交叉编译验证（0 警告 0 错误），
> 但 WPF 程序只能在 Windows 运行，请在 Windows 机器构建执行。

## 运行

1. 运行 `MediaBarWallpaper.exe`，日志区显示视频内核状态（libmpv 已加载 / 回退）
2. 点「应用壁纸」先试「内置动画」；勾选「壁纸频谱律动」再应用，放音乐看律动
3. 选「本地视频」→ 浏览任意格式视频 → 应用（libmpv 全格式硬解）
4. 勾选「显示媒体条」→ 吸附任务栏；播放器开「系统媒体控制」显示真实曲目/封面
5. 勾选「桌面歌词」→ 任务栏上方显示当前歌词行并随播放滚动
6. 日志区有 WorkerW 句柄、任务栏矩形、词源、虚拟桌面尺寸等诊断信息

## 已知限制（当前版本）

- **多显示器**：单个窗口覆盖整个虚拟桌面；正式版每显示器独立实例
- **垂直任务栏**：歌词/壁纸已做基础兜底，未深度适配
- **无播放自动隐藏**：当前为半透明，正式版淡出回收
- **频谱精度**：loopback 为系统输出混音，非单应用音频
- **逐字歌词**：当前降级为逐行显示，逐字动画待做
- **libmpv 体积**：全功能静态构建 dll 较大（~117MB），后续可自行编译精简版
- **非商业签名**：SmartScreen 可能提示「未知发布者」，正式发布需代码签名

## 目录结构

```
MediaBarWallpaper/
├── src/MediaBarWallpaper/
│   ├── App.xaml(.cs)            # 入口 + 全局资源/单例（SMTC/壁纸/频谱/歌词）
│   ├── MainWindow.xaml(.cs)     # 控制面板
│   ├── Interop/
│   │   ├── NativeMethods.cs     # Win32 P/Invoke
│   │   ├── TaskbarService.cs    # 任务栏定位与吸附计算
│   │   └── WallpaperHost.cs     # WorkerW 宿主发现/创建
│   ├── Services/
│   │   ├── IMediaSession.cs     # 会话抽象（PositionSec/DurationSec/Thumbnail）
│   │   ├── SmtcMediaSession.cs  # SMTC 真实会话（含演示回退）
│   │   ├── MockMediaSession.cs  # 纯演示会话
│   │   ├── AudioSpectrumService.cs # WASAPI + FFT 频谱
│   │   ├── LyricsService.cs     # 多源歌词检索/解析
│   │   ├── WallpaperEngine.cs   # 壁纸引擎（三源 + 频谱 + WorkerW 注入）
│   │   └── Mpv/
│   │       ├── MpvNativeMethods.cs # libmpv P/Invoke + 常量/结构体
│   │       ├── MpvNativeLoader.cs  # native dll 预加载
│   │       └── MpvVideoPlayer.cs   # mpv 托管封装（事件线程）
│   └── Views/
│       ├── MediaBarWindow.xaml(.cs)      # 任务栏媒体条
│       ├── MpvWallpaperWindow.xaml(.cs)  # mpv 视频壁纸窗口（WindowsFormsHost）
│       ├── SpectrumOverlayWindow.xaml(.cs) # 透明频谱覆盖窗口
│       ├── SpectrumBarsControl.xaml(.cs)   # 可复用 16 柱频谱控件
│       └── LyricsWindow.xaml(.cs)        # 桌面歌词窗口
└── MediaBarWallpaper.sln
```

## 后续规划

1. 逐字歌词动画（音节级高亮）、译文/罗马音
2. 任务栏图标避让、全屏游戏自动暂停、空闲降帧
3. 多显示器独立实例、后台内存压缩（目标 <60MB）
4. Inno Setup 安装器、自动更新、代码签名

## License

MIT。参考：AF Media Bar（MIT）、Lively Wallpaper（GPL-3.0，仅参考思路）、libmpv（LGPL-2.1，动态链接）、Lyricify Lyrics Helper（见其仓库协议）。
