# MediaBarWallpaper（v0.4.0）

**任务栏媒体控制条 + WorkerW 动态壁纸 + libmpv 全格式视频 + 多源桌面歌词** —— Windows 10/11 桌面美化工具。

形态参考开源项目 [AF Media Bar](https://github.com/Fervent-Tempo/AF-Media-Bar)（任务栏媒体控制条），并集成动态壁纸能力。已实现：SMTC 真实会话、WASAPI 频谱壁纸律动、**libmpv 全格式硬解视频**、**逐字卡拉OK 桌面歌词**、**多显示器壁纸**、设置持久化与托盘驻留。

## 功能（v0.4.0）

| 模块 | 说明 |
|---|---|
| 任务栏媒体条 | 无边框透明悬浮窗，`Shell_TrayWnd` 定位 + `SetWindowPos` 吸附任务栏（靠左/居中/靠右，可拖进度条、滚轮调系统音量）；未播放自动淡出；封面主色生成玻璃感渐变背景 |
| SMTC 真实会话 | `GlobalSystemMediaTransportControlsSessionManager` 读取真实曲目/封面/播放状态/时间线，支持播放/暂停/切歌/**跳转进度**；无播放器时自动回退演示数据 |
| **libmpv 视频壁纸** | 内置 libmpv 2.x：全格式（mkv/mp4/avi/mov/webm/flv…）+ `hwdec=auto-safe` 硬解，GPU 输出，Cover/Contain/Stretch 三种填充，静音循环；dll 缺失自动回退 WPF MediaElement |
| **多显示器壁纸** | 每台物理显示器独立实例（`Screen.AllScreens` + 物理像素 Rect），各屏独立内容与状态 |
| **图片壁纸与轮播** | 单图（Cover/Contain/Stretch）+ 文件夹轮播（间隔 5–60s 可调） |
| **内置动画壁纸** | 极光流动（LinearGradient + 平移/旋转）与粒子星空（30fps 粒子场，可空闲降帧）二选一 |
| **壁纸亮度调节** | 亮度遮罩（0.3–1.0），无需改系统设置 |
| WASAPI 频谱律动 | NAudio loopback + FFT + 16 段对数频段；**柱状 / 圆环 / 波形**三样式；灵敏度（0.5–3.0×）与回落速度（0.4–0.95）可调；WPF 源同窗口叠加，mpv 源独立透明覆盖窗口（解决 airspace） |
| **桌面歌词** | 任务栏上方居中显示当前行+下一行；多源检索：网易云 → QQ音乐 → LRCLIB；**逐字高亮（音节级卡拉OK）**，无逐字数据自动降级逐行；字号 / 透明度 / 主题色可调 |
| **设置持久化** | 全部设置写入 `%AppData%\MediaBarWallpaper\settings.json`，重启自动恢复 |
| **系统托盘** | 关闭窗口最小化到托盘（可关），右键：显示主界面 / 应用壁纸 / 停止壁纸 / 退出；双击还原 |
| **开机自启** | 非侵入：仅创建「启动」文件夹快捷方式（WScript.Shell COM），不写注册表 |
| 控制台 | 深色分页 UI（壁纸/媒体条/桌面歌词/频谱/设置），6 色主题强调色，实时日志 |

## 技术要点

- **非侵入式**：不 hook Explorer、不改注册表、不修改系统文件，全部走公开 Win32 API，降低 Defender 误报与系统更新失效风险
- **壁纸宿主**：`Progman` → 发送 `0x052C` → 枚举含 `SHELLDLL_DefView` 的顶层窗口 → 取其父窗口即 WorkerW（Win8+ 经典方案，Lively 同思路）
- **媒体条定位**：`FindWindow("Shell_TrayWnd")` 取矩形 → `ComputeAnchor` 计算吸附位置 → `SetWindowPos`，定时器检测变化
- **libmpv 集成**：NuGet 包 `Endpne.LibMPV.Windows` 分发 native dll 到 `libmpv/win-x64/`；启动时 `NativeLibrary.Load` 预加载；自写薄 P/Invoke，渲染走 `wid` 宿主句柄，mpv 内部自建 GPU 上下文（`vo=gpu`、`gpu-api=d3d11`）
- **SMTC**：WinRT 事件统一封送 UI 线程再广播；跳转走 `TryChangePlaybackPositionAsync`
- **频谱**：WASAPI loopback → FFT → 对数频段归一化 → 灵敏度放大 + 指数回落平滑；mpv 源因独立 hwnd 的 airspace，频谱放独立分层窗口（`WS_EX_LAYERED|WS_EX_TRANSPARENT`，SetParent WorkerW，z 序置顶）
- **歌词**：`Lyricify.Lyrics.Helper` 的 SearchHelper/ProviderHelper/ParseHelper；逐字数据（`FullSyllableLineInfo.Syllables`）按音节时间戳高亮 `Run`，80ms 定时器刷新
- **音量滚轮**：NAudio `AudioEndpointVolume`（系统主音量，非进程级）
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

1. 运行 `MediaBarWallpaper.exe`；首次可勾选「设置 → 开机自启 / 最小化到托盘」
2. 「壁纸」页：选「内置动画」（极光/粒子）或「本地图片/视频」→ 调亮度/填充 → 点「应用壁纸」
3. 「频谱」页：勾选「壁纸频谱律动」→ 重新「应用壁纸」→ 放音乐看律动（柱状/圆环/波形可切）
4. 「媒体条」页：勾选「显示任务栏媒体条」→ 吸附任务栏；播放器开「系统媒体控制」显示真实曲目/封面；滚轮调音量、拖动进度跳转
5. 「桌面歌词」页：勾选显示 → 任务栏上方逐字滚动（网易云/QQ音乐/LRCLIB 自动回退）
6. 「设置」页：切换主题强调色；日志区有 WorkerW 句柄、任务栏矩形、词源、虚拟桌面尺寸等诊断信息

## 已知限制（当前版本）

- **垂直任务栏**：歌词/壁纸已做基础兜底，未深度适配
- **频谱精度**：loopback 为系统输出混音，非单应用音频
- **libmpv 体积**：全功能静态构建 dll 较大（~117MB），后续可自行编译精简版
- **非商业签名**：SmartScreen 可能提示「未知发布者」，正式发布需代码签名
- **歌词源依赖网络**：网易云/QQ音乐接口若变更需随库更新；离线时歌词不可用

## 目录结构

```
MediaBarWallpaper/
├── src/MediaBarWallpaper/
│   ├── App.xaml(.cs)            # 入口 + 单例 + 托盘/自启接线
│   ├── MainWindow.xaml(.cs)     # 控制面板（Tab 分页 + 设置面板）
│   ├── Models/
│   │   └── AppSettings.cs       # 全部可配置项（持久化模型）
│   ├── Interop/
│   │   ├── NativeMethods.cs     # Win32 P/Invoke
│   │   ├── TaskbarService.cs    # 任务栏定位与吸附计算
│   │   └── WallpaperHost.cs     # WorkerW 宿主发现/创建
│   ├── Services/
│   │   ├── IMediaSession.cs     # 会话抽象（含 Seek）
│   │   ├── SmtcMediaSession.cs  # SMTC 真实会话（含演示回退）
│   │   ├── MockMediaSession.cs  # 纯演示会话
│   │   ├── SettingsService.cs   # settings.json 读写
│   │   ├── TrayService.cs       # NotifyIcon 托盘
│   │   ├── AutoStartService.cs  # 启动文件夹快捷方式
│   │   ├── SystemVolumeService.cs # 系统主音量
│   │   ├── AudioSpectrumService.cs # WASAPI + FFT 频谱（灵敏度/平滑）
│   │   ├── LyricsService.cs     # 多源歌词检索/解析
│   │   ├── WallpaperEngine.cs   # 壁纸引擎（多显示器 + 三源 + 频谱）
│   │   └── Mpv/
│   │       ├── MpvNativeMethods.cs # libmpv P/Invoke
│   │       ├── MpvNativeLoader.cs  # native dll 预加载
│   │       └── MpvVideoPlayer.cs   # mpv 托管封装（事件线程）
│   └── Views/
│       ├── MediaBarWindow.xaml(.cs)      # 任务栏媒体条（进度/音量/渐变）
│       ├── MpvWallpaperWindow.xaml(.cs)  # mpv 视频壁纸窗口
│       ├── SpectrumOverlayWindow.xaml(.cs) # 透明频谱覆盖窗口
│       ├── SpectrumBarsControl.xaml(.cs)   # 柱状/圆环/波形频谱控件
│       ├── LyricsWindow.xaml(.cs)        # 桌面歌词窗口（逐字卡拉OK）
│       ├── ParticleFieldControl.xaml(.cs) # 粒子星空动画
│       ├── CoverPalette.cs               # 封面主色提取
│       └── SpectrumBarsControl.xaml(.cs) # 频谱控件
└── MediaBarWallpaper.sln
```

## 后续规划

1. Inno Setup 安装器（含 libmpv 精简版选项）、自动更新、代码签名
2. 任务栏图标避让、全屏游戏自动暂停、进程空闲内存压缩（目标 <60MB）
3. 壁纸动效扩展（视频滤镜、时钟小组件、动态天气）
4. 歌词译文/罗马音并行显示

## License

MIT。参考：AF Media Bar（MIT）、Lively Wallpaper（GPL-3.0，仅参考思路）、libmpv（LGPL-2.1，动态链接）、Lyricify Lyrics Helper（见其仓库协议）。
