using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DogDesk.Models;
using DogDesk.Services;
using DogDesk.Services.DeskBox;

namespace DogDesk.Views;

/// <summary>
/// DeskBox 桌面格子叠加层（DeskBox 风格功能级自研，未复制 GPL 代码）：
/// 透明窗口，由 MainWindow SetParent 到 WorkerW（与壁纸共存，不 hook Explorer）。
/// 格子是对原文件/文件夹的链接——不移动原位；拖入文件=添加格子，双击=打开原文件，右键=删除。
/// 含待办/随记/音乐控制小组件。数据来自 <see cref="DeskBoxData"/>，变更回调落盘。
/// </summary>
public sealed class DeskBoxOverlayWindow : Window
{
    private readonly DeskBoxData _data;
    private readonly string _screenKey;
    private readonly IMediaSession _session;
    private readonly Action _changed;

    private readonly WrapPanel _tilesHost = new() { Margin = new Thickness(18, 18, 0, 0) };
    private readonly TextBox _todoInput = new();
    private readonly StackPanel _todoList = new();
    private readonly TextBox _noteBox = new();
    private readonly TextBlock _musicTitle = new();

    public DeskBoxOverlayWindow(Rect bounds, DeskBoxData data, string screenKey,
        IMediaSession session, Action onChanged)
    {
        _data = data;
        _screenKey = screenKey;
        _session = session;
        _changed = onChanged;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.X;
        Top = bounds.Y;
        AllowDrop = true;

        Drop += OnFileDrop;
        DragOver += (_, e) => e.Effects = DragDropEffects.Copy;

        var root = new Grid();
        root.Children.Add(_tilesHost);

        // 右侧小组件列
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 18, 0) };
        if (_data.ShowMusic) right.Children.Add(BuildMusicWidget());
        if (_data.ShowTodo) right.Children.Add(BuildTodoWidget());
        if (_data.ShowNote) right.Children.Add(BuildNoteWidget());
        root.Children.Add(right);

        Content = root;
        _session.PropertyChanged += OnSessionChanged;

        RebuildTiles();
        RebuildTodos();
    }

    // ================= 格子 =================

    private ScreenLayout ThisScreen =>
        _data.Screens.TryGetValue(_screenKey, out var s) ? s : _data.Screens[_screenKey] = new ScreenLayout();

    public void AddFileTile(string path)
    {
        var screen = ThisScreen;
        screen.Tiles.Add(new DeskBoxTile
        {
            Name = Path.GetFileName(path),
            Path = path,
            Color = "#818CF8",
        });
        _changed();
        RebuildTiles();
    }

    private void RebuildTiles()
    {
        _tilesHost.Children.Clear();
        foreach (var tile in ThisScreen.Tiles)
        {
            var btn = new Border
            {
                Width = 86, Height = 86, Margin = new Thickness(6),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tile.Color)),
                Opacity = 0.92,
                ToolTip = tile.Path,
                Child = new TextBlock
                {
                    Text = "📄\n" + tile.Name,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    Margin = new Thickness(6),
                },
            };
            btn.MouseLeftButtonDown += (_, _) => OpenTile(tile);
            btn.ContextMenu = BuildTileContext(tile);
            _tilesHost.Children.Add(btn);
        }
    }

    private ContextMenu BuildTileContext(DeskBoxTile tile)
    {
        var menu = new ContextMenu();
        var del = new MenuItem { Header = "删除格子" };
        del.Click += (_, _) =>
        {
            ThisScreen.Tiles.Remove(tile);
            _changed();
            RebuildTiles();
        };
        var rename = new MenuItem { Header = "重命名…" };
        rename.Click += (_, _) =>
        {
            string? n = PromptDialog.Show("重命名格子", tile.Name);
            if (!string.IsNullOrWhiteSpace(n)) { tile.Name = n; _changed(); RebuildTiles(); }
        };
        menu.Items.Add(rename);
        menu.Items.Add(del);
        return menu;
    }

    private void OpenTile(DeskBoxTile tile)
    {
        try { Process.Start(new ProcessStartInfo(tile.Path) { UseShellExecute = true }); }
        catch { }
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files) AddFileTile(f);
    }

    // ================= 小组件 =================

    private Border BuildMusicWidget()
    {
        _musicTitle.Text = "未在播放";
        _musicTitle.Foreground = Brushes.White;
        _musicTitle.TextWrapping = TextWrapping.Wrap;
        _musicTitle.MaxWidth = 220;

        var prev = MakeBtn("⏮", () => _session.Previous());
        var play = MakeBtn("▶", () => _session.TogglePlayPause());
        var next = MakeBtn("⏭", () => _session.Next());
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(prev); row.Children.Add(play); row.Children.Add(next);

        var panel = new StackPanel();
        panel.Children.Add(_musicTitle);
        panel.Children.Add(row);
        return Wrap(panel, "音乐");
    }

    private static Button MakeBtn(string g, Action onClick)
    {
        var b = new Button { Content = g, Width = 34, Height = 30, Margin = new Thickness(2, 6, 2, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private Border BuildTodoWidget()
    {
        _todoInput.TextChanged += (_, _) => { _data.Note = _noteBox.Text; };
        var add = new Button { Content = "＋ 待办", Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_todoInput.Text))
            {
                _data.Todos.Add(new DeskBoxTodo { Text = _todoInput.Text.Trim() });
                _todoInput.Text = "";
                _changed();
                RebuildTodos();
            }
        };
        var panel = new StackPanel();
        _todoInput.Height = 24;
        panel.Children.Add(_todoInput);
        panel.Children.Add(_todoList);
        panel.Children.Add(add);
        return Wrap(panel, "待办");
    }

    private void RebuildTodos()
    {
        _todoList.Children.Clear();
        foreach (var t in _data.Todos)
        {
            var cb = new CheckBox
            {
                Content = t.Text,
                Foreground = Brushes.White,
                IsChecked = t.Done,
                Margin = new Thickness(0, 2, 0, 0),
            };
            cb.Checked += (_, _) => { t.Done = true; _changed(); };
            cb.Unchecked += (_, _) => { t.Done = false; _changed(); };
            _todoList.Children.Add(cb);
        }
    }

    private Border BuildNoteWidget()
    {
        _noteBox.AcceptsReturn = true;
        _noteBox.TextWrapping = TextWrapping.Wrap;
        _noteBox.Height = 120;
        _noteBox.Text = _data.Note;
        _noteBox.TextChanged += (_, _) => { _data.Note = _noteBox.Text; _changed(); };
        return Wrap(_noteBox, "随记");
    }

    private static Border Wrap(UIElement content, string title)
    {
        var titleBlock = new TextBlock
        {
            Text = title, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4),
        };
        var panel = new StackPanel();
        panel.Children.Add(titleBlock);
        panel.Children.Add(content);
        return new Border
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1B, 0x1E, 0x2A)),
            Child = panel,
        };
    }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IMediaSession.Title) or nameof(IMediaSession.Artist))
            _musicTitle.Dispatcher.BeginInvoke(() =>
                _musicTitle.Text = _session.Title == "--" ? "未在播放" : $"{_session.Title} - {_session.Artist}");
    }
}
