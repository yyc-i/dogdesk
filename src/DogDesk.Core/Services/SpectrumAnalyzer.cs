using NAudio.Dsp;

namespace DogDesk.Services;

/// <summary>
/// 频谱分析器（纯数学，跨平台）：维护 FFT 采样环形缓冲，输出 16 段对数频段能量（0..1）。
/// 采集侧（WASAPI / CoreAudio / mpv 音频数据）由平台实现喂入 <see cref="AddSamples"/>，
/// 平台侧按需（如 ≥33ms 节流）调用 <see cref="Compute"/>。
/// 由 v0.4.0 的 AudioSpectrumService.ComputeSpectrum 抽取而来，行为保持一致。
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int BandCount = 16;

    private const int FftSize = 1024;
    private const double MaxMagnitude = 1200.0; // 归一化参考值（经验值）

    private readonly object _lock = new();
    private readonly float[] _sampleRing = new float[FftSize];
    private int _ringIndex;
    private readonly float[] _latest = new float[BandCount];

    /// <summary>追加 PCM 采样（单声道 float，-1..1）。线程安全。</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (float s in samples)
            {
                _sampleRing[_ringIndex] = s;
                _ringIndex = (_ringIndex + 1) % FftSize;
            }
        }
    }

    /// <summary>
    /// 计算当前频谱：FFT → 16 段对数频段能量 → 上升即时/下降平滑。
    /// 返回平滑后的副本（0..1），线程安全。
    /// </summary>
    public float[] Compute(int sampleRate, double sensitivity, double smoothing)
    {
        var buffer = new Complex[FftSize];
        lock (_lock)
        {
            for (int i = 0; i < FftSize; i++)
                buffer[i] = new Complex { X = _sampleRing[(_ringIndex + i) % FftSize], Y = 0 };
        }

        FastFourierTransform.FFT(true, (int)Math.Log(FftSize, 2), buffer);

        int usable = FftSize / 2;
        var bands = new float[BandCount];

        for (int b = 0; b < BandCount; b++)
        {
            double f0 = 40.0 * Math.Pow(20000.0 / 40.0, (double)b / BandCount);
            double f1 = 40.0 * Math.Pow(20000.0 / 40.0, (double)(b + 1) / BandCount);
            int i0 = Math.Clamp((int)(f0 / sampleRate * FftSize), 0, usable - 1);
            int i1 = Math.Clamp((int)(f1 / sampleRate * FftSize), i0 + 1, usable);

            double sum = 0;
            for (int i = i0; i < i1; i++)
                sum += Math.Sqrt(buffer[i].X * buffer[i].X + buffer[i].Y * buffer[i].Y);

            bands[b] = (float)Math.Clamp(
                sum / (i1 - i0) / MaxMagnitude * sensitivity, 0, 1);
        }

        // 平滑：上升即时，下降按 Smoothing 系数放缓，避免柱条抖动
        float decay = (float)Math.Clamp(smoothing, 0.4, 0.95);
        var latest = new float[BandCount];
        lock (_lock)
        {
            for (int b = 0; b < BandCount; b++)
                _latest[b] = Math.Max(bands[b], _latest[b] * decay);
            Array.Copy(_latest, latest, BandCount);
        }
        return latest;
    }

    /// <summary>清零平滑状态（停止采集时调用，避免残留频谱冻结画面）。</summary>
    public void Reset()
    {
        lock (_lock) Array.Clear(_latest);
    }

    /// <summary>返回最近一次平滑结果副本（不触发 FFT）。线程安全。</summary>
    public float[] GetLatest()
    {
        lock (_lock) return (float[])_latest.Clone();
    }
}
