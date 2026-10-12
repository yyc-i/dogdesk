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
                if (!s.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.Number)
                    continue;
                string id = idEl.GetInt64().ToString();
                string title = GetString(s, "name") ?? "未知歌曲";
                string artist = TryGetArtists(s);
                string? album = s.TryGetProperty("album", out var al) ? GetString(al, "name") : null;
                string? pic = null;
                if (s.TryGetProperty("album", out var al2) && al2.TryGetProperty("picUrl", out var picEl))
                    pic = picEl.GetString();
                double durationMs = s.TryGetProperty("duration", out var dur) ? dur.GetDouble() : 0;

                result.Add(new MediaItem
                {
                    Title = title,
                    Artist = artist,
                    Album = album,
                    DurationSec = durationMs > 0 ? durationMs / 1000.0 : 0,
                    Kind = MediaSourceKind.Online,
                    Uri = BuildPlayUrl(id),
                    CoverUrl = string.IsNullOrWhiteSpace(pic) ? null : pic,
                    Provider = Name,
                });
            }
        }
        catch (Exception ex)
        {
            // 非官方接口：任何异常都降级为空结果，由 UI 提示"在线音源暂不可用"
            System.Diagnostics.Debug.WriteLine($"[NeteaseMusicSource] {ex.Message}");
        }
        return result;
    }

    private static string? GetString(System.Text.Json.JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static string TryGetArtists(System.Text.Json.JsonElement s)
    {
        if (!s.TryGetProperty("artists", out var arr) || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
            return "";
        var names = arr.EnumerateArray()
            .Select(a => GetString(a, "name"))
            .Where(n => !string.IsNullOrWhiteSpace(n));
        return string.Join(" / ", names);
    }
}
