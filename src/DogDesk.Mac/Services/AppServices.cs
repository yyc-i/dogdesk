using DogDesk.Mac.Services.Mpv;
using DogDesk.Models;
using DogDesk.Services;
using DogDesk.Services.Ai;
using DogDesk.Services.BootAnim;
using DogDesk.Services.Download;
using DogDesk.Services.Effect;
using DogDesk.Services.Equalizer;
using DogDesk.Services.MusicSource;
using DogDesk.Services.Playback;
using DogDesk.Services.Playlists;

namespace DogDesk.Mac.Services;

/// <summary>
/// 应用协调层（组合根）：把 <see cref="MpvAudioPlayer"/>、<see cref="PlaybackQueue"/>、
/// <see cref="LyricsService"/>、在线音源与均衡器串成可播放的整体。
/// UI 只与本类交互，不直接操作 mpv。
/// </summary>
public sealed class AppServices
{
    public SettingsService Settings { get; } = new();
    public MpvAudioPlayer Player { get; }
    public PlaybackQueue Queue { get; } = new();
    public LyricsService Lyrics { get; } = new();
    public NeteaseMusicSource Netease { get; } = new();
    public NavidromeClient Navidrome { get; } = new();
    public RadioService Radio { get; }
    public List<DogDeskPlaylist> Playlists { get; private set; }

    /// <summary>下载管理器（v0.8）：aria2 内核封装，UI 经 <see cref="AppServices"/> 间接访问。</summary>
    public DownloadManager Downloads { get; }

    /// <summary>播放状态变化（mpv 属性 / 队列切换 / 封面就绪），UI 刷新入口。</summary>
    public event Action? StateChanged;

    /// <summary>日志行（设置页/调试用）。</summary>
    public event Action<string>? Log;

    public AppServices()
    {
        Player = new MpvAudioPlayer();
        Player.Changed += () => StateChanged?.Invoke();
        Player.Ended += OnPlayerEnded;

        Queue.CurrentChanged += OnQueueCurrentChanged;

        Lyrics.Log += msg => Log?.Invoke(msg);

        // 还原队列循环模式
        Queue.Repeat = RepeatMode.Off;

        // 凭据回填 Navidrome
        if (!string.IsNullOrWhiteSpace(Settings.Current.NavidromeServer))
            Navidrome.Configure(
                Settings.Current.NavidromeServer,
                Settings.Current.NavidromeUser,
                Settings.Current.NavidromePassword);

        // 歌单（含「我的收藏」）与电台
        Playlists = PlaylistStore.EnsureFavorites(PlaylistStore.Load());
        Radio = new RadioService(Netease);

        // v0.8 下载管理（aria2 内核，初始化由 UI 在打开主窗口后异步触发）
        Downloads = new DownloadManager(Settings);

        ApplyAudioChain();
        ApplySpeed();
    }

    public bool MpvAvailable => Player.Available;
    public string MpvStatus => Player.Status;

    /// <summary>用一组曲目加载队列并从 index 开始播放。</summary>
    public void PlayList(IEnumerable<MediaItem> items, int index)
    {
        Queue.Load(items, index);
    }

    /// <summary>播放队列中指定索引（双击列表项时调用）。</summary>
    public void PlayAt(int index) => Queue.PlayAt(index);

    public void ToggleRepeat()
    {
        Queue.Repeat = Queue.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        StateChanged?.Invoke();
    }

    public void TogglePlayPause() => Player.TogglePlayPause();
    public void Seek(double fraction) => Player.Seek(fraction);

    public void Next()
    {
        var next = Queue.MoveNext();
        if (next is not null) Queue.PlayAt(IndexOf(next));
    }

    public void Previous()
    {
        var prev = Queue.MovePrevious();
        if (prev is not null) Queue.PlayAt(IndexOf(prev));
    }

    /// <summary>根据当前设置重建「均衡器 + 3D 环绕」合并滤镜链。</summary>
    public void ApplyAudioChain()
    {
        var eq = EqualizerPreset.From(Settings.Current.EqualizerGains);
        if (!Settings.Current.EqualizerEnabled) eq = new EqualizerPreset(); // 均衡器关闭→平直
        var surround = SurroundEffect.FromSettings(
            Settings.Current.SurroundEnabled,
            Settings.Current.SurroundWidth,
            Settings.Current.SurroundSpeed);
        string chain = EffectAf.BuildChain(eq, surround);
        Player.ApplyAf(chain);
    }

    /// <summary>兼容旧调用：均衡器变化后重建整条音频链。</summary>
    public void ApplyEqualizer() => ApplyAudioChain();

