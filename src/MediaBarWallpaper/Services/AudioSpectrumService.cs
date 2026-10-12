using NAudio.Wave;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 系统音频频谱采集（WASAPI loopback）：捕获系统正在播放的声音并做 FFT，
/// 输出 16 段对数频段能量（0..1），供任务栏频谱与壁纸律动使用。
/// FFT/平滑数学已下沉到跨平台 <see cref="SpectrumAnalyzer"/>（MediaBarWallpaper.Core），
/// 本服务只负责 Windows 采集侧与节流。数据在后台线程产生；<see cref="GetLatest"/> 线程安全。
/// </summary>
public sealed class AudioSpectrumService : IDisposable
{
    public const int BandCount = SpectrumAnalyzer.BandCount;

    private const int MinFftIntervalMs = 33; // ≈30fps

    private WasapiLoopbackCapture? _capture;
    private readonly SpectrumAnalyzer _analyzer = new();
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

        _analyzer.Reset();
    }

    /// <summary>获取最近一次频谱（归一化 0..1）。</summary>
    public float[] GetLatest() => _analyzer.GetLatest();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded <= 0) return;

        WaveFormat fmt = _capture.WaveFormat;
        int frameSize = fmt.BlockAlign;
        int sampleSize = fmt.BitsPerSample / 8;
        if (frameSize <= 0 || sampleSize <= 0) return;

        // 帧间拆包 → 单声道 float 采样，推入共享环形缓冲
        Span<float> tmp = e.BytesRecorded <= 65536 ? stackalloc float[e.BytesRecorded / frameSize] : new float[e.BytesRecorded / frameSize];
        int n = 0;
        for (int offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
        {
            float sample = fmt.Encoding switch
            {
                WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(e.Buffer, offset),
                _ => BitConverter.ToInt16(e.Buffer, offset) / 32768f,
            };
            tmp[n++] = sample;
        }
        if (n > 0) _analyzer.AddSamples(tmp[..n]);

        if ((DateTime.Now - _lastFft).TotalMilliseconds < MinFftIntervalMs) return;
        _lastFft = DateTime.Now;
        float[] bands = _analyzer.Compute(_capture.WaveFormat.SampleRate, Sensitivity, Smoothing);
        SpectrumReady?.Invoke(bands);
    }

    public void Dispose() => Stop();
}
