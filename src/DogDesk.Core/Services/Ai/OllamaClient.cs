using System.Text.Json;
using System.Text.Json.Nodes;

namespace DogDesk.Services.Ai;

/// <summary>
/// 本地 LLM 通道：Ollama（http://localhost:11434/v1，OpenAI 兼容、无 key、免费离线、数据不出本机）。
/// 自动检测服务运行与已拉取模型；未安装时由 UI 给出安装与 pull 指引。
/// </summary>
public sealed class OllamaClient : IChatProvider
{
    private static readonly HttpClient Http = CreateHttp();
    private readonly string _baseUrl;

    public string Name => "本地（Ollama）";

    public OllamaClient(string baseUrl = "http://localhost:11434")
    {
        _baseUrl = (baseUrl ?? "").TrimEnd('/').TrimEnd('/').TrimEnd('/');
        if (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            _baseUrl = _baseUrl[..^3];
    }

    /// <summary>Ollama 的 OpenAI 兼容端点（追加 /v1）。</summary>
    public string ApiBaseUrl => _baseUrl + "/v1";

    /// <summary>本地通道始终"已配置"；实际可用性以 <see cref="CheckAsync"/> 为准。</summary>
    public bool IsConfigured => true;

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        h.Timeout = TimeSpan.FromSeconds(120);
        return h;
    }

    /// <summary>检测 Ollama 是否运行（GET /api/tags，短超时）。</summary>
    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var resp = await Http.GetAsync(_baseUrl + "/api/tags", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>列出本机已拉取模型名（如 "qwen2.5:7b"）；未运行返回空列表。</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var result = new List<string>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await Http.GetAsync(_baseUrl + "/api/tags", cts.Token);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in models.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        result.Add(n.GetString()!);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OllamaClient.ListModels] {ex.Message}");
        }
        return result;
    }

    public async Task<ChatReply?> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage)) return null;
        try
        {
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.UserMessage });

            var body = new JsonObject
            {
                ["model"] = request.Model ?? "",
                ["messages"] = messages,
                ["temperature"] = request.Temperature,
                ["stream"] = false,
            };
            if (request.Tools is { Count: > 0 })
            {
                var tools = new JsonArray();
                foreach (var t in request.Tools)
                {
                    JsonNode? parameters = null;
                    try { parameters = JsonNode.Parse(t.ParametersJson); } catch { }
                    tools.Add(new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["parameters"] = parameters ?? new JsonObject(),
                        },
                    });
                }
                body["tools"] = tools;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + "/chat/completions");
            req.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return ChatCompletions.Parse(doc.RootElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OllamaClient] {ex.Message}");
            return null;
        }
    }
}