    /// <summary>应用倍速到 mpv（speed 属性）。</summary>
    public void ApplySpeed() => Player.SetSpeed(Settings.Current.PlaybackSpeed);

    // ============ 歌单 / 收藏 ============
    private DogDeskPlaylist Favorites =>
        Playlists.First(p => p.Name == PlaylistStore.FavoritesName);

    /// <summary>把当前曲目加入/移出「我的收藏」，返回是否已收藏。</summary>
    public bool ToggleFavoriteCurrent()
    {
        var cur = Queue.Current;
        if (cur is null) return false;
        var fav = Favorites;
        int exist = fav.Items.FindIndex(i => i.Uri == cur.Uri);
        if (exist >= 0) fav.Items.RemoveAt(exist);
        else fav.Items.Add(cur);
        SavePlaylists();
        return exist < 0;
    }

    public bool IsCurrentFavorited()
    {
        var cur = Queue.Current;
        return cur is not null && Favorites.Items.Any(i => i.Uri == cur.Uri);
    }

    public void AddToPlaylist(string name, MediaItem item)
    {
        var pl = Playlists.FirstOrDefault(p => p.Name == name);
        if (pl is null) { pl = new DogDeskPlaylist { Name = name }; Playlists.Add(pl); }
        pl.Items.Add(item);
        SavePlaylists();
    }

