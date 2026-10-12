using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 歌词服务：按曲目信息多源检索（网易云 → QQ音乐 → LRCLIB），
/// 用 ParseHelper 解析为统一 <see cref="LyricsData"/>（自动识别逐字/逐行）。
/// </summary>
public sealed class LyricsService
{
    public event Action<string>? Log;

    public LyricsData? Data { get; private set; }
    public bool HasLyrics => Data?.Lines is { Count: > 0 };

    private string? _lastKey;

    /// <summary>检索歌词；同一首歌不重复请求。成功返回 true。</summary>
    public async Task<bool> TryFetchAsync(string title, string artist, double durationSec = 0)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        string key = $"{title.Trim()}|{artist.Trim()}";
        if (key == _lastKey) return HasLyrics;

        var track = new TrackMetadata
        {
            Title = title.Trim(),
            Artist = string.IsNullOrWhiteSpace(artist) ? "" : artist.Trim(),
        };
        if (durationSec > 5) track.DurationMs = (int?)(durationSec * 1000);

        (string? raw, string source) = await TryNetease(track);
        if (raw is null) (raw, source) = await TryQQMusic(track);
        if (raw is null) (raw, source) = await TryLrclib(track);

        if (raw is null)
        {
            Data = null;
            _lastKey = key;
            Log?.Invoke($"[歌词] 未找到「{title} - {artist}」的歌词");
            return false;
        }

        try
        {
            Data = ParseHelper.ParseLyrics(raw);
        }
        catch (Exception ex)
        {
            Data = null;
            _lastKey = key;
            Log?.Invoke($"[歌词] 解析失败：{ex.Message}");
            return false;
        }

        _lastKey = key;
        if (Data is null)
        {
            Log?.Invoke("[歌词] 解析返回空结果");
            return false;
        }
        int lineCount = Data.Lines?.Count ?? 0;
        Log?.Invoke($"[歌词] 已获取（{source}，{lineCount} 行，同步类型 {Data.File?.SyncTypes}）");
        return true;
    }

    private async Task<(string?, string)> TryNetease(TrackMetadata track)
    {
        try
        {
            var r = await SearchHelper.Search(track, Searchers.Netease);
            if (!IsGoodMatch(r)) return (null, "");

            string? id = null;
            if (r is not null) { dynamic d = r; id = d.Id; }
            if (string.IsNullOrWhiteSpace(id)) return (null, "");

            var lr = await ProviderHelper.NeteaseApi.GetLyric(id);
            if (lr is null || lr.Nolyric || lr.Uncollected) return (null, "");

            // 优先逐字 Yrc，否则逐行 Lrc
            string? raw = lr.Yrc?.Lyric;
            if (string.IsNullOrWhiteSpace(raw)) raw = lr.Lrc?.Lyric;
            return (string.IsNullOrWhiteSpace(raw) ? null : raw, "网易云");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[歌词] 网易源异常：{ex.Message}");
            return (null, "");
        }
    }

    private async Task<(string?, string)> TryQQMusic(TrackMetadata track)
    {
        try
        {
            var r = await SearchHelper.Search(track, Searchers.QQMusic);
            if (!IsGoodMatch(r)) return (null, "");

            string? mid = null;
            if (r is not null) { dynamic d = r; mid = d.Mid; }
            if (string.IsNullOrWhiteSpace(mid)) return (null, "");

            var lr = await ProviderHelper.QQMusicApi.GetLyric(mid);
            if (lr is null || lr.Code != 0 || string.IsNullOrWhiteSpace(lr.Lyric))
                return (null, "");
            return (lr.Lyric, "QQ音乐");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[歌词] QQ源异常：{ex.Message}");
            return (null, "");
        }
    }

    private async Task<(string?, string)> TryLrclib(TrackMetadata track)
    {
        try
        {
            int? dur = track.DurationMs is null ? null : (int)(track.DurationMs / 1000);
            string title = track.Title ?? "";
            string artist = track.Artist ?? "";
            string album = track.Album ?? "";

            var exact = await ProviderHelper.LRCLIBApi.Get(title, artist, album, dur);
            if (exact is not null)
                return FromLrclib(exact.Instrumental, exact.SyncedLyrics, exact.PlainLyrics);

            var results = await ProviderHelper.LRCLIBApi.Search(title, artist, album, dur);
            var s = results?.FirstOrDefault();
            if (s is not null)
                return FromLrclib(s.Instrumental, s.SyncedLyrics, s.PlainLyrics);

            return (null, "");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[歌词] LRCLIB源异常：{ex.Message}");
            return (null, "");
        }
    }

    private static (string?, string) FromLrclib(bool instrumental, string? synced, string? plain)
    {
        if (instrumental) return (null, "");
        string raw = synced ?? plain ?? "";
        return (string.IsNullOrWhiteSpace(raw) ? null : raw, "LRCLIB");
    }

    /// <summary>匹配度阈值过滤（低于 Low=30 视为错误匹配）。</summary>
    private static bool IsGoodMatch(ISearchResult? r)
    {
        if (r is null) return false;
        if (!r.MatchType.HasValue) return true; // 无评分时放行
        int score = Convert.ToInt32(r.MatchType.Value);
        return score >= 30;
    }
}
