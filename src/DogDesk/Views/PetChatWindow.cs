using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DogDesk.Views;

/// <summary>桌宠锚定迷你对话气泡：复用主窗口同一 IChatProvider 管线（send 回调注入）。</summary>
public sealed class PetChatWindow : Window
{
    private readonly ListBox _log = new() { Height = 160, Background = System.Windows.Media.Brushes.Transparent, Foreground = System.Windows.Media.Brushes.White };
    private readonly TextBox _input = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _send = new() { Content = "发送", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
    private readonly Func<string, Task<string>> _sendFn;

    public PetChatWindow(Func<string, Task<string>> sendFn)
    {
        _sendFn = sendFn;
        Title = "dogdesk";
        Width = 300;
        Height = 260;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(235, 20, 22, 31));

        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(_log);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new Grid { Width = 220 };
        box.Children.Add(_input);
        row.Children.Add(box);
        row.Children.Add(_send);
        panel.Children.Add(row);
        Content = panel;

        _send.Click += async (_, _) => await SendAsync();
        _input.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await SendAsync(); };
    }

    private async Task SendAsync()
    {
        string msg = _input.Text.Trim();
        if (msg.Length == 0) return;
        _input.Text = "";
        _log.Items.Add("我：" + msg);
        try
        {
            string reply = await _sendFn(msg);
            _log.Items.Add("桌宠：" + reply);
        }
        catch (Exception ex)
        {
            _log.Items.Add("错误：" + ex.Message);
        }
    }
}
