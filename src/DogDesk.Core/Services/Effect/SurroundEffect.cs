using DogDesk.Services.Equalizer;

namespace DogDesk.Services.Effect;

/// <summary>
/// 3D 环绕音效参数（Mineradio 风格功能级自研）：
/// 通过 ffmpeg lavfi 滤镜组合实现——<c>extrastereo</c>（立体声宽度）+ <c>aphaser</c>（相位旋转环绕，speed 即环绕速度）。
/// 全部走现有 libmpv af 滤镜链，无需改动音频内核。
/// </summary>
public sealed class SurroundEffect
{
    /// <summary>总开关。</summary>
    public bool Enabled { get; set; }

    /// <summary>立体声宽度（0 .. 6，extrastereo m 参数）。</summary>
    public double Width { get; set; } = 2.5;

    /// <summary>环绕速度（0.1 .. 2.0 Hz，aphaser speed 参数）。</summary>
    public double Speed { get; set; } = 0.5;

    public SurroundEffect Clone() => new() { Enabled = Enabled, Width = Width, Speed = Speed };

    /// <summary>从 AppSettings 恢复。</summary>
    public static SurroundEffect FromSettings(bool enabled, double width, double speed) => new()
    {
        Enabled = enabled,
        Width = Math.Clamp(width, 0.0, 6.0),
        Speed = Math.Clamp(speed, 0.1, 2.0),
    };
}

/// <summary>
/// 音效预设（可导出/导入分享，JSON 文件）：环绕参数 + 均衡器增益 + 名称。
/// </summary>
public sealed class EffectPreset
{
    public string Name { get; set; } = "未命名预设";
    public bool SurroundEnabled { get; set; }
    public double SurroundWidth { get; set; } = 2.5;
    public double SurroundSpeed { get; set; } = 0.5;
    public double[] EqualizerGains { get; set; } = new double[EqualizerPreset.BandCount];

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public void Export(string path)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(this, JsonOpt));
    }

    public static EffectPreset? Import(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var p = System.Text.Json.JsonSerializer.Deserialize<EffectPreset>(
                System.IO.File.ReadAllText(path), JsonOpt);
            if (p is null) return null;
            if (p.EqualizerGains is null || p.EqualizerGains.Length != EqualizerPreset.BandCount)
                p.EqualizerGains = new double[EqualizerPreset.BandCount];
            return p;
        }
        catch
        {
            return null;
        }
    }

    public EqualizerPreset ToEqualizer() => EqualizerPreset.From(EqualizerGains);

    public SurroundEffect ToSurround() => new()
    {
        Enabled = SurroundEnabled,
        Width = Math.Clamp(SurroundWidth, 0.0, 6.0),
        Speed = Math.Clamp(SurroundSpeed, 0.1, 2.0),
    };
}

/// <summary>
/// 把「均衡器 + 3D 环绕」组合为一条 libmpv af 滤镜链（lavfi 包裹）。
/// 空串 = 旁路。mpv 运行时 set_property("af", ...) 即生效。
/// </summary>
public static class EffectAf
{
    public static string BuildChain(EqualizerPreset eq, SurroundEffect surround)
    {
        var parts = new List<string>();
        if (surround.Enabled)
        {
            double w = Math.Clamp(surround.Width, 0.0, 6.0);
            if (w > 0.05) parts.Add($"extrastereo=m={w:0.0#}");
            double s = Math.Clamp(surround.Speed, 0.1, 2.0);
            parts.Add($"aphaser=in_gain=0.4:out_gain=0.74:delay=3:decay=0.4:speed={s:0.00}:type=triangular");
        }
        parts.AddRange(EqualizerAf.Filters(eq));
        return parts.Count == 0 ? "" : "lavfi=[" + string.Join(",", parts) + "]";
    }
}
