using System.ComponentModel;
using System.Windows.Media;

namespace DogDesk.Services;

/// <summary>
/// 会话路由（v0.5）：在「内置 mpv 音频会话」与「系统 SMTC 会话」之间透明切换，
/// 对媒体条/歌词/全屏歌词暴露统一的 <see cref="IMediaSession"/>。
/// <list type="bullet">
/// <item>播放内置曲目（本地曲库/在线/Navidrome）→ <see cref="ActivateBuiltin"/>，媒体条/歌词/封面主色跟随内置；</item>
/// <item>内置停止且队列清空 → <see cref="ReleaseToSystem"/>，自动切回系统 SMTC 会话（系统正在播放时）；</item>
/// <item>非内置期间，SMTC 的曲目/封面/进度实时驱动媒体条，与 v0.4 行为一致。</item>
/// </list>
/// 仅转发「当前激活会话」的属性变化，避免两套会话互相干扰。
/// </summary>
public sealed class MediaSessionRouter : IMediaSession
{
    private readonly SmtcMediaSession _system;
    private readonly MpvAudioPlayer _builtin;

    /// <summary>当前是否以内置会话为激活路由。</summary>
    public bool BuiltinActive { get; private set; }

    public MediaSessionRouter(SmtcMediaSession system, MpvAudioPlayer builtin)
    {
        _system = system;
        _builtin = builtin;

        _system.PropertyChanged += OnInnerChanged;
        _builtin.PropertyChanged += OnInnerChanged;
    }

    /// <summary>激活内置会话（每次内置开始播放一首新曲时调用）。</summary>
    public void ActivateBuiltin()
    {
        if (BuiltinActive) return;
        BuiltinActive = true;
        RaiseAll();
    }

    /// <summary>释放回系统会话（内置队列清空/停止时调用）。</summary>
    public void ReleaseToSystem()
    {
        if (!BuiltinActive) return;
        BuiltinActive = false;
        RaiseAll();
    }

    private IMediaSession Active => BuiltinActive ? _builtin : _system;

    // ================= 转发 =================

    public string Title => Active.Title;
    public string Artist => Active.Artist;
    public ImageSource? Thumbnail => Active.Thumbnail;
    public bool IsPlaying => Active.IsPlaying;
    public double Progress01 => Active.Progress01;
    public double PositionSec => Active.PositionSec;
    public double DurationSec => Active.DurationSec;
    public bool IsReal => Active.IsReal;

    public void TogglePlayPause() => Active.TogglePlayPause();
    public void Next() => Active.Next();
    public void Previous() => Active.Previous();
    public void Seek(double fraction) => Active.Seek(fraction);

    // ================= 事件转发 =================

    private void OnInnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 只转发当前激活会话的变化；非激活会话的更新直接忽略（路由互不干扰）
        if (!ReferenceEquals(sender, Active)) return;
        PropertyChanged?.Invoke(this, e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        foreach (var n in new[] { nameof(Title), nameof(Artist), nameof(Thumbnail),
                   nameof(IsPlaying), nameof(PositionSec), nameof(DurationSec),
                   nameof(Progress01), nameof(IsReal) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
