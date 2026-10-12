using DogDesk.Models;

namespace DogDesk.Services.MusicSource;

/// <summary>
/// 音乐电台（Mineradio 风格自研）：按风格关键词经在线音源聚合出 60 首推荐队列。
/// 依赖第三方非官方接口时，UI 沿用"仅供学习、稳定性不保证"标注。
/// </summary>
public sealed class RadioService
{
    /// <summary>电台风格目录（风格名 → 聚合搜索关键词）。</summary>
    public static readonly (string Genre, string Keyword)[] Genres =
    {
        ("DJ/电音", "DJ 电音"),
        ("流行", "流行歌曲"),
        ("民谣", "民谣"),
        ("摇滚", "摇滚"),
        ("古风", "古风"),
        ("说唱", "说唱"),
        ("轻音乐", "轻音乐"),
        ("车载", "车载DJ"),
    };

    private readonly IMusicSource _source;

    public RadioService(IMusicSource source) => _source = source;

    /// <summary>按风格生成推荐队列（默认 60 首，多关键词聚合去重）。失败返回空列表。</summary>
    public async Task<IReadOnlyList<MediaItem>> BuildQueueAsync(string genre, int target = 60, CancellationToken ct = default)
    {
        string keyword = "热门";
        foreach (var (g, k) in Genres)
        {
            if (string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)) { keyword = k; break; }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<MediaItem>();
        string[] queries = { $"{keyword} 热门", keyword, $"{keyword} 精选" };
        foreach (string q in queries)
        {
            if (ct.IsCancellationRequested || queue.Count >= target) break;
            IReadOnlyList<MediaItem> hits;
            try { hits = await _source.SearchAsync(q, ct); }
            catch { hits = Array.Empty<MediaItem>(); }

            foreach (var item in hits)
            {
                if (queue.Count >= target) break;
                if (!item.IsValid || !seen.Add(item.Uri)) continue;
                queue.Add(item);
            }
        }
        return queue;
    }
}
