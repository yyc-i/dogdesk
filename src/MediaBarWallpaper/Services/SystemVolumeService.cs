using NAudio.CoreAudioApi;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 系统主音量控制（NAudio AudioEndpointVolume）：读取/设置 0..1，
/// 供媒体条滚轮调节音量。失败（无默认输出设备）时返回静默 null 语义。
/// </summary>
public sealed class SystemVolumeService : IDisposable
{
    private MMDevice? _device;
    private AudioEndpointVolume? _volume;

    public SystemVolumeService()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _volume = _device.AudioEndpointVolume;
        }
        catch
        {
            // 无输出设备时不启用音量控制
        }
    }

    public bool Available => _volume is not null;

    /// <summary>当前主音量 0..1；不可用时返回 0。</summary>
    public float GetVolume()
    {
        try { return _volume?.MasterVolumeLevelScalar ?? 0; }
        catch { return 0; }
    }

    /// <summary>设置主音量 0..1。</summary>
    public void SetVolume(float value)
    {
        try
        {
            if (_volume is not null)
                _volume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 1);
        }
        catch { }
    }

    /// <summary>系统是否静音。</summary>
    public bool GetMute()
    {
        try { return _volume?.Mute ?? false; }
        catch { return false; }
    }

    public void SetMute(bool mute)
    {
        try
        {
            if (_volume is not null)
                _volume.Mute = mute;
        }
        catch { }
    }

    public void Dispose()
    {
        _volume = null;
        _device?.Dispose();
        _device = null;
    }
}
