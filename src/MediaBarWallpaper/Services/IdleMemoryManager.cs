using System.Windows.Threading;
using MediaBarWallpaper.Interop;

namespace MediaBarWallpaper.Services;

/// <summary>
/// 空闲期内存/能耗管理器（v0.5 内存优化项 B1/B2/B4 合并驱动）：
/// 单一低频 DispatcherTimer（默认 45s）替代多个高频定时器，降低唤醒次数。
/// 判定「空闲」= 无媒体播放 且 媒体条/桌面歌词/全屏歌词均未显示（且 IdleReduceFps 开启）。
/// 空闲时：EmptyWorkingSet 裁剪进程工作集 + GC.Collect(2, Optimized)；
/// 并回调 <see cref="IdleTick"/> 让主窗口按需暂停频谱采集 / 降低壁纸动画帧率。
/// 活动时跳过裁剪并回调 <see cref="ActiveTick"/> 恢复。
/// </summary>
public sealed class IdleMemoryManager
{
    private readonly Func<bool> _isIdle;
    private DispatcherTimer? _timer;

    /// <summary>每个空闲周期触发一次（用于频谱按需 Stop、壁纸降帧）。</summary>
    public Action? IdleTick;

    /// <summary>从空闲恢复为活动时触发一次（用于频谱按需 Start、壁纸恢复帧率）。</summary>
    public Action? ActiveTick;

    private bool _wasIdle = true;

    public IdleMemoryManager(Func<bool> isIdle) => _isIdle = isIdle;

    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        bool idle;
        try { idle = _isIdle(); } catch { return; }

        if (idle)
        {
            // B1：裁剪工作集 + 低频 GC（活动时跳过）
            try { NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess()); } catch { }
            GC.Collect(2, GCCollectionMode.Optimized, false);

            IdleTick?.Invoke();
        }
        else if (_wasIdle)
        {
            ActiveTick?.Invoke();
        }
        _wasIdle = idle;
    }
}
