using System.IO;
using System.Runtime.InteropServices;
using MediaBarWallpaper.Models;

namespace MediaBarWallpaper.Services.Mpv;

/// <summary>
/// libmpv 托管封装：创建实例 → 配置嵌入/硬解/静音/循环/cover → 事件线程观察时长与位置 → 销毁。
/// 渲染走 wid（宿主窗口句柄），mpv 内部自建 GPU 上下文，无需 render API / D3DImage。
/// </summary>
internal sealed class MpvVideoPlayer : IDisposable
{
    private IntPtr _ctx;
    private Thread? _eventThread;
    private volatile bool _running;
    private readonly IntPtr _wid;
    private readonly StretchMode _stretch;
    private ulong _observeSeq;

    public double Duration { get; private set; }
    public double Position { get; private set; }
    public bool IsLoaded { get; private set; }

    public event Action<string>? Log;
    public event Action? FileEnded;

    public MpvVideoPlayer(IntPtr wid, StretchMode stretch = StretchMode.Cover)
    {
        _wid = wid;
        _stretch = stretch;
    }

    public void Initialize()
    {
        _ctx = MpvNative.Create();
        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create 返回空句柄（libmpv 不可用？）");

        // —— 嵌入与渲染（wid 必须在 initialize 之前设置）——
        SetOpt("wid", _wid.ToString());
        SetOpt("vo", "gpu");                 // GPU 视频输出（Windows 上走 d3d11）
        SetOpt("hwdec", "auto-safe");        // 安全白名单硬件解码
        SetOpt("gpu-api", "d3d11");
        // —— 壁纸场景行为 ——
        SetOpt("mute", "yes");               // 壁纸视频静音
        SetOpt("volume", "0");
        SetOpt("loop-file", "inf");          // 单文件循环
        SetOpt("keep-open", "yes");
        SetOpt("idle", "yes");
        ApplyStretchOptions();               // cover / contain / stretch 填充模式
        SetOpt("video-align-x", "0");
        SetOpt("video-align-y", "0");
        // —— 关闭交互/OSD/光标 ——
        SetOpt("input-default-bindings", "no");
        SetOpt("input-vo-keyboard", "no");
        SetOpt("input-cursor", "no");
        SetOpt("cursor-autohide", "no");
        SetOpt("osd-level", "0");
        SetOpt("config", "no");
        SetOpt("terminal", "no");
        SetOpt("msg-level", "all=error");

        int err = MpvNative.Initialize(_ctx);
        if (err < 0)
            throw new InvalidOperationException($"mpv_initialize 失败：{ErrorText(err)}");

        // 观察属性（reply userdata 用递增序号）
        Observe("duration", MpvFormat.Double);
        Observe("time-pos", MpvFormat.Double);
        Observe("pause", MpvFormat.Flag);

        _running = true;
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "mpv-event"
        };
        _eventThread.Start();
        IsLoaded = true;
        Log?.Invoke($"mpv 已初始化（client API {MpvNative.ClientApiVersion() >> 16}.{MpvNative.ClientApiVersion() & 0xFFFF}）");
    }

    public void LoadFile(string path)
    {
        if (_ctx == IntPtr.Zero) return;
        // 用命令数组传参，路径中的特殊字符更安全
        int err = MpvNative.Command(_ctx, new[] { "loadfile", path, "replace" });
        if (err < 0)
            Log?.Invoke($"loadfile 失败：{ErrorText(err)}");
        else
            Log?.Invoke($"mpv 加载视频：{Path.GetFileName(path)}");
    }

    public void SetPaused(bool paused)
    {
        if (_ctx != IntPtr.Zero)
            MpvNative.SetPropertyString(_ctx, "pause", paused ? "yes" : "no");
    }

    private void Observe(string name, int format)
        => MpvNative.ObserveProperty(_ctx, ++_observeSeq, name, format);

    private void SetOpt(string name, string value)
    {
        int err = MpvNative.SetOptionString(_ctx, name, value);
        if (err < 0)
            Log?.Invoke($"选项 {name}={value} 设置失败：{ErrorText(err)}");
    }

    /// <summary>按填充模式配置 panscan / keepaspect。</summary>
    private void ApplyStretchOptions()
    {
        switch (_stretch)
        {
            case StretchMode.Contain:
                SetOpt("panscan", "0");
                SetOpt("keepaspect", "yes");
                break;
            case StretchMode.Stretch:
                SetOpt("panscan", "0");
                SetOpt("keepaspect", "no");  // 允许拉伸填满
                break;
            default: // Cover
                SetOpt("panscan", "1.0");    // 配合 keepaspect 裁剪填满
                SetOpt("keepaspect", "yes");
                break;
        }
    }

    private string ErrorText(int err)
    {
        IntPtr p = MpvNative.ErrorString(err);
        return p == IntPtr.Zero ? err.ToString() : (Marshal.PtrToStringUTF8(p) ?? err.ToString());
    }

    private void EventLoop()
    {
        while (_running)
        {
            IntPtr p = MpvNative.WaitEvent(_ctx, 1.0);
            if (p == IntPtr.Zero) continue;

            MpvEvent ev;
            try { ev = Marshal.PtrToStructure<MpvEvent>(p); }
            catch { continue; }

            switch (ev.EventId)
            {
                case MpvEventId.Shutdown:
                    _running = false;
                    break;
                case MpvEventId.EndFile:
                    FileEnded?.Invoke(); // loop-file 已处理循环，这里仅通知
                    break;
                case MpvEventId.PropertyChange:
                    HandlePropertyChange(ev.Data);
                    break;
            }
        }
    }

    private void HandlePropertyChange(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero) return;
        MpvEventProperty prop;
        try { prop = Marshal.PtrToStructure<MpvEventProperty>(dataPtr); }
        catch { return; }

        string name = Marshal.PtrToStringUTF8(prop.Name) ?? string.Empty;

        if (prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero)
        {
            double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(prop.Data));
            if (name == "duration") Duration = val;
            else if (name == "time-pos") Position = val;
        }
    }

    public void Dispose()
    {
        if (!IsLoaded) return;
        _running = false;
        if (_ctx != IntPtr.Zero)
        {
            try
            {
                MpvNative.Wakeup(_ctx);
                _eventThread?.Join(2000);
                MpvNative.TerminateDestroy(_ctx);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"mpv 销毁异常：{ex.Message}");
            }
            _ctx = IntPtr.Zero;
        }
        IsLoaded = false;
    }
}
