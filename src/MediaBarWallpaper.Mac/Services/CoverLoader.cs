using MediaBarWallpaper.Models;

namespace MediaBarWallpaper.Mac.Services;

/// <summary>
/// 封面字节获取：优先本地封面文件，其次按 CoverUrl 下载（在线/Navidrome）。
/// 失败一律返回 null（UI 回退占位封面），不抛异常。
/// </summary>
public static class CoverLoader
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static async Task<byte[]?> FetchBytesAsync(MediaItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.LocalCoverPath) && File.Exists(item.LocalCoverPath))
                return await File.ReadAllBytesAsync(item.LocalCoverPath).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(item.CoverUrl))
            {
                string url = item.CoverUrl!;
                // Navidrome 的 CoverUrl 是相对 /rest/... 形式，需带凭据时由客户端拼好完整地址
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        catch { /* 封面下载失败降级 */ }
        return null;
    }
}
