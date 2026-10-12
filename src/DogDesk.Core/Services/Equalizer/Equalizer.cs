namespace DogDesk.Services.Equalizer;

/// <summary>
/// 10 段图形均衡器参数（31Hz .. 16kHz，1/2 倍频程间隔）。
/// 增益单位 dB，范围 -12 .. +12。纯数据模型，跨平台共享。
/// </summary>
public sealed class EqualizerPreset
{
    public const int BandCount = 10;

    /// <summary>各段中心频率（Hz）。</summary>
    public static readonly double[] Frequencies =
    {
        31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000,
    };

    /// <summary>各段增益（dB）。</summary>
    public double[] Gains { get; set; } = new double[BandCount];

    public double GetBand(int index) => Gains[Math.Clamp(index, 0, BandCount - 1)];

    public void SetBand(int index, double db)
    {
        Gains[Math.Clamp(index, 0, BandCount - 1)] = Math.Clamp(db, -12.0, 12.0);
    }

    /// <summary>全部归零（平直）。</summary>
    public void Reset() => Array.Clear(Gains);

    /// <summary>是否存在有效调节（任一频段 |g| ≥ 0.5dB）。</summary>
    public bool IsActive => Gains.Any(g => Math.Abs(g) >= 0.5);

    public EqualizerPreset Clone()
    {
        var p = new EqualizerPreset();
        Array.Copy(Gains, p.Gains, BandCount);
        return p;
    }

    /// <summary>从 AppSettings 的 double[] 恢复（长度不足补零，超长截断）。</summary>
    public static EqualizerPreset From(double[]? gains)
    {
        var p = new EqualizerPreset();
        if (gains is null) return p;
        for (int i = 0; i < BandCount; i++)
            p.Gains[i] = i < gains.Length ? Math.Clamp(gains[i], -12.0, 12.0) : 0.0;
        return p;
    }
}

/// <summary>
/// 把 <see cref="EqualizerPreset"/> 转为 libmpv 可用的音频滤镜链（ffmpeg lavfi equalizer）。
/// 用法：mpv 加载媒体前 SetOptionString("af", BuildChain(preset)) 或在运行时 set_property("af", ...)。
/// 仅包含 |g| ≥ 0.5 的频段，避免无意义滤镜。
/// </summary>
public static class EqualizerAf
{
    public static string BuildChain(EqualizerPreset preset)
    {
        var parts = new List<string>();
        for (int i = 0; i < EqualizerPreset.BandCount; i++)
        {
            double g = Math.Round(preset.Gains[i], 1);
            if (Math.Abs(g) < 0.5) continue;
            parts.Add($"equalizer=f={EqualizerPreset.Frequencies[i]:0}:g={g:0.0}");
        }
        if (parts.Count == 0) return "";
        return "lavfi=[" + string.Join(",", parts) + "]";
    }

    /// <summary>空链（旁路）。</summary>
    public const string Bypass = "";
}
