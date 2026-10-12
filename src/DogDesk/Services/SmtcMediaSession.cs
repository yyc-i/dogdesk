using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DogDesk.Services;

/// <summary>
/// 真实媒体会话：接入 Windows 系统媒体会话（SMTC，Win10 1809+ 可用），
/// 读取正在播放的曲目、封面、播放状态与时间线，并支持播放控制。
/// 未检测到播放器会话时自动回退到内置演示数据，保证媒体条始终有内容。
/// 注意：WinRT 事件在任意线程触发，本类统一封送到创建线程（UI）后再广播属性变化。
/// </summary>
public sealed class SmtcMediaSession : IMediaSession
{
    private static readonly (string Title, string Artist)[] FallbackTracks =
    {
        ("City of Stars", "Ryan Gosling & Emma Stone"),
        ("夜空中最亮的星", "逃跑计划"),
        ("Shape of You", "Ed Sheeran"),
        ("晴天", "周杰伦"),
        ("Blinding Lights", "The Weeknd"),
    };

    private const double FallbackDurationSec = 210.0;
    private const int MaxThumbnailBytes = 15_000_000;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _ticker;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private GlobalSystemMediaTransportControlsSessionPlaybackInfo? _playback;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? _mediaProps;
    private ImageSource? _thumbnail;

    // 回退演示数据
    private int _fallbackIndex;
    private double _fallbackProgress;
    private bool _fallbackPlaying = true;

