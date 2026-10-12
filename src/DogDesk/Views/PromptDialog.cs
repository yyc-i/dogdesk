using System.Windows;
using System.Windows.Controls;

namespace DogDesk.Views;

/// <summary>极简文本输入对话框（避免依赖 VisualBasic InputBox）。</summary>
internal static class PromptDialog
{
    public static string? Show(string title, string current)
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = System.Windows.Media.Brushes.White,
            ResizeMode = ResizeMode.NoResize,
        };
        var box = new TextBox { Text = current, Margin = new Thickness(16, 16, 16, 0) };
        var ok = new Button { Content = "确定", Width = 90, Margin = new Thickness(16, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(ok);
        win.Content = panel;
        return win.ShowDialog() == true ? result : null;
    }
}
