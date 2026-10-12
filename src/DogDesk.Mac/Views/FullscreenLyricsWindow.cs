using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;
using Avalonia.Threading;
using Lyricify.Lyrics.Models;
using DogDesk.Mac.Services;
using DogDesk.Models;

namespace DogDesk.Mac.Views;

/// <summary>
/// 全屏沉浸式歌词窗口（Folia 风格）。覆盖屏幕、深色半透明背景，
/// 当前行大字居中，按播放位置流光扫过（Flow）。
/// Name / Chorus 样式在 macOS 版降级为同一流光效果（仅配色/字号差异）。
/// </summary>
public sealed class FullscreenLyricsWindow : Window
{
    private readonly AppServices _app;
    private readonly TextBlock _currentDim;
    private readonly TextBlock _current;
    private readonly TextBlock _next;
    private readonly Border _clipHost;
    private readonly DispatcherTimer _timer;

    private LyricsData? _data;
    private int _lineIndex = -1;
    private LyricsAnimationStyle _style = LyricsAnimationStyle.Flow;

    private static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA0));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));

    public FullscreenLyricsWindow(AppServices app)
    {
        _app = app;

        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ExtendClientAreaToDecorationsHint = true;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Width = 1200;
        Height = 700;
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromArgb(0xEA, 0x0A, 0x0A, 0x14));

        _currentDim = new TextBlock
        {
            FontSize = 64,
            Foreground = Dim,
            FontWeight = FontWeight.Medium,
            TextAlignment = TextAlignment.Center,
        };
        _current = new TextBlock
        {
            FontSize = 64,
            Foreground = Accent,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
        };

        _clipHost = new Border
        {
            Width = 0,
            ClipToBounds = true,
            Child = _current,
            VerticalAlignment = VAlign.Center,
        };

        var grid = new Grid { VerticalAlignment = VAlign.Center };
        grid.Children.Add(_currentDim);
        grid.Children.Add(_clipHost);

        _next = new TextBlock
        {
            FontSize = 30,
            Foreground = Dim,
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0),
        };

        Content = new StackPanel
        {
            VerticalAlignment = VAlign.Center,
            HorizontalAlignment = HAlign.Center,
            Margin = new Thickness(60, 0),
            Children = { grid, _next },
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => OnTick();
    }

    public void ApplyOpacity(double o)
    {
        byte a = (byte)(Math.Clamp(o, 0.4, 1.0) * 255);
        Background = new SolidColorBrush(Color.FromArgb(a, 0x0A, 0x0A, 0x14));
    }

    public void ApplyStyle(LyricsAnimationStyle style)
    {
        _style = style;
        // 样式差异：仅调字号/强调色（流光机制一致）
        switch (style)
        {
            case LyricsAnimationStyle.Name:
                SetFont(76, new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x6E)));
                break;
            case LyricsAnimationStyle.Chorus:
                SetFont(54, new SolidColorBrush(Color.FromRgb(0x6E, 0xD5, 0xF5)));
                break;
            default:
                SetFont(64, new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8)));
                break;
        }
    }

    private void SetFont(double size, IBrush accent)
    {
        _currentDim.FontSize = size;
        _current.FontSize = size;
        _current.Foreground = accent;
    }

    public void EnsureShown()
    {
        ApplyOpacity(_app.Settings.Current.FullscreenLyricsOpacity);
        ApplyStyle(_app.Settings.Current.LyricsAnimationStyle);
        Show();
        _timer.Start();
    }

    public new void Hide()
    {
        _timer.Stop();
        base.Hide();
    }

    private void OnTick()
    {
        if (!IsVisible) return;
        _data = _app.Lyrics.Data;
        double tMs = _app.Player.PositionSec * 1000.0;

        if (_data?.Lines is not { Count: > 0 })
        {
            _currentDim.Text = "♪";
            _current.Text = "";
            _next.Text = "";
            _clipHost.Width = 0;
            return;
        }

        int idx = FindLineIndex(_data.Lines, tMs);
        if (idx != _lineIndex)
        {
            _lineIndex = idx;
            RenderLine(idx);
        }
        UpdateSweep(tMs);
    }

    private void RenderLine(int idx)
    {
        if (idx < 0)
        {
            _currentDim.Text = "♪";
            _current.Text = "";
            _next.Text = "";
            _clipHost.Width = 0;
            return;
        }
        var lines = _data?.Lines;
        if (lines is null) return;
        string text = Clean(lines[idx].Text);
        _currentDim.Text = text;
        _current.Text = text;
        _next.Text = idx + 1 < lines.Count ? Clean(lines[idx + 1].Text) : "";
        _clipHost.Width = 0;
    }

    private void UpdateSweep(double tMs)
    {
        var lines = _data?.Lines;
        if (_lineIndex < 0 || lines is null) { _clipHost.Width = 0; return; }
        var line = lines[_lineIndex];
        double start = line.StartTime ?? 0;
        double end = (_lineIndex + 1 < lines.Count)
            ? (lines[_lineIndex + 1].StartTime ?? start)
            : start + 5000;
        double span = Math.Max(1, end - start);
        double frac = Math.Clamp((tMs - start) / span, 0, 1);
        _clipHost.Width = _currentDim.Bounds.Width * frac;
    }

    private static int FindLineIndex(System.Collections.Generic.List<ILineInfo> lines, double tMs)
    {
        int idx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            int? st = lines[i].StartTime;
            if (!st.HasValue) continue;
            if (st.Value <= tMs) idx = i;
            else break;
        }
        return idx;
    }

    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "♪" : text.Trim();
}
