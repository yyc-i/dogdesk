using DogDesk.Models;

namespace DogDesk.Services.Playback;

/// <summary>播放队列循环模式。</summary>
public enum RepeatMode { Off, All, One }

/// <summary>
/// 纯逻辑播放队列（线程安全）：本地曲库 / 在线音源 / Navidrome 共用。
/// 不感知具体播放内核，由平台侧（WPF/Avalonia）把 <see cref="Current"/> 交给 libmpv 播放。
/// </summary>
public sealed class PlaybackQueue
{
    private readonly object _lock = new();
    private readonly List<MediaItem> _items = new();
    private int _current = -1;

    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    /// <summary>当前条目变化（含 null=停止）。</summary>
    public event Action<MediaItem?>? CurrentChanged;
    /// <summary>队列内容变化（加载/增删/清空）。</summary>
    public event Action? QueueChanged;

    public IReadOnlyList<MediaItem> Items
    {
        get { lock (_lock) return _items.ToArray(); }
    }

    public MediaItem? Current
    {
        get { lock (_lock) return _current >= 0 && _current < _items.Count ? _items[_current] : null; }
    }

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    /// <summary>整表加载并从 startIndex 开始播放；空队列时仅广播变更。</summary>
    public void Load(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        List<MediaItem> list;
        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(items.Where(i => i.IsValid));
            _current = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
            list = _items.ToList();
        }
        QueueChanged?.Invoke();
        CurrentChanged?.Invoke(Current);
    }

    public void Enqueue(MediaItem item)
    {
        if (!item.IsValid) return;
        lock (_lock) _items.Add(item);
        QueueChanged?.Invoke();
    }

    public void PlayAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count) return;
            _current = index;
        }
        CurrentChanged?.Invoke(Current);
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count) return;
            bool wasCurrent = index == _current;
            _items.RemoveAt(index);
            if (wasCurrent)
                _current = _items.Count == 0 ? -1 : Math.Min(_current, _items.Count - 1);
            else if (_current > index) _current--;
        }
        QueueChanged?.Invoke();
        if (_current < 0) CurrentChanged?.Invoke(null);
    }

    /// <summary>把条目插到当前曲目之后（"下一首播放"）；无当前曲目时追加到队尾。</summary>
    public void InsertAfterCurrent(MediaItem item)
    {
        if (!item.IsValid) return;
        lock (_lock)
        {
            if (_current >= 0 && _current < _items.Count)
                _items.Insert(_current + 1, item);
            else
                _items.Add(item);
        }
        QueueChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            _current = -1;
        }
        QueueChanged?.Invoke();
        CurrentChanged?.Invoke(null);
    }

    /// <summary>尝试取下一首；RepeatMode.One 时返回当前（由调用方决定重播）。返回 null 表示无下一首。</summary>
    public MediaItem? MoveNext()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return null;
            if (Repeat == RepeatMode.One && _current >= 0) return _items[_current];
            if (Repeat == RepeatMode.All && _current >= _items.Count - 1) { _current = 0; return _items[_current]; }
            if (_current >= _items.Count - 1) return null;
            _current++;
            return _items[_current];
        }
    }

    /// <summary>尝试取上一首（RepeatMode.Off 时回到队首即停止前移）。</summary>
    public MediaItem? MovePrevious()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return null;
            if (Repeat == RepeatMode.All && _current <= 0) { _current = _items.Count - 1; return _items[_current]; }
            if (_current <= 0) { _current = 0; return _items[0]; }
            _current--;
            return _items[_current];
        }
    }

    /// <summary>播完一首后由调用方调用：决定是否自动续播（All/One）。</summary>
    public bool TryAutoAdvance(out MediaItem? next)
    {
        if (Repeat == RepeatMode.Off)
        {
            next = null;
            return false;
        }
        next = MoveNext();
        return next is not null;
    }
}
