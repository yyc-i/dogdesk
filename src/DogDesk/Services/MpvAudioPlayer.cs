using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DogDesk.Models;
using DogDesk.Services.Mpv;

namespace DogDesk.Services;

/// <summary>
/// 内置音频播放内核：独立 libmpv context（与壁纸视频播放器互不干扰），
/// 纯音频输出（vo=null），驱动本地曲库 / 在线音源 / Navidrome 播放。
/// 实现 <see cref="IMediaSession"/>，把播放状态暴露给媒体条/歌词，
/// 由 <see cref="MediaSessionRouter"/> 与系统 SMTC 会话做路由切换。
/// 不感知播放队列：eof/切歌请求通过事件抛回平台侧（MainWindow）。
/// </summary>
public sealed class MpvAudioPlayer : IMediaSession, IDisposable
{
    private IntPtr _ctx;
    private Thread? _eventThread;
    private volatile bool _running;
    private readonly Dispatcher _dispatcher;
    private ulong _observeSeq;

    private MediaItem? _current;
    private double _position;
    private double _duration;
    private bool _paused;
    private bool _loaded;
    private ImageSource? _thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>一首播放结束（eof），由平台侧决定自动续播/停止。</summary>
    public event Action? PlaybackEnded;

    /// <summary>用户点了「下一首」（平台侧转交给播放队列）。</summary>
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public event Action<string>? Log;

    /// <summary>libmpv 不可用时为 false，内置播放整体降级（媒体条/歌词继续跟随 SMTC）。</summary>
    public bool IsAvailable { get; private set; }

    public MpvAudioPlayer()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    // ================= IMediaSession =================

    public string Title => _current?.Title ?? "--";
    public string Artist => _current?.Artist ?? "";
    public ImageSource? Thumbnail => _thumbnail;
    public bool IsReal => true;
    public bool IsPlaying => IsAvailable && _loaded && !_paused;
    public double PositionSec => _position;
    public double DurationSec => _duration > 0 ? _duration : 1;
    public double Progress01 => _duration > 0 ? Math.Clamp(_position / _duration, 0, 1) : 0;

    // ================= 生命周期 =================

