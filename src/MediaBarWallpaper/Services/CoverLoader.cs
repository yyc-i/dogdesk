using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 封面/图片内存友好解码（v0.5 内存优化项）：
/// <list type="bullet">
/// <item>BitmapCacheOption.OnLoad：解码后立即脱离源流，避免 file/stream 长期占用；</item>
/// <item>本地文件用 using FileStream，加载完即释放句柄；</item>
/// <item>网络封面先下到 MemoryStream（不落地缓存整文件），再解码；</item>
/// <item>按目标尺寸上限解码（DecodeToDip），超大封面不全尺寸解码；</item>
/// <item>结果 Freeze，可跨线程复用。</item>
/// </list>
/// 失败一律返回 null，不抛异常。
/// </summary>
public static class CoverLoader
{
    private static readonly HttpClient Http = CreateHttp();

    /// <summary>解码尺寸上限（DIP）：封面主色提取/媒体条展示够用即可，避免全尺寸大图驻留内存。</summary>
    public const int MaxDecodeDip = 320;

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        h.Timeout = TimeSpan.FromSeconds(12);
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return h;
    }

    /// <summary>从 MediaItem 的本地路径或 URL 取封面，异步解码为冻结的 BitmapSource。</summary>
    public static async Task<ImageSource?> LoadAsync(string? localPath, string? url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                return LoadFromFile(localPath);

            if (!string.IsNullOrWhiteSpace(url))
            {
                // data: 或 远程
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return null;
                byte[] bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length is 0 or > 20_000_000) return null;
                return Decode(new MemoryStream(bytes));
            }
        }
        catch
        {
            // 封面加载失败不影响播放
        }
        return null;
    }

    /// <summary>同步从本地文件解码（用于已就绪的本地封面）。</summary>
    public static ImageSource? LoadFromFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Decode(fs);
        }
        catch { return null; }
    }

    /// <summary>按尺寸上限解码并冻结。</summary>
    private static BitmapImage Decode(Stream stream)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // 解码后脱离流
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = MaxDecodeDip;          // 限制解码尺寸（B9）
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
