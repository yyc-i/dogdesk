using System.Text.Json;
using System.Text.Json.Nodes;

namespace DogDesk.Services.Ai;

/// <summary>
/// 云端 LLM 通道：OpenAI 兼容 Chat Completions（豆包/DeepSeek/OpenAI 等，用户自备 base_url+key）。
/// 纯 net8.0 无 WPF 依赖。隐私：对话发送到用户自行配置的第三方服务，由用户负责；本类不收集额外数据。
/// </summary>
public sealed class LlmClient : IChatProvider
{
    private static readonly HttpClient Http = CreateHttp();

    public string Name => "云端（OpenAI 兼容）";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public double Temperature { get; init; } = 0.7;

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        h.Timeout = TimeSpan.FromSeconds(60);
        return h;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

    /// <summary>兼容旧签名：单轮对话（无工具）。</summary>
    public Task<string?> CompleteAsync(string userMessage, string? systemPromptOverride = null, CancellationToken ct = default)
        => CompleteTextAsync(userMessage, systemPromptOverride, ct);

    private async Task<string?> CompleteTextAsync(string userMessage, string? systemPromptOverride, CancellationToken ct)
    {
        var reply = await CompleteAsync(new ChatRequest
        {
            UserMessage = userMessage,
            SystemPrompt = string.IsNullOrWhiteSpace(systemPromptOverride) ? SystemPrompt : systemPromptOverride,
            Temperature = Temperature,
        }, ct);
        return reply?.Text;
    }

    public async Task<ChatReply?> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(request.UserMessage)) return null;
        try
        {
            string url = BaseUrl.TrimEnd('/') + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            req.Content = new StringContent(BuildBody(request).ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return ChatCompletions.Parse(doc.RootElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LlmClient] {ex.Message}");
            return null;
        }
    }

    internal JsonObject BuildBody(ChatRequest request)
    {
        var messages = new JsonArray();
        string sys = string.IsNullOrWhiteSpace(request.SystemPrompt) ? SystemPrompt : request.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(sys)) messages.Add(new JsonObject { ["role"] = "system", ["content"] = sys });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.UserMessage });

        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? Model : request.Model,
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
        return body;
    }
}

/// <summary>chat/completions 响应解析（文本 + 可选 tool_calls）。</summary>
public static class ChatCompletions
{
    public static ChatReply? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var msg)) return null;

        var reply = new ChatReply();
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            reply.Text = content.GetString();

        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            reply.ToolCalls = new List<ToolCall>();
            foreach (var c in calls.EnumerateArray())
            {
                var call = new ToolCall();
                if (c.TryGetProperty("id", out var id)) call.Id = id.GetString() ?? "";
                if (c.TryGetProperty("function", out var fn))
                {
                    if (fn.TryGetProperty("name", out var n)) call.Name = n.GetString() ?? "";
                    if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                        call.Arguments = a.GetString() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(call.Name)) reply.ToolCalls.Add(call);
            }
            if (reply.ToolCalls.Count == 0) reply.ToolCalls = null;
        }
        return reply;
    }
}
