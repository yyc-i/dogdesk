namespace DogDesk.Models;

/// <summary>
/// 桌宠实例（BongoCat 风格自研）：形象来源（内置/图片序列）、位置/大小/表情记忆。
/// </summary>
public sealed class PetInfo
{
    /// <summary>桌宠显示名（唯一标识）。</summary>
    public string Name { get; set; } = "";

    /// <summary>形象类型：Builtin（内置自绘猫）/ ImageSeq（图片序列帧文件夹）。</summary>
    public string ModelType { get; set; } = "Builtin";

    /// <summary>素材路径（ModelType=ImageSeq 时为帧文件夹；Builtin 可为空）。</summary>
    public string ModelPath { get; set; } = "";

    /// <summary>记忆的屏幕位置（物理像素，主屏坐标）。</summary>
    public double X { get; set; } = 400;
    public double Y { get; set; } = 300;

    /// <summary>记忆的大小（像素）。</summary>
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 200;

    /// <summary>当前表情状态（idle/happy/sleep 等，随实现扩展）。</summary>
    public string Expression { get; set; } = "idle";

    public int ZIndex { get; set; } = 0;

    // ---- v0.8.0 表现层升级（纯增量字段；旧 JSON 缺省时取默认值，向后兼容）----

    /// <summary>毛色（十六进制，如 #FFF3E0；空/非法时用默认色）。</summary>
    public string Color { get; set; } = "#FFF3E0";

    /// <summary>每只桌宠的独立缩放（0.5 .. 2.0；默认 1.0，再乘全局 PetScale）。</summary>
    public double Scale { get; set; } = 1.0;
}