    /// <summary>惰性初始化：首次播放时才创建 mpv context（libmpv 缺失则标记不可用）。</summary>
    private void EnsureInit()
    {
        if (_ctx != IntPtr.Zero || IsAvailable) return;
        if (!MpvNativeLoader.IsLoaded)
        {
            Log?.Invoke("[内置播放] libmpv 未加载，内置音频播放不可用（媒体条/歌词仍跟随系统 SMTC）");
            return;
        }

        try
        {
            _ctx = MpvNative.Create();
            if (_ctx == IntPtr.Zero) return;

            // 纯音频：不创建视频输出，不弹窗
            SetOpt("vo", "null");
            SetOpt("mute", "no");
            SetOpt("volume", "100");
            SetOpt("idle", "yes");
            SetOpt("keep-open", "yes");
            SetOpt("input-default-bindings", "no");
            SetOpt("osc", "no");
            SetOpt("config", "no");
            SetOpt("terminal", "no");
            SetOpt("msg-level", "all=error");

            int err = MpvNative.Initialize(_ctx);
            if (err < 0) { Log?.Invoke($"[内置播放] mpv_initialize 失败：{err}"); return; }

            Observe("time-pos", MpvFormat.Double);
            Observe("duration", MpvFormat.Double);
            Observe("pause", MpvFormat.Flag);
            Observe("eof-reached", MpvFormat.Flag);

            _running = true;
            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-audio-event" };
            _eventThread.Start();
            IsAvailable = true;
            Log?.Invoke("[内置播放] libmpv 音频内核已就绪");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[内置播放] 初始化异常：{ex.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>应用均衡器+环绕组合 af 链（空串=旁路）。切歌后自动重放。</summary>
    public void ApplyEqualizer(string afChain)
    {
        _lastAf = afChain ?? "";
        EnsureInit();
        if (_ctx == IntPtr.Zero) return;
        try
        {
            int err = MpvNative.SetPropertyString(_ctx, "af", _lastAf);
            if (err < 0)
                Log?.Invoke($"[音效] af 链设置失败（已旁路，不影响播放）：{err}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[音效] af 链异常：{ex.Message}");
        }
    }

    /// <summary>应用音效预设链（均衡器+环绕组合）。等价于 ApplyEqualizer。</summary>
    public void ApplyEffects(string afChain) => ApplyEqualizer(afChain);

    private string _lastAf = "";
    private double _speed = 1.0;

    /// <summary>播放倍速（0.5-2.0）。运行时设置 mpv speed 属性。</summary>
    public void SetSpeed(double speed)
    {
        _speed = Math.Clamp(speed, 0.5, 2.0);
        EnsureInit();
        if (_ctx == IntPtr.Zero) return;
        try
        {
            int err = MpvNative.SetPropertyDouble(_ctx, "speed", _speed);
            if (err < 0) Log?.Invoke($"[倍速] speed 设置失败：{err}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[倍速] speed 异常：{ex.Message}");
        }
    }

    /// <summary>加载并播放一条媒体（本地路径或可播放 URL）。</summary>
    public void Play(MediaItem item)
    {
        EnsureInit();
        if (!IsAvailable || !item.IsValid) return;

        _current = item;
        _position = 0;
        _duration = item.DurationSec > 0 ? item.DurationSec : 0;
        _paused = false;
        _loaded = false;
        _thumbnail = null;

        int err = MpvNative.Command(_ctx, new[] { "loadfile", item.Uri, "replace" });
        if (err < 0)
        {
            Log?.Invoke($"[内置播放] loadfile 失败：{item.Title}（{err}）");
            return;
        }

        // 切歌后重放音效链与倍速（空 af=旁路）
        try
        {
            if (!string.IsNullOrEmpty(_lastAf)) MpvNative.SetPropertyString(_ctx, "af", _lastAf);
            if (Math.Abs(_speed - 1.0) > 0.001) MpvNative.SetPropertyDouble(_ctx, "speed", _speed);
        }
        catch { }

        Log?.Invoke($"[内置播放] ▶ {item.Title} - {item.Artist} [{item.Provider}]");
        RaiseAll();

        _ = LoadCoverAsync(item);
    }

    /// <summary>停止当前播放（卸载文件，回到 idle）。</summary>
    public void Stop()
    {
        if (_ctx != IntPtr.Zero)
            MpvNative.Command(_ctx, new[] { "stop" });
        _current = null;
        _position = 0;
        _loaded = false;
        _thumbnail = null;
        _paused = false;
        RaiseAll();
    }

    private async Task LoadCoverAsync(MediaItem item)
    {
        ImageSource? img = await CoverLoader.LoadAsync(item.LocalCoverPath, item.CoverUrl);
        if (!ReferenceEquals(_current, item)) return; // 已切歌
        _thumbnail = img;
        Raise(nameof(Thumbnail)); // Raise 内部已按 dispatcher 封送
    }

    // ================= 播放控制（IMediaSession） =================

    public void TogglePlayPause()
    {
        if (!IsAvailable || !_loaded) { NextRequested?.Invoke(); return; }
        SetPause(!_paused);
    }

    public void Next() => NextRequested?.Invoke();
    public void Previous() => PreviousRequested?.Invoke();

    public void Seek(double fraction)
    {
        if (!IsAvailable || _duration <= 0) return;
        double target = Math.Clamp(fraction, 0, 1) * _duration;
        MpvNative.Command(_ctx, new[] { "seek", target.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), "absolute" });
    }

    private void SetPause(bool paused)
    {
        MpvNative.SetPropertyString(_ctx, "pause", paused ? "yes" : "no");
    }

    // ================= mpv 事件循环 =================

    private void EventLoop()
    {
        while (_running)
        {
            IntPtr p = MpvNative.WaitEvent(_ctx, 1.0);
            if (p == IntPtr.Zero) continue;
            MpvEvent ev;
            try { ev = Marshal.PtrToStructure<MpvEvent>(p); } catch { continue; }

            switch (ev.EventId)
            {
                case MpvEventId.Shutdown:
                    _running = false;
                    break;
                case MpvEventId.FileLoaded:
                    OnFileLoaded();
                    break;
                case MpvEventId.EndFile:
                    // eof：通知平台侧续播（keep-open 下暂停在末尾）
                    _dispatcher.BeginInvoke(() => PlaybackEnded?.Invoke());
                    break;
                case MpvEventId.PropertyChange:
                    HandleProperty(ev.Data);
                    break;
            }
        }
    }

    private void OnFileLoaded()
    {
        _loaded = true;
        _dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(IsPlaying));
            Raise(nameof(Progress01));
        });
    }

    private void HandleProperty(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero) return;
        MpvEventProperty prop;
        try { prop = Marshal.PtrToStructure<MpvEventProperty>(dataPtr); } catch { return; }

        string name = Marshal.PtrToStringUTF8(prop.Name) ?? "";
        bool ui = false;

        if (prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero)
        {
            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(prop.Data));
            if (name == "time-pos") { _position = v; ui = true; }
            else if (name == "duration") { _duration = v; ui = true; }
        }
        else if (prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero)
        {
            int f = Marshal.ReadInt32(prop.Data);
            if (name == "pause") { _paused = f != 0; ui = true; }
        }

        if (ui)
            _dispatcher.BeginInvoke(() =>
            {
                Raise(nameof(IsPlaying));
                Raise(nameof(PositionSec));
                Raise(nameof(DurationSec));
                Raise(nameof(Progress01));
            });
    }

    private void Observe(string name, int format)
        => MpvNative.ObserveProperty(_ctx, ++_observeSeq, name, format);

    private void SetOpt(string name, string value)
    {
        int err = MpvNative.SetOptionString(_ctx, name, value);
        if (err < 0) Log?.Invoke($"[内置播放] 选项 {name}={value} 设置失败：{err}");
    }

    private void RaiseAll()
    {
        foreach (var n in new[] { nameof(Title), nameof(Artist), nameof(Thumbnail),
                   nameof(IsPlaying), nameof(PositionSec), nameof(DurationSec),
                   nameof(Progress01), nameof(IsReal) })
            Raise(n);
    }

    private void Raise(string name)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => Raise(name)); return; }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _running = false;
        if (_ctx != IntPtr.Zero)
        {
            try
            {
                MpvNative.Wakeup(_ctx);
                _eventThread?.Join(1500);
                MpvNative.TerminateDestroy(_ctx);
            }
            catch (Exception ex) { Log?.Invoke($"[内置播放] 销毁异常：{ex.Message}"); }
            _ctx = IntPtr.Zero;
        }
        IsAvailable = false;
    }
}
