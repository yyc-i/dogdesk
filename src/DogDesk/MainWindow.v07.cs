using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DogDesk.Models;
using DogDesk.Services.Ai;
using DogDesk.Services.Optimizer;
using DogDesk.Services.Pets;
using DogDesk.Views;

namespace DogDesk;

public partial class MainWindow
{
    private PetWindow? _pet;
    private readonly List<(string Role, string Text)> _chat = new();

    // ================= 模块 1 桌宠 =================

    private void OnPetToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.PetEnabled = PetEnabledCheck.IsChecked == true;
            _settingsService.Save();
            if (_settings.PetEnabled)
                ShowPet();
            else
                ClosePet();
            PetStatusText.Text = _settings.PetEnabled ? "桌宠已显示（可拖动）" : "桌宠已关闭";
        }
        catch (Exception ex) { PetStatusText.Text = "桌宠异常：" + ex.Message; }
    }

    private void ShowPet()
    {
        ClosePet();
        _pet = new PetWindow
        {
            Left = _settings.PetScale * 100,
            Top = SystemParameters.WorkArea.Bottom - 180,
            Width = 120 * _settings.PetScale,
            Height = 120 * _settings.PetScale,
        };
        _pet.SourceInitialized += (_, _) => _pet.ApplyNoActivate();
        _pet.Clicked += pet =>
        {
            try
            {
                var bubble = new PetChatWindow(AskAiAsync)
                {
                    Left = pet.Left - 140,
                    Top = pet.Top - 200,
                };
                bubble.Show();
            }
            catch (Exception ex) { Log("[桌宠] 气泡失败：" + ex.Message); }
        };
        _pet.Show();
        _pet.StartPolling();
    }

    /// <summary>共享 LLM 调用管线（Tab 发送与桌宠气泡共用）。</summary>
    private async Task<string> AskAiAsync(string msg)
    {
        try
        {
            var provider = BuildLlm();
            if (provider is LlmClient cloud && !cloud.IsConfigured)
                return "（未配置云端，请在 AI 伙伴页配置或切本地 Ollama）";
            var request = new ChatRequest
            {
                UserMessage = msg,
                SystemPrompt = string.IsNullOrWhiteSpace(_settings.AiSystemPrompt)
                    ? CharacterPresets.GetPrompt(_settings.AiCharacter) : _settings.AiSystemPrompt,
                Temperature = 0.7,
            };
            if (_settings.AiToolsEnabled)
                request.Tools = ToolRegistry.All().ToList();
            var reply = await provider.CompleteAsync(request);
            if (reply is null) return "（无回复）";
            if (reply.ToolCalls is { Count: > 0 })
                foreach (var call in reply.ToolCalls)
                    await ExecuteToolAsync(call);
            return reply.Text ?? "（已执行工具）";
        }
        catch (Exception ex) { return "错误：" + ex.Message; }
    }

    private void ClosePet()
    {
        try { _pet?.Close(); } catch { }
        _pet = null;
    }

    private void OnPetScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        try
        {
            _settings.PetScale = e.NewValue;
            PetScaleValue.Text = e.NewValue.ToString("0.0");
            _settingsService.Save();
            if (_pet is not null)
            {
                _pet.Width = 120 * e.NewValue;
                _pet.Height = 120 * e.NewValue;
            }
        }
        catch { }
    }

    // ================= 模块 2 AI 伙伴 =================

    private void InitAiTab()
    {
        try
        {
            AiBaseUrlBox.Text = _settings.AiBaseUrl;
            AiApiKeyBox.Password = _settings.AiApiKey;
            AiModelBox.Text = _settings.AiModel;
            AiTtsCheck.IsChecked = _settings.AiTtsEnabled;
            AiSttCheck.IsChecked = _settings.AiSttEnabled;
            AiEnabledCheck.IsChecked = _settings.AiEnabled;
            AiToolsCheck.IsChecked = _settings.AiToolsEnabled;
            AiChannelCombo.SelectedIndex = _settings.AiChannel == "Cloud" ? 1 : 0;

            AiCharacterCombo.Items.Clear();
            foreach (var (name, _) in CharacterPresets.All)
                AiCharacterCombo.Items.Add(name);
            if (AiCharacterCombo.Items.Contains(_settings.AiCharacter))
                AiCharacterCombo.SelectedItem = _settings.AiCharacter;
            else if (AiCharacterCombo.Items.Count > 0)
                AiCharacterCombo.SelectedIndex = 0;

            RegisterTools();
        }
        catch { }
    }

    private void OnAiChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiChannelCombo.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            _settings.AiChannel = tag;
    }

    private async void OnOllamaCheckClick(object sender, RoutedEventArgs e)
    {
        try
        {
            AiStatusText.Text = "正在检测 Ollama…";
            var ollama = new OllamaClient();
            bool ok = await ollama.CheckAsync();
            if (!ok)
            {
                AiStatusText.Text = "未检测到 Ollama（http://localhost:11434）。请安装：官网下载或 `brew install ollama`，然后 `ollama serve` 并 `ollama pull qwen2.5:7b`。";
                return;
            }
            var models = await ollama.ListModelsAsync();
            OllamaModelCombo.Items.Clear();
            foreach (var m in models) OllamaModelCombo.Items.Add(m);
            AiStatusText.Text = models.Count > 0 ? $"Ollama 已就绪，模型：{string.Join(", ", models)}" : "Ollama 已运行，但未拉取模型（请 `ollama pull`）";
        }
        catch (Exception ex) { AiStatusText.Text = "检测异常：" + ex.Message; }
    }

    /// <summary>注册白名单工具（≥5），仅本机执行与日志，危险写操作不提供。</summary>
    private void RegisterTools()
    {
        try
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "media_control",
                Description = "控制媒体播放：上一首/播放暂停/下一首",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"prev\",\"playpause\",\"next\"]}}}",
            });
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "system_info",
                Description = "只读：查看 CPU/内存/磁盘概览",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
            });
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "open_path",
                Description = "用系统默认程序打开文件/文件夹/URL（非可执行文件）",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
                RequiresConfirm = true,
            });
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "screenshot",
                Description = "截取当前屏幕并保存到 %AppData%\\DogDesk\\screenshots\\",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
            });
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "remind",
                Description = "设置一个延时提醒（秒数+文案）",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"seconds\":{\"type\":\"number\"},\"text\":{\"type\":\"string\"}},\"required\":[\"seconds\",\"text\"]}",
                RequiresConfirm = true,
            });
        }
        catch { }
    }

    private Task<string> ExecuteToolAsync(ToolCall call)
    {
        try
        {
            string args = call.Arguments ?? "{}";
            Log($"[AI工具] 桌宠调用了 {call.Name}（参数 {args}）");
            string result = call.Name switch
            {
                "media_control" => ExecMediaControl(args),
                "system_info" => ExecSystemInfo(),
                "open_path" => ExecOpenPath(args),
                "screenshot" => ExecScreenshot(),
                "remind" => ExecRemind(args),
                _ => "未知工具",
            };
            Log($"[AI工具] {call.Name} → {result}");
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            string err = "工具执行失败：" + ex.Message;
            Log("[AI工具] " + err);
            return Task.FromResult(err);
        }
    }

    private static JsonElement Parse(string args)
    {
        try { return JsonDocument.Parse(args).RootElement; } catch { return default; }
    }

    private string ExecMediaControl(string args)
    {
        string action = Parse(args).TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        switch (action)
        {
            case "prev": _session.Previous(); return "已切换上一首";
            case "next": _session.Next(); return "已切换下一首";
            case "playpause": _session.TogglePlayPause(); return _session.IsPlaying ? "已暂停" : "已播放";
            default: return "未知动作（仅支持 prev/playpause/next）";
        }
    }

    private string ExecSystemInfo()
    {
        try
        {
            long memMb = GC.GetTotalMemory(false) / 1024 / 1024;
            var drives = System.IO.DriveInfo.GetDrives();
            string disk = drives.Length > 0 ? $"{drives[0].AvailableFreeSpace / 1024 / 1024}MB 可用 / {drives[0].TotalSize / 1024 / 1024}MB" : "无";
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            string batt = power.BatteryLifePercent.ToString("0%");
            return $"进程托管内存 {memMb}MB；系统盘 {disk}；电池 {batt}";
        }
        catch (Exception ex) { return "读取系统信息失败：" + ex.Message; }
    }

    private string ExecOpenPath(string args)
    {
        string path = Parse(args).TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        if (path.Length == 0) return "缺少 path 参数";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
            return $"已打开：{path}";
        }
        catch (Exception ex) { return "打开失败：" + ex.Message; }
    }

    private string ExecScreenshot()
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogDesk", "screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size);
            }
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return "已保存截图：" + file;
        }
        catch (Exception ex) { return "截图失败：" + ex.Message; }
    }

    private string ExecRemind(string args)
    {
        try
        {
            var el = Parse(args);
            double minutes = el.TryGetProperty("seconds", out var s) ? s.GetDouble() / 60.0 : 1;
            string text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "提醒";
            System.Threading.Timer? tmr = null;
            tmr = new System.Threading.Timer(_ =>
            {
                try { Dispatcher.BeginInvoke(() => MessageBox.Show(text, "dogdesk 提醒")); } catch { }
                tmr?.Dispose();
            }, null, TimeSpan.FromMinutes(Math.Max(0.01, minutes)), TimeSpan.FromMilliseconds(-1));
            return $"已设置 {minutes:0.#} 分钟后提醒";
        }
        catch (Exception ex) { return "设置提醒失败：" + ex.Message; }
    }

    private void OnAiToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            bool on = AiEnabledCheck.IsChecked == true;
            if (on)
            {
                var c = MessageBox.Show(
                    "AI 伙伴将把你输入的对话发送到你自行配置的第三方 LLM 服务，由你自行负责。\n本应用不收集、不上传额外数据。是否启用？",
                    "AI 伙伴隐私确认", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (c != MessageBoxResult.Yes) { AiEnabledCheck.IsChecked = false; on = false; }
            }
            _settings.AiEnabled = on;
            AiSendButton.IsEnabled = on;
            SaveAiConfig();
        }
        catch { }
    }

    private void OnAiCharacterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiCharacterCombo.SelectedItem is string name)
            _settings.AiCharacter = name;
    }

    private void SaveAiConfig()
    {
        try
        {
            _settings.AiBaseUrl = AiBaseUrlBox.Text?.Trim() ?? "";
            _settings.AiApiKey = AiApiKeyBox.Password;
            _settings.AiModel = AiModelBox.Text?.Trim() ?? "";
            _settings.AiTtsEnabled = AiTtsCheck.IsChecked == true;
            _settings.AiSttEnabled = AiSttCheck.IsChecked == true;
            _settings.AiToolsEnabled = AiToolsCheck.IsChecked == true;
            if (AiChannelCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
                _settings.AiChannel = tag;
            _settingsService.Save();
        }
        catch { }
    }

    private IChatProvider BuildLlm()
    {
        string sys = string.IsNullOrWhiteSpace(_settings.AiSystemPrompt)
            ? CharacterPresets.GetPrompt(_settings.AiCharacter)
            : _settings.AiSystemPrompt;

        // 双通道：本地 Ollama（默认，离线）优先，否则云端 OpenAI 兼容
        if (_settings.AiChannel == "Cloud")
        {
            return new LlmClient
            {
                BaseUrl = _settings.AiBaseUrl,
                ApiKey = _settings.AiApiKey,
                Model = _settings.AiModel,
                SystemPrompt = sys,
            };
        }
        return new OllamaClient();
    }

    private async void OnAiSendClick(object sender, RoutedEventArgs e) => await SendAiAsync();

    private void OnAiInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SendAiAsync();
    }

    private async Task SendAiAsync()
    {
        try
        {
            SaveAiConfig();
            string msg = AiInputBox.Text?.Trim() ?? "";
            if (msg.Length == 0) return;
            var provider = BuildLlm();
            if (provider is LlmClient cloud && !cloud.IsConfigured)
            {
                AiStatusText.Text = "云端通道未配置 Base URL / API Key，或切回本地 Ollama";
                return;
            }

            AddChat("我", msg);
            AiInputBox.Text = "";
            AiStatusText.Text = "思考中…";

            var request = new ChatRequest
            {
                UserMessage = msg,
                SystemPrompt = string.IsNullOrWhiteSpace(_settings.AiSystemPrompt)
                    ? CharacterPresets.GetPrompt(_settings.AiCharacter) : _settings.AiSystemPrompt,
                Temperature = 0.7,
            };
            if (_settings.AiToolsEnabled)
                request.Tools = ToolRegistry.All().ToList();

            var reply = await provider.CompleteAsync(request);
            if (reply is null) { AiStatusText.Text = "未收到回复（接口/服务异常）"; return; }

            // 工具调用回灌（最多一轮）
            if (reply.ToolCalls is { Count: > 0 })
            {
                foreach (var call in reply.ToolCalls)
                {
                    var (ok, err) = ToolRegistry.ValidateJson(call.Arguments ?? "");
                    if (!ok) { Log($"[AI工具] 参数校验失败 {call.Name}：{err}"); continue; }
                    var desc = ToolRegistry.Get(call.Name);
                    if (desc?.RequiresConfirm == true)
                    {
                        var c = MessageBox.Show($"桌宠请求执行工具「{call.Name}」，参数：{call.Arguments}\n是否允许？",
                            "工具执行确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (c != MessageBoxResult.Yes) continue;
                    }
                    string result = await ExecuteToolAsync(call);
                    AddChat("工具", $"{call.Name} → {result}");
                }
            }

            string text = reply.Text ?? "(无文本回复)";
            AddChat(_settings.AiCharacter, text);
            AiStatusText.Text = "";
            if (_settings.AiTtsEnabled) Speak(text);
        }
        catch (Exception ex) { AiStatusText.Text = "AI 异常：" + ex.Message; }
    }

    private void AddChat(string who, string text)
    {
        _chat.Add((who, text));
        if (_chat.Count > 40) _chat.RemoveAt(0);
        AiChatList.Items.Add($"{who}：{text}");
        AiChatList.ScrollIntoView(AiChatList.Items[^1]);
    }

    private void Speak(string text)
    {
        try
        {
            using var tts = new System.Speech.Synthesis.SpeechSynthesizer();
            tts.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "TTS 不可用（缺语音包）：" + ex.Message;
        }
    }

    // ================= 模块 4 开机动画 =================

    private void InitBootTab()
    {
        try
        {
            BootAnimEnabledCheck.IsChecked = _settings.BootAnimEnabled;
            BootAnimStyleCombo.SelectedIndex = _settings.BootAnimStyle switch
            {
                "Gradient" => 1,
                "FlowText" => 2,
                _ => 0,
            };
        }
        catch { }
    }

    private void OnBootAnimStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        _settings.BootAnimStyle = BootAnimStyleCombo.SelectedIndex switch
        {
            1 => "Gradient",
            2 => "FlowText",
            _ => "Aurora",
        };
        _settingsService.Save();
    }

    private void OnBootTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var w = new BootAnimWindow(_settings.BootAnimDurationSec, _settings.BootAnimStyle, _settings.BootAnimPath);
            w.Show();
        }
        catch (Exception ex) { BootStatusText.Text = "预览失败：" + ex.Message; }
    }

    /// <summary>App 启动时调用：非模态展示开机动画，不阻塞主窗口。</summary>
    public void ShowBootAnimIfEnabled()
    {
        try
        {
            if (!_settings.BootAnimEnabled) return;
            var w = new BootAnimWindow(_settings.BootAnimDurationSec, _settings.BootAnimStyle, _settings.BootAnimPath);
            w.Show();
        }
        catch { }
    }

    // ================= ZenDesktop 视觉效果（实验性） =================

    private void OnZenApplyClick(object sender, RoutedEventArgs e)
    {
        RunZenScript("scripts/zen-desktop/apply.ps1", "应用 ZenDesktop 视觉效果");
    }

    private void OnZenUninstallClick(object sender, RoutedEventArgs e)
    {
        RunZenScript("scripts/zen-desktop/uninstall.ps1", "卸载 ZenDesktop 视觉效果");
    }

    private void RunZenScript(string script, string label)
    {
        try
        {
            var c = MessageBox.Show(
                $"{label}：将通过提权 PowerShell 调用 Windhawk CLI 修改 Explorer 进程，可能影响稳定性。\n建议先创建系统还原点。是否继续？",
                "视觉效果（实验性）风险确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (c != MessageBoxResult.Yes) return;
            string content = System.IO.File.Exists(script) ? System.IO.File.ReadAllText(script) :
                "# 脚本缺失：" + script;
            var (ok, output) = ElevationRunner.Run(content, elevated: true, logTag: "ZenDesktop");
            Log(ok ? $"[ZenDesktop] {label} 完成" : $"[ZenDesktop] {label} 失败：{output}");
        }
        catch (Exception ex) { Log("[ZenDesktop] 异常：" + ex.Message); }
    }
}
