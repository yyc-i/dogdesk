namespace DogDesk.Services.Ai;

/// <summary>
/// OpenAI 兼容图像生成客户端（/images/generations，如豆包/OpenAI 视觉模型，用户自备 key）。
/// 供「AI 生成开机动画素材」入口使用；best effort，失败返回 null。
/// </summary>
public sealed class ImageGenClient
{
    private static readonly HttpClient Http = CreateHttp();

    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        h.Timeout = TimeSpan.FromSeconds(90);
        return h;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

    /// <summary>生成一张图片，返回可直接下载的 URL 或 base64 数据；失败返回 null。</summary>
    public async Task<string?> GenerateImageAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(prompt)) return null;
        try
        {
            string url = BaseUrl.TrimEnd('/') + "/images/generations";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            var body = new { model = Model, prompt, n = 1, size = "1024x1024" };
            req.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == System.Text.Json.JsonValueKind.Array &&
                data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.TryGetProperty("url", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String)
                    return u.GetString();
                if (first.TryGetProperty("b64_json", out var b64) && b64.ValueKind == System.Text.Json.JsonValueKind.String)
                    return b64.GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageGenClient] {ex.Message}");
            return null;
        }
    }
}
