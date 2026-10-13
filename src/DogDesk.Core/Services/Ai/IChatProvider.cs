namespace DogDesk.Services.Ai;

/// <summary>
/// LLM 对话通道统一抽象：云端 OpenAI 兼容（<see cref="LlmClient"/>）与本地 Ollama
/// （<see cref="OllamaClient"/>，http://localhost:11434/v1，无 key 免费离线）共用同一接口。
/// 支持可选 function calling（工具白名单见 <see cref="ToolRegistry"/>）。
/// </summary>
public interface IChatProvider
{
    /// <summary>通道显示名（如"云端 OpenAI 兼容"/"本地 Ollama"）。</summary>
    string Name { get; }

    /// <summary>是否已配置可用（云端要求 base_url+key；本地始终 true，但需服务运行）。</summary>
    bool IsConfigured { get; }

    Task<ChatReply?> CompleteAsync(ChatRequest request, CancellationToken ct = default);
}

public sealed class ChatRequest
{
    public string UserMessage { get; set; } = "";
    public string? SystemPrompt { get; set; }
    /// <summary>模型名（空则用通道默认模型）。</summary>
    public string Model { get; set; } = "";
    /// <summary>可选工具白名单；非空时请求携带 function calling。</summary>
    public IReadOnlyList<ToolDescriptor>? Tools { get; set; }
    public double Temperature { get; set; } = 0.7;
}

public sealed class ChatReply
{
    /// <summary>纯文本回复（无工具调用时为 null 或空）。</summary>
    public string? Text { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
}

public sealed class ToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>参数 JSON 原文。</summary>
    public string Arguments { get; set; } = "";
}