    public void CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Playlists.All(p => p.Name != name))
        {
            Playlists.Add(new DogDeskPlaylist { Name = name });
            SavePlaylists();
        }
    }

    public void RemoveFromPlaylist(string name, MediaItem item)
    {
        var pl = Playlists.FirstOrDefault(p => p.Name == name);
        pl?.Items.RemoveAll(i => i.Uri == item.Uri);
        SavePlaylists();
    }

    private void SavePlaylists() => PlaylistStore.Save(Playlists);

    // ============ AI 伙伴（v0.7） ============
    /// <summary>按当前设置构建 LLM 客户端；系统提示词优先用自定义，否则取角色预设。</summary>
    public LlmClient BuildLlmClient()
    {
        var s = Settings.Current;
        string sys = !string.IsNullOrWhiteSpace(s.AiSystemPrompt)
            ? s.AiSystemPrompt
            : CharacterPresets.GetPrompt(s.AiCharacter);
        return new LlmClient
        {
            BaseUrl = s.AiBaseUrl,
            ApiKey = s.AiApiKey,
            Model = s.AiModel,
            SystemPrompt = sys,
        };
    }

    /// <summary>双通道聊天 provider（v0.7）：Local=Ollama 本地，Cloud=OpenAI 兼容云端。</summary>
    public IChatProvider BuildChatProvider()
    {
        var s = Settings.Current;
        if (string.Equals(s.AiChannel, "Cloud", StringComparison.OrdinalIgnoreCase))
            return BuildLlmClient();
        return new OllamaClient();
    }

    /// <summary>当前 channel 下对话使用的模型名（云端取 AiModel，本地取 AiLocalModel）。</summary>
    public string CurrentChatModel()
    {
        var s = Settings.Current;
        return string.Equals(s.AiChannel, "Cloud", StringComparison.OrdinalIgnoreCase)
            ? s.AiModel
            : s.AiLocalModel;
    }

    // ============ 开机动画素材库（v0.7） ============
    public List<BootAnimEntry> LoadBootAnimLibrary() => BootAnimStore.Load();
    public void SaveBootAnimLibrary(List<BootAnimEntry> entries) => BootAnimStore.Save(entries);

    /// <summary>导入一张图片到素材库目录并登记；返回新条目，失败返回 null。</summary>
    public BootAnimEntry? ImportBootAnimImage(string srcPath)
    {
        try
        {
            Directory.CreateDirectory(BootAnimStore.RootDir);
            string ext = Path.GetExtension(srcPath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            string dest = Path.Combine(BootAnimStore.RootDir, $"import_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
            File.Copy(srcPath, dest, overwrite: true);
            var entry = new BootAnimEntry { Name = Path.GetFileNameWithoutExtension(srcPath), Kind = "Image", Path = dest };
            var list = LoadBootAnimLibrary();
            list.Add(entry);
            SaveBootAnimLibrary(list);
            return entry;
        }
        catch { return null; }
    }

    /// <summary>删除素材条目（文件 + 索引）。</summary>
    public void DeleteBootAnimEntry(BootAnimEntry entry)
    {
        try { if (File.Exists(entry.Path)) File.Delete(entry.Path); } catch { }
        var list = LoadBootAnimLibrary();
        list.RemoveAll(e => e.Path == entry.Path);
        SaveBootAnimLibrary(list);
    }

    /// <summary>用 ImageGenClient 生成一张开机动画素材并保存登记；返回新条目，失败返回 null。</summary>
    public async Task<BootAnimEntry?> GenerateBootAnimImageAsync(string prompt)
    {
        var s = Settings.Current;
        try
        {
            var client = new ImageGenClient
            {
                BaseUrl = s.AiBaseUrl,
                ApiKey = s.AiApiKey,
                Model = s.AiModel,
            };
            string? result = await client.GenerateImageAsync(prompt);
            if (string.IsNullOrWhiteSpace(result)) return null;

            byte[] bytes;
            if (result.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new System.Net.Http.HttpClient();
                bytes = await http.GetByteArrayAsync(result);
            }
            else
            {
                bytes = Convert.FromBase64String(result);
            }

            Directory.CreateDirectory(BootAnimStore.RootDir);
            string dest = Path.Combine(BootAnimStore.RootDir, $"aigen_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            await File.WriteAllBytesAsync(dest, bytes);

            var entry = new BootAnimEntry { Name = "AI 生成", Kind = "Image", Path = dest };
            var list = LoadBootAnimLibrary();
            list.Add(entry);
            SaveBootAnimLibrary(list);
            return entry;
        }
        catch { return null; }
    }

    // ============ 记忆播放 ============
    /// <summary>退出时保存队列与当前位置（若启用且有有效队列）。</summary>
    public void SaveOnExit()
    {
        // v0.8 下载内核退出（幂等，异常安全：不影响记忆播放保存）
        try { Downloads.Shutdown(); } catch { }

        if (!Settings.Current.ResumePlayback || Queue.Current is null) return;
        try
        {
            var data = new ResumeData
            {
                Queue = Queue.Items.ToList(),
                CurrentIndex = IndexOf(Queue.Current!),
                PositionSec = Player.PositionSec,
            };
            ResumeStore.Save(data);
        }
        catch { }
    }

    /// <summary>启动时恢复队列与位置；失败静默。返回是否恢复了有效队列。</summary>
    public bool RestoreOnStartup()
    {
        if (!Settings.Current.ResumePlayback) return false;
        var data = ResumeStore.Load();
        if (data is not { IsValid: true }) return false;
        try
        {
            Queue.Load(data.Queue, data.CurrentIndex);
            // 位置在曲目加载后由 UI 触发 seek（mpv duration 就绪）
            _pendingSeekSec = data.PositionSec;
            return true;
        }
        catch { return false; }
    }

    private double _pendingSeekSec;

    /// <summary>取出并清除待恢复的播放位置（秒）；无则返回 0。</summary>
    public double ConsumePendingSeek()
    {
        double v = _pendingSeekSec;
        _pendingSeekSec = 0;
        return v;
    }

    /// <summary>清空队列并停止。</summary>
    public void Stop() => Queue.Clear();

    private void OnQueueCurrentChanged(MediaItem? item)
    {
        if (item is null)
        {
            Player.SetTrackInfo("", "");
            Player.ThumbnailBytes = null;
            StateChanged?.Invoke();
            return;
        }

        Player.SetTrackInfo(item.Title, item.Artist);
        Player.Load(item.Uri);
        Player.Play();

        StateChanged?.Invoke();

        // 封面与歌词异步加载（不阻塞 UI）
        _ = LoadCoverAsync(item);
        _ = LoadLyricsAsync(item);
    }

    private async Task LoadCoverAsync(MediaItem item)
    {
        byte[]? cover = await CoverLoader.FetchBytesAsync(item).ConfigureAwait(true);
        if (Queue.Current == item)
        {
            Player.ThumbnailBytes = cover;
            StateChanged?.Invoke();
        }
    }

    private async Task LoadLyricsAsync(MediaItem item)
    {
        try
        {
            await Lyrics.TryFetchAsync(item.Title, item.Artist, item.DurationSec).ConfigureAwait(true);
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[歌词] 异常：{ex.Message}");
        }
    }

    private void OnPlayerEnded()
    {
        // 在 mpv 事件线程触发；切歌动作回到队列逻辑
        if (Queue.TryAutoAdvance(out MediaItem? next) && next is not null)
        {
            // 直接走队列切换
            Queue.PlayAt(IndexOf(next));
        }
    }

    private int IndexOf(MediaItem item)
    {
        var items = Queue.Items;
        for (int i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }

    public void Dispose() => Player.Dispose();
}
