using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;
using Avalonia.Threading;
using Lyricify.Lyrics.Models;
using DogDesk.Mac.Services;

namespace DogDesk.Mac.Views;

/// <summary>
/// 桌面歌词窗口：置顶、半透明、无装饰，屏幕底部居中。
/// 用 <see cref="AppServices.Lyrics"/> 数据 + 播放位置驱动当前行/下一行，
/// 逐字歌词用「已唱区域裁切」实现流光高亮。
/// </summary>
public sealed class LyricsOverlayWindow : Window
{
    private readonly AppServices _app;
    private readonly TextBlock _current;
    private readonly TextBlock _currentDim;
    private readonly TextBlock _next;
    private readonly TextBlock _translation;
    private readonly Border _clipHost;
    private readonly DispatcherTimer _timer;

    private LyricsData? _data;
    private int _lineIndex = -1;
    private bool _dualLine = true;
    private bool _vertical;
    private double _offsetPercent = 50.0;

    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0));

    public LyricsOverlayWindow(AppServices app)
    {
        _app = app;

        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ExtendClientAreaToDecorationsHint = true;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Width = 860;
        Height = 120;

        _currentDim = new TextBlock
        {
            FontSize = 26,
            Foreground = DimBrush,
            TextAlignment = TextAlignment.Center,
        };
        _current = new TextBlock
        {
            FontSize = 26,
            Foreground = Accent,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
        };

        // 流光高亮层：裁切容器宽度随演唱进度变化
        _clipHost = new Border
        {
            Width = 0,
            ClipToBounds = true,
            Child = _current,
            VerticalAlignment = VAlign.Top,
        };

        var grid = new Grid();
        grid.Children.Add(_currentDim);
        grid.Children.Add(_clipHost);

        var panel = new StackPanel
        {
            HorizontalAlignment = HAlign.Center,
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = "",
                    FontSize = 15,
                    Foreground = DimBrush,
                    Opacity = 0.6,
                    TextAlignment = TextAlignment.Center,
                },
                new TextBlock
                {
                    Text = "",
                    FontSize = 13,
                    Foreground = DimBrush,
                    Opacity = 0.8,
                    FontStyle = FontStyle.Italic,
                    TextAlignment = TextAlignment.Center,
                },
            },
        };
        _next = (TextBlock)panel.Children[1];
        _translation = (TextBlock)panel.Children[2];

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0x10, 0x20)),
            CornerRadius = new CornerRadius(10),
            Child = panel,
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) => OnTick();
    }

    public void ApplyOpacity(double o) => Opacity = Math.Clamp(o, 0.3, 1.0);

    /// <summary>双行开关：是否显示下一行。</summary>
    public void ApplyDualLine(bool dual)
    {
        _dualLine = dual;
        if (_next is not null)
            _next.IsVisible = dual;
    }

    /// <summary>竖排（v0.6）：对歌词面板施加 90° 旋转并交换窗口宽高。</summary>
    public void ApplyVertical(bool vertical)
    {
        _vertical = vertical;
        if (Content is Border b && b.Child is StackPanel panel)
        {
            panel.RenderTransform = vertical
                ? new RotateTransform(90)
                : null;
            Width = vertical ? 120 : 860;
            Height = vertical ? 860 : 120;
        }
    }

    /// <summary>垂直位置偏移（0..100，相对屏幕高度的百分比锚定）。</summary>
    public void ApplyOffset(double percent)
    {
        _offsetPercent = Math.Clamp(percent, 0, 100);
        if (IsVisible) PositionBottomCenter();
    }

    public void EnsureShown()
    {
        PositionBottomCenter();
        Show();
        _timer.Start();
    }

    public new void Hide()
    {
        _timer.Stop();
        base.Hide();
    }

    private void PositionBottomCenter()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;
            int x = (int)(area.X + (area.Width - Width) / 2);
            // 按偏移百分比（0=顶，100=底）纵向锚定
            int y = (int)(area.Y + area.Height * (_offsetPercent / 100.0) - Height / 2);
            y = Math.Clamp(y, (int)area.Y, (int)(area.Bottom - Height));
            Position = new PixelPoint(x, y);
        }
        catch { /* 定位失败忽略 */ }
    }

    private void OnTick()
    {
        if (!IsVisible) return;
        _data = _app.Lyrics.Data;
        double tMs = _app.Player.PositionSec * 1000.0;

        if (_data?.Lines is not { Count: > 0 })
        {
            if (_lineIndex != -1)
            {
                _lineIndex = -1;
                _currentDim.Text = "♪";
                _current.Text = "";
                _next.Text = "";
                _translation.Text = "";
                _clipHost.Width = 0;
            }
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
            _translation.Text = "";
            _clipHost.Width = 0;
            return;
        }

        var lines = _data?.Lines;
        if (lines is null) return;

        string text = Clean(lines[idx].Text);
        _currentDim.Text = text;
        _current.Text = text;
        _next.Text = idx + 1 < lines.Count ? Clean(lines[idx + 1].Text) : "";
        _translation.Text = ExtractTranslation(lines[idx]);
        _clipHost.Width = 0;
    }

    /// <summary>尽力读取译文字段（ChineseTranslation）；无则空串。</summary>
    private static string ExtractTranslation(ILineInfo line)
    {
        try
        {
            var prop = line.GetType().GetProperty("ChineseTranslation");
            string? t = prop?.GetValue(line) as string;
            return string.IsNullOrWhiteSpace(t) ? "" : t.Trim();
        }
        catch { return ""; }
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