    public SmtcMediaSession()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    /// <summary>初始化 SMTC 管理器。失败（早期系统/无音频会话服务）时保持演示模式，不抛出。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) =>
                _dispatcher.BeginInvoke(() => Attach(_manager?.GetCurrentSession()));
            Attach(_manager.GetCurrentSession());
        }
        catch
        {
            // 环境不支持 SMTC：继续使用演示数据
        }
    }

    /// <summary>是否已获得真实媒体会话。</summary>
    public bool HasRealSession => _session is not null;

    public bool IsReal => _session is not null;

    public string Title => _session is not null ? (_mediaProps?.Title ?? "--") : FallbackTracks[_fallbackIndex].Title;

    public string Artist => _session is not null ? (_mediaProps?.Artist ?? "--") : FallbackTracks[_fallbackIndex].Artist;

    public ImageSource? Thumbnail => _thumbnail;

    public bool IsPlaying
    {
        get
        {
            if (_session is null) return _fallbackPlaying;
            try
            {
                return _playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch { return false; }
        }
    }

    public double Progress01
    {
        get
        {
            if (_session is null) return Math.Clamp(_fallbackProgress / 100.0, 0, 1);
            try
            {
                var t = _session.GetTimelineProperties();
                double total = Math.Max(1, t.EndTime.TotalSeconds - t.StartTime.TotalSeconds);
                double pos = Math.Max(0, t.Position.TotalSeconds - t.StartTime.TotalSeconds);
                return Math.Clamp(pos / total, 0, 1);
            }
            catch { return 0; }
        }
    }

    public double PositionSec
    {
        get
        {
            if (_session is null)
                return Math.Clamp(_fallbackProgress / 100.0, 0, 1) * FallbackDurationSec;
            try
            {
                var t = _session.GetTimelineProperties();
                return Math.Max(0, t.Position.TotalSeconds - t.StartTime.TotalSeconds);
            }
            catch { return 0; }
        }
    }

    public double DurationSec
    {
        get
        {
            if (_session is null) return FallbackDurationSec;
            try
            {
                var t = _session.GetTimelineProperties();
                double total = t.EndTime.TotalSeconds - t.StartTime.TotalSeconds;
                return total > 1 ? total : FallbackDurationSec;
            }
            catch { return FallbackDurationSec; }
        }
    }

    // ---- 播放控制 ----

    public async void TogglePlayPause()
    {
        if (_session is null)
        {
            _fallbackPlaying = !_fallbackPlaying;
            Raise(nameof(IsPlaying));
            return;
        }
        try
        {
            if (IsPlaying) await _session.TryPauseAsync();
            else await _session.TryPlayAsync();
        }
        catch { }
    }

    public async void Next()
    {
        if (_session is null)
        {
            AdvanceFallback(1);
            return;
        }
        try { await _session.TrySkipNextAsync(); } catch { }
    }

    public async void Previous()
    {
        if (_session is null)
        {
            AdvanceFallback(-1);
            return;
        }
        try { await _session.TrySkipPreviousAsync(); } catch { }
    }

    public async void Seek(double fraction)
    {
        if (_session is null)
        {
            _fallbackProgress = Math.Clamp(fraction, 0, 1) * 100.0;
            Raise(nameof(Progress01));
            return;
        }
        try
        {
            double total = DurationSec;
            long ms = (long)(Math.Clamp(fraction, 0, 1) * total * 1000);
            await _session.TryChangePlaybackPositionAsync(ms);
        }
        catch { }
    }

    // ---- 会话挂接 ----

    private void Attach(GlobalSystemMediaTransportControlsSession? s)
    {
        if (_session == s) return;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = s;
        _playback = null;
        _mediaProps = null;
        _thumbnail = null;

        if (s is not null)
        {
            s.MediaPropertiesChanged += OnMediaPropertiesChanged;
            s.PlaybackInfoChanged += OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            try { _playback = s.GetPlaybackInfo(); } catch { }
        }

        _ = LoadMediaPropertiesAsync();

        Raise(nameof(Title));
        Raise(nameof(Artist));
        Raise(nameof(Thumbnail));
        Raise(nameof(IsPlaying));
        Raise(nameof(IsReal));
        Raise(nameof(Progress01));
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) =>
        _dispatcher.BeginInvoke(async () => await LoadMediaPropertiesAsync());

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) =>
        _dispatcher.BeginInvoke(() =>
        {
            try { _playback = s.GetPlaybackInfo(); } catch { }
            Raise(nameof(IsPlaying));
        });

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) =>
        _dispatcher.BeginInvoke(() => Raise(nameof(Progress01)));

    private async Task LoadMediaPropertiesAsync()
    {
        if (_session is null) return;
        try
        {
            _mediaProps = await _session.TryGetMediaPropertiesAsync();
            _thumbnail = await DecodeThumbnailAsync(_mediaProps?.Thumbnail);
            Raise(nameof(Title));
            Raise(nameof(Artist));
            Raise(nameof(Thumbnail));
        }
        catch { }
    }

    // ---- 演示数据 ----

    private void OnTick(object? sender, EventArgs e)
    {
        if (_session is null)
        {
            if (_fallbackPlaying)
            {
                _fallbackProgress += 100.0 * 0.4 / FallbackDurationSec;
                if (_fallbackProgress >= 100.0)
                {
                    AdvanceFallback(1);
                    return;
                }
            }
            Raise(nameof(Progress01));
            return;
        }
        Raise(nameof(Progress01)); // 真实会话：定时拉取时间线，保持进度平滑
    }

    private void AdvanceFallback(int delta)
    {
        _fallbackIndex = (_fallbackIndex + delta + FallbackTracks.Length) % FallbackTracks.Length;
        _fallbackProgress = 0;
        Raise(nameof(Title));
        Raise(nameof(Artist));
        Raise(nameof(Progress01));
    }

    // ---- 封面解码 ----

    private static async Task<ImageSource?> DecodeThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using IRandomAccessStream stream = await reference.OpenReadAsync();
            ulong size = stream.Size;
            if (size == 0 || size > MaxThumbnailBytes) return null;

            DataReader reader = new(stream.GetInputStreamAt(0));
            byte[] bytes;
            try
            {
                await reader.LoadAsync((uint)size);
                bytes = new byte[(int)size];
                reader.ReadBytes(bytes);
            }
            finally
            {
                reader.Dispose();
            }

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ---- 事件 ----

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
