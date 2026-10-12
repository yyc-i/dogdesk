using DogDesk.Models;

namespace DogDesk.Services.MusicSource;

/// <summary>
/// 在线音源抽象。实现方可以是第三方非官方接口（标注 <see cref="IsOfficial"/> = false），
/// 界面必须对非官方源展示"第三方接口仅供学习、稳定性不保证"的风险提示。
/// </summary>
public interface IMusicSource
{
    /// <summary>来源显示名（如"网易云"）。</summary>
    string Name { get; }

    /// <summary>是否官方接口；false 时 UI 需标注风险。</summary>
    bool IsOfficial { get; }

    /// <summary>按关键词搜索，返回可播放条目（可能为空列表，不抛异常）。</summary>
    Task<IReadOnlyList<MediaItem>> SearchAsync(string keyword, CancellationToken ct = default);
}

/// <summary>
/// 网易云在线音源（非官方网页接口）。
/// 说明：本实现走 music.163.com 公开网页搜索接口与"外链播放"地址，属第三方非官方接口，
/// 仅供学习交流；接口随时可能失效/受版权风控，播放失败时 UI 应给出友好提示。
/// </summary>
public sealed class NeteaseMusicSource : IMusicSource
{
    public string Name => "网易云";
    public bool IsOfficial => false;

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        h.Timeout = TimeSpan.FromSeconds(15);
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        h.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        return h;
    }

    /// <summary>把歌曲 id 转为外链播放地址（mpv 可直接播放；该接口可能失效）。</summary>
    public static string BuildPlayUrl(string songId) =>
        $"https://music.163.com/song/media/outer/url?id={songId}.mp3";

    public async Task<IReadOnlyList<MediaItem>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        var result = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(keyword)) return result;
        try
        {
            string url = "https://music.163.com/api/search/get/web?type=1&limit=30&offset=0&s=" +
                         Uri.EscapeDataString(keyword.Trim());
            using var resp = await Http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            var songs = doc.RootElement.TryGetProperty("result", out var r)
                && r.TryGetProperty("songs", out var songsEl)
                    ? songsEl : default;

            if (songs.ValueKind != System.Text.Json.JsonValueKind.Array) return result;

            foreach (var s in songs.EnumerateArray())
            {
                var item = SongToItem(s);
                if (item is not null) result.Add(item);
            }
        }
        catch (Exception ex)
        {
            // 非官方接口：任何异常都降级为空结果，由 UI 提示"在线音源暂不可用"
            System.Diagnostics.Debug.WriteLine($"[NeteaseMusicSource] {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 获取网易云歌单（排行榜）曲目（非官方接口，仅供学习）。
    /// 榜单 id 见 <see cref="ChartsCatalog.NeteaseCharts"/>。
    /// </summary>
    public async Task<IReadOnlyList<MediaItem>> GetChartAsync(string playlistId, int count = 30, CancellationToken ct = default)
    {
        var result = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(playlistId)) return result;
        try
        {
            string url = $"https://music.163.com/api/v3/playlist/detail?id={Uri.EscapeDataString(playlistId)}";
            using var resp = await Http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            if (!doc.RootElement.TryGetProperty("playlist", out var pl) ||
                !pl.TryGetProperty("tracks", out var tracks) ||
                tracks.ValueKind != System.Text.Json.JsonValueKind.Array)
                return result;

            foreach (var s in tracks.EnumerateArray())
            {
                var item = SongToItem(s);
                if (item is not null) result.Add(item);
                if (result.Count >= count) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NeteaseMusicSource.Chart] {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 统一的歌曲 JSON → MediaItem 映射：兼容搜索接口（artists/album/duration）
    /// 与歌单接口（ar/al/dt）两种字段形状。
    /// </summary>
    private static MediaItem? SongToItem(System.Text.Json.JsonElement s)
    {
        if (!s.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.Number)
            return null;
        string id = idEl.GetInt64().ToString();
        string title = GetString(s, "name") ?? "未知歌曲";
        string artist = TryGetArtists(s);
        string? album = s.TryGetProperty("album", out var al) ? GetString(al, "name") : GetString(s, "al", "name");
        string? pic = GetString(s, "album", "picUrl") ?? GetString(s, "al", "picUrl");
        double durationMs = s.TryGetProperty("duration", out var dur) ? dur.GetDouble()
            : s.TryGetProperty("dt", out var dt) ? dt.GetDouble() : 0;

        return new MediaItem
        {
            Title = title,
            Artist = artist,
            Album = album,
            DurationSec = durationMs > 0 ? durationMs / 1000.0 : 0,
            Kind = MediaSourceKind.Online,
            Uri = BuildPlayUrl(id),
            CoverUrl = string.IsNullOrWhiteSpace(pic) ? null : pic,
            Provider = "网易云",
        };
    }

    private static string? GetString(System.Text.Json.JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static string? GetString(System.Text.Json.JsonElement el, string outer, string inner)
        => el.TryGetProperty(outer, out var o) ? GetString(o, inner) : null;

    private static string TryGetArtists(System.Text.Json.JsonElement s)
    {
        // 兼容搜索接口 artists[] 与歌单接口 ar[]
        var arr = s.TryGetProperty("artists", out var a1) && a1.ValueKind == System.Text.Json.JsonValueKind.Array ? a1
            : s.TryGetProperty("ar", out var a2) && a2.ValueKind == System.Text.Json.JsonValueKind.Array ? a2
            : default;
        if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) return "";
        var names = arr.EnumerateArray()
            .Select(a => GetString(a, "name"))
            .Where(n => !string.IsNullOrWhiteSpace(n));
        return string.Join(" / ", names);
    }
}

/// <summary>网易云内置排行榜（非官方接口，仅供学习）。</summary>
public static class ChartsCatalog
{
    public static readonly (string Id, string Name)[] NeteaseCharts =
    {
        ("19723756", "云音乐飙升榜"),
        ("3778678", "云音乐热歌榜"),
        ("3779629", "云音乐新歌榜"),
        ("2884035", "云音乐原创榜"),
    };
}
