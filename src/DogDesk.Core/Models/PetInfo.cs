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
}
