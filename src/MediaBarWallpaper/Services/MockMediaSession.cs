using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 模拟媒体会话：用定时器推进进度、轮换演示曲目。
/// 用于在未接入真实系统媒体会话前验证媒体条 UI 与交互闭环。
/// </summary>
public sealed class MockMediaSession : IMediaSession
{
    private static readonly (string Title, string Artist)[] Tracks =
    {
        ("City of Stars", "Ryan Gosling & Emma Stone"),
        ("夜空中最亮的星", "逃跑计划"),
        ("Shape of You", "Ed Sheeran"),
        ("晴天", "周杰伦"),
        ("Blinding Lights", "The Weeknd"),
    };

    private const double TrackDurationSec = 210.0;
    private const double TickSec = 0.2;

    private readonly DispatcherTimer _timer;
    private int _trackIndex;
    private double _progress; // 0..100
    private bool _isPlaying = true;

    public MockMediaSession()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TickSec) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public string Title => Tracks[_trackIndex].Title;
    public string Artist => Tracks[_trackIndex].Artist;

    public ImageSource? Thumbnail => null;

    public bool IsReal => false;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged(nameof(IsPlaying));
        }
    }

    public double Progress01 => _progress / 100.0;

    public double PositionSec => Progress01 * TrackDurationSec;

    public double DurationSec => TrackDurationSec;

    public void TogglePlayPause() => IsPlaying = !IsPlaying;

    public void Next()
    {
        _trackIndex = (_trackIndex + 1) % Tracks.Length;
        _progress = 0;
        RaiseTrackChanged();
    }

    public void Previous()
    {
        _trackIndex = (_trackIndex - 1 + Tracks.Length) % Tracks.Length;
        _progress = 0;
        RaiseTrackChanged();
    }

    public void Seek(double fraction)
    {
        _progress = Math.Clamp(fraction, 0, 1) * 100.0;
        OnPropertyChanged(nameof(Progress01));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) return;
        _progress += 100.0 * TickSec / TrackDurationSec;
        if (_progress >= 100.0)
        {
            Next();
            return;
        }
        OnPropertyChanged(nameof(Progress01));
    }

    private void RaiseTrackChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Progress01));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
