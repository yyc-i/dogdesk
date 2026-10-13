using System.Text.RegularExpressions;

namespace DogDesk.Services.Download;

/// <summary>
/// 剪贴板/外部文本中的下载链接识别（纯静态、无剪贴板权限申请；
/// 由宿主侧把剪贴板文本通过 <see cref="OfferText"/> 喂入）。
/// </summary>
public static class ClipboardLinkWatcher
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""''\[\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>URL 结尾需要剥离的成对/标点字符（中英文混合）。</summary>
    private static readonly char[] TrailingPunct =
    {
        '.', ',', ';', ':', '!', '?', ')', ']', '}', '>', '"', '\'', '，', '。', '；', '：',
        '！', '？', '）', '】', '」', '』', '”', '’', '、',
    };

    /// <summary>最近一次检测到的链接（用于去重，避免重复弹下载提示）。</summary>
    public static string? LastDetectedUrl { get; private set; }

    /// <summary>检测到与上次不同的新链接时触发。</summary>
    public static event Action<string>? LinkDetected;

    /// <summary>从文本中提取首个 http(s) 链接并清理尾部标点；无则返回 null。</summary>
    public static string? ExtractUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = UrlRegex.Match(text);
        if (!m.Success) return null;
        string url = m.Value;
        bool trimmed = true;
        while (trimmed && url.Length > 0)
        {
            trimmed = false;
            foreach (char c in TrailingPunct)
            {
                if (url.EndsWith(c))
                {
                    url = url.Substring(0, url.Length - 1);
                    trimmed = true;
                    break;
                }
            }
        }
        return url.Length > 0 ? url : null;
    }

    /// <summary>喂入一段文本（如剪贴板内容）；与上次相同则忽略，不同则更新并触发事件。</summary>
    public static void OfferText(string? text)
    {
        string? url = ExtractUrl(text);
        if (url == null) return;
        if (string.Equals(url, LastDetectedUrl, StringComparison.Ordinal)) return;
        LastDetectedUrl = url;
        LinkDetected?.Invoke(url);
    }
}
