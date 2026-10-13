using System.Text.Json;

namespace DogDesk.Services.Ai;

/// <summary>
/// LLM 可调用工具的白名单描述。工具实现（具体执行）由 UI 平台侧注册并执行；
/// 本表仅承载名称/描述/参数 schema/校验，供 function calling 请求与日志展示。
/// 安全硬约束：只允许白名单工具；涉及写操作需 <see cref="RequiresConfirm"/> = true 由 UI 弹窗确认；
/// 所有调用由调用方记入日志。
/// </summary>
public sealed class ToolDescriptor
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>参数 JSON Schema（OpenAI function 格式，字符串序列化）。</summary>
    public string ParametersJson { get; set; } = "{\"type\":\"object\",\"properties\":{}}";
    /// <summary>是否危险操作（执行前需用户确认弹窗）。</summary>
    public bool RequiresConfirm { get; set; }
}

/// <summary>
/// 工具注册表（进程内静态）。平台侧（WPF/Mac）启动时注册各自可执行的白名单工具。
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, ToolDescriptor> _tools = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static void Register(ToolDescriptor tool)
    {
        if (string.IsNullOrWhiteSpace(tool?.Name)) return;
        lock (_lock) _tools[tool.Name] = tool;
    }

    public static ToolDescriptor? Get(string name)
    {
        lock (_lock) return _tools.TryGetValue(name ?? "", out var t) ? t : null;
    }

    public static IReadOnlyList<ToolDescriptor> All()
    {
        lock (_lock) return _tools.Values.OrderBy(t => t.Name).ToList();
    }

    /// <summary>校验参数 JSON 至少是合法对象；具体字段校验由各工具执行器负责。</summary>
    public static (bool Ok, string Error) ValidateJson(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return (true, "");
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? (true, "")
                : (false, "参数必须是 JSON 对象");
        }
        catch (Exception ex)
        {
            return (false, $"参数 JSON 解析失败：{ex.Message}");
        }
    }
}
