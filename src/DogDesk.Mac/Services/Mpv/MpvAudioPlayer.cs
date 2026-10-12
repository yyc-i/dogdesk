using System.Runtime.InteropServices;
using DogDesk.Models;

namespace DogDesk.Mac.Services.Mpv;

/// <summary>
/// 基于系统 libmpv.dylib 的音频播放内核，实现 <see cref="IMediaSession"/>。
/// 线程模式参考 Windows 侧 MpvVideoPlayer：后台线程 WaitEvent 事件循环，
/// 观察 time-pos / duration / pause / media-title；音频走 mpv 默认 ao（macOS coreaudio）。
/// </summary>
public sealed class MpvAudioPlayer : IMediaSession
{
    private IntPtr _ctx;
    private Thread? _eventThread;
    private volatile bool _running;
    private ulong _observeSeq;

    private double _positionSec;
    private double _durationSec;
    private bool _paused = true;
    private string _mediaTitle = "";
    private byte[]? _thumb;

    /// <summary>libmpv 不可用时为 false（UI 据此禁用播放）。</summary>
    public bool Available { get; }
    public string Status { get; }

    /// <summary>当前文件播放结束（END_FILE）时触发。</summary>
    public event Action? Ended;

    public event Action? Changed;

    public MpvAudioPlayer()
    {
        if (!MpvLib.TryLoad(out string msg))
        {
            Available = false;
            Status = msg;
            return;
        }
        Status = msg;

        _ctx = MpvLib.Create();
        if (_ctx == IntPtr.Zero)
        {
            Available = false;
            Status = "mpv_create 返回空句柄";
            return;
        }

        // 纯音频播放配置
        SetOpt("vid", "no");          // 不渲染视频（音频应用）
        SetOpt("sid", "no");          // 关闭字幕
        SetOpt("input-default-bindings", "no");
        SetOpt("input-vo-keyboard", "no");
        SetOpt("osd-level", "0");
        SetOpt("config", "no");
        SetOpt("terminal", "no");
        SetOpt("msg-level", "all=error");
        SetOpt("idle", "yes");        // 无文件时进入 idle，保持实例存活
        SetOpt("keep-open", "yes");   // 播完不自动退出

        int err = MpvLib.Initialize(_ctx);
        if (err < 0)
        {
            Available = false;
            Status = $"mpv_initialize 失败：{ErrorText(err)}";
            MpvLib.TerminateDestroy(_ctx);
            _ctx = IntPtr.Zero;
            return;
        }

        Available = true;
        Observe("time-pos", MpvLib.FormatDouble);
        Observe("duration", MpvLib.FormatDouble);
        Observe("pause", MpvLib.FormatFlag);
        Observe("media-title", MpvLib.FormatString);

        _running = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-event" };
        _eventThread.Start();
    }

    // ---- IMediaSession ----
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public byte[]? ThumbnailBytes
    {
        get => _thumb;
        set { _thumb = value; Changed?.Invoke(); }
    }
    public bool IsPlaying => Available && !_paused;
    public double Progress01 => DurationSec > 0 ? Math.Clamp(PositionSec / DurationSec, 0, 1) : 0;
    public double PositionSec => _positionSec;
    public double DurationSec => _durationSec;

    public void SetTrackInfo(string title, string artist)
    {
        Title = title;
        Artist = artist;
        Changed?.Invoke();
    }

    /// <summary>加载并开始播放一个本地路径或可播放 URL。</summary>
    public void Load(string url)
    {
        if (!Available) return;
        int err = CommandWithArgs("loadfile", url, "replace");
        if (err < 0)
            System.Diagnostics.Debug.WriteLine($"[mpv] loadfile 失败：{ErrorText(err)}");
    }

    public void TogglePlayPause()
    {
        if (!Available) return;
        MpvLib.SetPropertyString(_ctx, "pause", _paused ? "no" : "yes");
    }

    public void Play()
    {
        if (Available && _paused)
            MpvLib.SetPropertyString(_ctx, "pause", "no");
    }

