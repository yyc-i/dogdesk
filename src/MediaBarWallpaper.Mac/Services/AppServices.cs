using MediaBarWallpaper.Mac.Services.Mpv;
using MediaBarWallpaper.Models;
using MediaBarWallpaper.Services;
using MediaBarWallpaper.Services.Equalizer;
using MediaBarWallpaper.Services.MusicSource;
using MediaBarWallpaper.Services.Playback;

namespace MediaBarWallpaper.Mac.Services;

/// <summary>
/// 应用协调层（组合根）：把 <see cref="MpvAudioPlayer"/>、<see cref="PlaybackQueue"/>、
/// <see cref="LyricsService"/>、在线音源与均衡器串成可播放的整体。
/// UI 只与本类交互，不直接操作 mpv。
/// </summary>
public sealed class AppServices
{
    public SettingsService Settings { get; } = new();
    public MpvAudioPlayer Player { get; }
    public PlaybackQueue Queue { get; } = new();
    public LyricsService Lyrics { get; } = new();
    public NeteaseMusicSource Netease { get; } = new();
    public NavidromeClient Navidrome { get; } = new();

    /// <summary>播放状态变化（mpv 属性 / 队列切换 / 封面就绪），UI 刷新入口。</summary>
    public event Action? StateChanged;

    /// <summary>日志行（设置页/调试用）。</summary>
    public event Action<string>? Log;

    public AppServices()
    {
        Player = new MpvAudioPlayer();
        Player.Changed += () => StateChanged?.Invoke();
        Player.Ended += OnPlayerEnded;

        Queue.CurrentChanged += OnQueueCurrentChanged;

        Lyrics.Log += msg => Log?.Invoke(msg);

        // 还原队列循环模式
        Queue.Repeat = RepeatMode.Off;

        // 凭据回填 Navidrome
        if (!string.IsNullOrWhiteSpace(Settings.Current.NavidromeServer))
            Navidrome.Configure(
                Settings.Current.NavidromeServer,
                Settings.Current.NavidromeUser,
                Settings.Current.NavidromePassword);

        ApplyEqualizer();
    }

    public bool MpvAvailable => Player.Available;
    public string MpvStatus => Player.Status;

    /// <summary>用一组曲目加载队列并从 index 开始播放。</summary>
    public void PlayList(IEnumerable<MediaItem> items, int index)
    {
        Queue.Load(items, index);
    }

    /// <summary>播放队列中指定索引（双击列表项时调用）。</summary>
    public void PlayAt(int index) => Queue.PlayAt(index);

    public void ToggleRepeat()
    {
        Queue.Repeat = Queue.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        StateChanged?.Invoke();
    }

    public void TogglePlayPause() => Player.TogglePlayPause();
    public void Seek(double fraction) => Player.Seek(fraction);

    public void Next()
    {
        var next = Queue.MoveNext();
        if (next is not null) Queue.PlayAt(IndexOf(next));
    }

    public void Previous()
    {
        var prev = Queue.MovePrevious();
        if (prev is not null) Queue.PlayAt(IndexOf(prev));
    }

    /// <summary>根据当前设置重建并应用均衡器链。</summary>
    public void ApplyEqualizer()
    {
        var preset = EqualizerPreset.From(Settings.Current.EqualizerGains);
        string chain = Settings.Current.EqualizerEnabled ? EqualizerAf.BuildChain(preset) : EqualizerAf.Bypass;
        Player.ApplyEqualizerChain(chain);
    }

    /// <summary>清空队列并停止。</summary>
    public void Stop() => Queue.Clear();

    private void OnQueueCurrentChanged(MediaItem? item)
    {
        if (item is null)
        {
            Player.SetTrackInfo("", "");
            Player.ThumbnailBytes = null;
            StateChanged?.Invoke();
            return;
        }

        Player.SetTrackInfo(item.Title, item.Artist);
        Player.Load(item.Uri);
        Player.Play();

        StateChanged?.Invoke();

        // 封面与歌词异步加载（不阻塞 UI）
        _ = LoadCoverAsync(item);
        _ = LoadLyricsAsync(item);
    }

    private async Task LoadCoverAsync(MediaItem item)
    {
        byte[]? cover = await CoverLoader.FetchBytesAsync(item).ConfigureAwait(true);
        if (Queue.Current == item)
        {
            Player.ThumbnailBytes = cover;
            StateChanged?.Invoke();
        }
    }

    private async Task LoadLyricsAsync(MediaItem item)
    {
        try
        {
            await Lyrics.TryFetchAsync(item.Title, item.Artist, item.DurationSec).ConfigureAwait(true);
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[歌词] 异常：{ex.Message}");
        }
    }

    private void OnPlayerEnded()
    {
        // 在 mpv 事件线程触发；切歌动作回到队列逻辑
        if (Queue.TryAutoAdvance(out MediaItem? next) && next is not null)
        {
            // 直接走队列切换
            Queue.PlayAt(IndexOf(next));
        }
    }

    private int IndexOf(MediaItem item)
    {
        var items = Queue.Items;
        for (int i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }

    public void Dispose() => Player.Dispose();
}
