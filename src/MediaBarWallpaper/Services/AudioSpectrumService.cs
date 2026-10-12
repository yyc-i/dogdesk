using NAudio.Dsp;
using NAudio.Wave;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 系统音频频谱采集（WASAPI loopback）：捕获系统正在播放的声音并做 FFT，
/// 输出 16 段对数频段能量（0..1），供任务栏频谱与壁纸律动使用。
/// 数据在后台线程产生；<see cref="GetLatest"/> 线程安全。
/// </summary>
public sealed class AudioSpectrumService : IDisposable
{
    public const int BandCount = 16;

    private const int FftSize = 1024;
    private const double MaxMagnitude = 1200.0; // 归一化参考值（经验值）
    private const int MinFftIntervalMs = 33;    // ≈30fps

    private WasapiLoopbackCapture? _capture;
    private readonly float[] _sampleRing = new float[FftSize];
    private int _ringIndex;
    private readonly object _lock = new();
    private readonly float[] _latest = new float[BandCount];
    private DateTime _lastFft = DateTime.MinValue;

    /// <summary>灵敏度（能量放大系数，1 = 默认）。</summary>
    public double Sensitivity { get; set; } = 1.0;

    /// <summary>下降平滑系数（0.4 快速回落 .. 0.95 缓慢回落），上升始终即时。</summary>
    public double Smoothing { get; set; } = 0.72;

    public event Action<float[]>? SpectrumReady;

    public bool IsRunning => _capture is not null;

    /// <summary>启动系统声音捕获；失败（无默认输出设备等）返回 false。</summary>
    public bool Start()
    {
        if (_capture is not null) return true;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            return true;
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
            return false;
        }
    }

    public void Stop()
    {
        if (_capture is null) return;
        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
        }
        catch { }
        _capture.Dispose();
        _capture = null;

        lock (_lock) Array.Clear(_latest);
    }

    /// <summary>获取最近一次频谱（归一化 0..1）。</summary>
    public float[] GetLatest()
    {
        lock (_lock) return (float[])_latest.Clone();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded <= 0) return;

        WaveFormat fmt = _capture.WaveFormat;
        int frameSize = fmt.BlockAlign;
        int sampleSize = fmt.BitsPerSample / 8;
        if (frameSize <= 0 || sampleSize <= 0) return;

        lock (_lock)
        {
            for (int offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
            {
                float sample = fmt.Encoding switch
                {
                    WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(e.Buffer, offset),
                    _ => BitConverter.ToInt16(e.Buffer, offset) / 32768f,
                };
                _sampleRing[_ringIndex] = sample;
                _ringIndex = (_ringIndex + 1) % FftSize;
            }
        }

        if ((DateTime.Now - _lastFft).TotalMilliseconds < MinFftIntervalMs) return;
        _lastFft = DateTime.Now;
        ComputeSpectrum();
    }

    private void ComputeSpectrum()
    {
        var buffer = new Complex[FftSize];
        lock (_lock)
        {
            for (int i = 0; i < FftSize; i++)
                buffer[i] = new Complex { X = _sampleRing[(_ringIndex + i) % FftSize], Y = 0 };
        }

        FastFourierTransform.FFT(true, (int)Math.Log(FftSize, 2), buffer);

        double sampleRate = _capture?.WaveFormat.SampleRate ?? 44100.0;
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
                sum / (i1 - i0) / MaxMagnitude * Sensitivity, 0, 1);
        }

        // 平滑：上升即时，下降按 Smoothing 系数放缓，避免柱条抖动
        float decay = (float)Math.Clamp(Smoothing, 0.4, 0.95);
        lock (_lock)
        {
            for (int b = 0; b < BandCount; b++)
                _latest[b] = Math.Max(bands[b], _latest[b] * 0.72f);
        }

        SpectrumReady?.Invoke(GetLatest());
    }

    public void Dispose() => Stop();
}
