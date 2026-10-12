namespace DogDesk.Services.Playback;

/// <summary>
/// 节拍检测（简单能量阈值法）：低频段平均能量 vs 滚动平均的比值超过阈值即判为节拍。
/// 供粒子径向爆发 / 白色闪烁 / 泛光脉冲 / 歌词振动等视觉增强复用（Mineradio 风格自研）。
/// 非音频级算法，直接消费现有频谱归一化数据（0..1），开销极小。
/// </summary>
public sealed class BeatDetector
{
    private readonly double _threshold;    // 触发比值（默认 1.5 = 当前低频能量为均值 1.5 倍）
    private readonly int _minIntervalMs;   // 最小节拍间隔（防连击）
    private readonly int _lowBandCount;    // 计入低频段的个数
    private double _average;               // 滚动平均
    private DateTime _lastBeat = DateTime.MinValue;

    public BeatDetector(double threshold = 1.5, int minIntervalMs = 150, int lowBandCount = 5)
    {
        _threshold = threshold;
        _minIntervalMs = minIntervalMs;
        _lowBandCount = lowBandCount;
    }

    /// <summary>
    /// 喂入归一化频谱段（0..1，16 段），命中节拍返回 true。
    /// intensity 可做整体灵敏度缩放（&gt;1 更灵敏，&lt;1 更迟钝）。
    /// </summary>
    public bool Detect(float[] bands, double intensity = 1.0)
    {
        if (bands is null || bands.Length == 0) return false;
        int n = Math.Min(_lowBandCount, bands.Length);
        double low = 0;
        for (int i = 0; i < n; i++) low += bands[i];
        low /= n;

        _average = _average * 0.95 + low * 0.05;
        if (_average <= 0.001) return false;

        if (low / _average >= _threshold * Math.Max(0.2, intensity) &&
            (DateTime.Now - _lastBeat).TotalMilliseconds >= _minIntervalMs)
        {
            _lastBeat = DateTime.Now;
            return true;
        }
        return false;
    }

    /// <summary>重置状态（停止播放/切换频谱源时调用）。</summary>
    public void Reset()
    {
        _average = 0;
        _lastBeat = DateTime.MinValue;
    }
}
