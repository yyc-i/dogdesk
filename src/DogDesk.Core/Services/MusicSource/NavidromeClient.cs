using DogDesk.Models;

namespace DogDesk.Services.MusicSource;

/// <summary>
/// Navidrome（Subsonic API）客户端：登录校验 → 按关键词搜索歌曲 → 专辑列表 → 播放流地址。
/// 走 Subsonic REST 协议（/rest/*.view?u=&amp;p=&amp;v=&amp;c=&amp;f=json），跨平台纯 HTTP。
/// 凭据仅保存在本机 settings.json（明文），README 已说明。
/// </summary>
public sealed class NavidromeClient : IMusicSource
{
    public string Name => "Navidrome";
    /// <summary>自建服务端、凭据由用户提供，视为官方接口。</summary>
    public bool IsOfficial => true;

    /// <summary>IMusicSource 适配：搜索歌曲。</summary>
    public Task<IReadOnlyList<MediaItem>> SearchAsync(string keyword, CancellationToken ct = default)
        => SearchSongsAsync(keyword, 30, ct);

    private readonly HttpClient _http;
    private string _server = "";
    private string _user = "";
    private string _password = "";
    private string _apiVersion = "1.16.1";
    private string _clientName = "dogdesk";

    public NavidromeClient() : this(new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }))
    {
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public NavidromeClient(HttpClient http) => _http = http;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_server) && !string.IsNullOrWhiteSpace(_user);

    /// <summary>设置连接参数（登录表单提交时调用）。</summary>
    public void Configure(string server, string user, string password)
    {
        _server = server.Trim().TrimEnd('/');
        _user = user.Trim();
        _password = password;
    }

    /// <summary>校验连接与凭据（调用 ping.view）；成功返回 true。</summary>
    public async Task<bool> TestAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("ping", [], ct);
        return doc is not null && doc.RootElement.TryGetProperty("subsonic-response", out var sr)
            && sr.TryGetProperty("status", out var st) && st.GetString() == "ok";
    }

    /// <summary>按关键词搜索歌曲（search3.view）。</summary>
    public async Task<IReadOnlyList<MediaItem>> SearchSongsAsync(string keyword, int count = 30, CancellationToken ct = default)
    {
        var items = new List<MediaItem>();
        using var doc = await GetJsonAsync("search3",
            [("query", keyword), ("songCount", count.ToString()), ("albumCount", "0"), ("artistCount", "0")], ct);
        if (doc is null) return items;
        var songs = Find(doc.RootElement, "song");
        foreach (var s in songs)
        {
            var item = SongToItem(s, MediaSourceKind.Navidrome);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    /// <summary>获取专辑列表（getAlbumList2，type=newest）。</summary>
    public async Task<IReadOnlyList<MediaItem>> GetAlbumSongsAsync(int albumCount = 20, CancellationToken ct = default)
    {
        var items = new List<MediaItem>();
        using var doc = await GetJsonAsync("getAlbumList2", [("type", "newest"), ("size", albumCount.ToString())], ct);
        if (doc is null) return items;
        var albums = Find(doc.RootElement, "album");
        foreach (var a in albums)
        {
            if (!a.TryGetProperty("id", out var idEl)) continue;
            string albumId = idEl.ToString();
            string albumName = GetString(a, "name") ?? "未知专辑";
            string artist = GetString(a, "artist") ?? "";
            string? cover = a.TryGetProperty("coverArt", out var ca) ? $"getCoverArt.view?id={ca}" : null;
            // 展开专辑内歌曲（取前 50 首）
            using var albumDoc = await GetJsonAsync("getAlbum", [("id", albumId)], ct);
            if (albumDoc is null) continue;
            foreach (var s in Find(albumDoc.RootElement, "song"))
            {
                var item = SongToItem(s, MediaSourceKind.Navidrome);
                if (item is null) continue;
                item.Album = albumName;
                if (string.IsNullOrWhiteSpace(item.Artist)) item.Artist = artist;
                item.CoverUrl ??= cover;
                items.Add(item);
            }
        }
        return items;
    }

    /// <summary>构造歌曲播放流地址（stream.view）。</summary>
    public string BuildStreamUrl(string songId) =>
        $"{_server}/rest/stream.view?id={Uri.EscapeDataString(songId)}{AuthSuffix()}";

    /// <summary>构造封面图地址（getCoverArt.view）。</summary>
    public string BuildCoverUrl(string coverArtId) =>
        $"{_server}/rest/getCoverArt.view?id={Uri.EscapeDataString(coverArtId)}{AuthSuffix()}";

    private MediaItem? SongToItem(System.Text.Json.JsonElement s, MediaSourceKind kind)
    {
        if (!s.TryGetProperty("id", out var idEl)) return null;
        string id = idEl.ToString();
        string title = GetString(s, "title") ?? "未知歌曲";
        string artist = GetString(s, "artist") ?? "";
        int duration = s.TryGetProperty("duration", out var d) ? d.GetInt32() : 0;
        string? cover = s.TryGetProperty("coverArt", out var ca) ? ca.ToString() : null;

        return new MediaItem
        {
            Title = title,
            Artist = artist,
            DurationSec = duration,
            Kind = kind,
            Uri = BuildStreamUrl(id),
            CoverUrl = cover is null ? null : BuildCoverUrl(cover),
            Provider = "Navidrome",
        };
    }

    private string AuthSuffix() =>
        $"&u={Uri.EscapeDataString(_user)}&p={Uri.EscapeDataString(_password)}&v={_apiVersion}&c={_clientName}&f=json";

    private async Task<System.Text.Json.JsonDocument?> GetJsonAsync(
        string method, (string Key, string Value)[] extra, CancellationToken ct = default)
    {
        try
        {
            string url = $"{_server}/rest/{method}.view{AuthSuffix()}";
            foreach (var (k, v) in extra)
                url += $"&{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return null; // 网络/JSON 异常统一降级为空结果
        }
    }

    private static IEnumerable<System.Text.Json.JsonElement> Find(System.Text.Json.JsonElement root, string prop)
    {
        if (!root.TryGetProperty("subsonic-response", out var sr)) yield break;
        if (!sr.TryGetProperty(prop, out var el)) yield break;
        if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var e in el.EnumerateArray()) yield return e;
        }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            // search3 的结果在 song 数组里；部分接口为单对象
            if (el.TryGetProperty(prop, out var inner) && inner.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var e in inner.EnumerateArray()) yield return e;
            else if (el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty("id", out _))
                yield return el;
        }
    }

    private static string? GetString(System.Text.Json.JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            return v.GetString();
        return null;
    }
}