    public void Seek(double fraction)
    {
        if (!Available || _durationSec <= 0) return;
        double t = Math.Clamp(fraction, 0, 1) * _durationSec;
        CommandWithArgs("seek", t.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture), "absolute");
    }

    public void Next() { /* 由协调层决定队列切歌 */ }
    public void Previous() { /* 由协调层决定队列切歌 */ }

    /// <summary>应用均衡器/环绕等音频滤镜链（运行时 set_property af）。空串 = 旁路。</summary>
    public void ApplyEqualizerChain(string chain)
    {
        if (!Available) return;
        MpvLib.SetPropertyString(_ctx, "af", string.IsNullOrEmpty(chain) ? "[]" : chain);
    }

    /// <summary>通用音频滤镜链设置（v0.6：环绕 + 均衡器合并链）。</summary>
    public void ApplyAf(string chain) => ApplyEqualizerChain(chain);

    /// <summary>设置播放倍速（mpv speed 属性，0.5 .. 2.0）。</summary>
    public void SetSpeed(double speed)
    {
        if (!Available) return;
        double v = Math.Clamp(speed, 0.5, 2.0);
        MpvLib.SetPropertyString(_ctx, "speed", v.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- 内部 ----
    private void Observe(string name, int format)
        => MpvLib.ObserveProperty(_ctx, ++_observeSeq, name, format);

    private void SetOpt(string name, string value)
        => MpvLib.SetOptionString(_ctx, name, value);

    /// <summary>构造 UTF-8 参数字符串数组、调用 mpv_command 并释放临时内存。</summary>
    private int CommandWithArgs(params string[] args)
    {
        IntPtr[] strPtrs = new IntPtr[args.Length + 1];
        for (int i = 0; i < args.Length; i++)
            strPtrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
        strPtrs[args.Length] = IntPtr.Zero;

        IntPtr arr = Marshal.AllocHGlobal(IntPtr.Size * strPtrs.Length);
        for (int i = 0; i < strPtrs.Length; i++)
            Marshal.WriteIntPtr(arr, i * IntPtr.Size, strPtrs[i]);

        try
        {
            return MpvLib.Command(_ctx, arr);
        }
        finally
        {
            Marshal.FreeHGlobal(arr);
            for (int i = 0; i < args.Length; i++)
                Marshal.FreeCoTaskMem(strPtrs[i]);
        }
    }

    private void EventLoop()
    {
        while (_running)
        {
            IntPtr p = MpvLib.WaitEvent(_ctx, 1.0);
            if (p == IntPtr.Zero) continue;

            MpvLib.MpvEvent ev;
            try { ev = Marshal.PtrToStructure<MpvLib.MpvEvent>(p); }
            catch { continue; }

            switch (ev.EventId)
            {
                case MpvLib.EventShutdown:
                    _running = false;
                    break;
                case MpvLib.EventEndFile:
                    // 播完（非 keep-open 暂停的尾帧）
                    Ended?.Invoke();
                    break;
                case MpvLib.EventPropertyChange:
                    HandlePropertyChange(ev.Data);
                    break;
            }
        }
    }

    private void HandlePropertyChange(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero) return;
        MpvLib.MpvEventProperty prop;
        try { prop = Marshal.PtrToStructure<MpvLib.MpvEventProperty>(dataPtr); }
        catch { return; }

        string name = Marshal.PtrToStringUTF8(prop.Name) ?? string.Empty;

        if (prop.Format == MpvLib.FormatDouble && prop.Data != IntPtr.Zero)
        {
            double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(prop.Data));
            if (name == "time-pos") _positionSec = val;
            else if (name == "duration") _durationSec = val;
            Changed?.Invoke();
        }
        else if (prop.Format == MpvLib.FormatFlag && prop.Data != IntPtr.Zero)
        {
            bool val = Marshal.ReadInt32(prop.Data) != 0;
            if (name == "pause") { _paused = val; Changed?.Invoke(); }
        }
        else if (prop.Format == MpvLib.FormatString && prop.Data != IntPtr.Zero)
        {
            // prop.Data 是 char**（指向 mpv 分配的字符串指针）
            IntPtr strPtr = Marshal.ReadIntPtr(prop.Data);
            string? s = Marshal.PtrToStringUTF8(strPtr);
            if (name == "media-title" && s is not null) { _mediaTitle = s; Changed?.Invoke(); }
        }
    }

    private string ErrorText(int err)
    {
        IntPtr p = MpvLib.ErrorString(err);
        return p == IntPtr.Zero ? err.ToString() : (Marshal.PtrToStringUTF8(p) ?? err.ToString());
    }

    public void Dispose()
    {
        _running = false;
        if (_ctx != IntPtr.Zero)
        {
            try
            {
                MpvLib.Wakeup(_ctx);
                _eventThread?.Join(2000);
                MpvLib.TerminateDestroy(_ctx);
            }
            catch { /* 忽略销毁异常 */ }
            _ctx = IntPtr.Zero;
        }
    }
}
