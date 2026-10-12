using System.Runtime.InteropServices;

namespace DogDesk.Services.Mpv;

/// <summary>
/// libmpv 2.x client API 的薄 P/Invoke 绑定（只覆盖壁纸播放所需的子集）。
/// native dll 由 Endpne.LibMPV.Windows NuGet 包输出到 libmpv/win-x64/libmpv-2.dll，
/// 启动时由 <see cref="MpvNativeLoader"/> 显式预加载，之后这里的 DllImport 按 dll 名命中已加载模块。
/// </summary>
internal static class MpvNative
{
    private const string Dll = "libmpv-2.dll";

    [DllImport(Dll, EntryPoint = "mpv_client_api_version")]
    public static extern uint ClientApiVersion();

    [DllImport(Dll, EntryPoint = "mpv_error_string")]
    public static extern IntPtr ErrorString(int error);

    [DllImport(Dll, EntryPoint = "mpv_create")]
    public static extern IntPtr Create();

    [DllImport(Dll, EntryPoint = "mpv_initialize")]
    public static extern int Initialize(IntPtr ctx);

    [DllImport(Dll, EntryPoint = "mpv_terminate_destroy")]
    public static extern void TerminateDestroy(IntPtr ctx);

    [DllImport(Dll, EntryPoint = "mpv_free")]
    public static extern void Free(IntPtr ptr);

    [DllImport(Dll, EntryPoint = "mpv_set_option_string", CharSet = CharSet.Unicode)]
    public static extern int SetOptionString(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? data);

    [DllImport(Dll, EntryPoint = "mpv_command")]
    public static extern int Command(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPUTF8Str)] string?[] args);

    [DllImport(Dll, EntryPoint = "mpv_command_string")]
    public static extern int CommandString(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

    [DllImport(Dll, EntryPoint = "mpv_get_property_string")]
    public static extern IntPtr GetPropertyStringNative(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Dll, EntryPoint = "mpv_set_property_string")]
    public static extern int SetPropertyString(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    // —— 类型化属性读写（音频内核用于 time-pos / pause / duration 的直接读写）——

    [DllImport(Dll, EntryPoint = "mpv_get_property")]
    public static extern int GetProperty(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    [DllImport(Dll, EntryPoint = "mpv_set_property")]
    public static extern int SetProperty(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref double data);

    [DllImport(Dll, EntryPoint = "mpv_get_property")]
    public static extern int GetPropertyFlag(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out int data);

    [DllImport(Dll, EntryPoint = "mpv_set_property")]
    public static extern int SetPropertyFlag(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref int data);

    /// <summary>读取 double 属性；失败返回 fallback。</summary>
    public static double GetPropertyDouble(IntPtr ctx, string name, double fallback = 0)
        => GetProperty(ctx, name, MpvFormat.Double, out double v) < 0 ? fallback : v;

    [DllImport(Dll, EntryPoint = "mpv_observe_property")]
    public static extern int ObserveProperty(IntPtr ctx, ulong replyUserData,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Dll, EntryPoint = "mpv_wait_event")]
    public static extern IntPtr WaitEvent(IntPtr ctx, double timeoutSeconds);

    [DllImport(Dll, EntryPoint = "mpv_wakeup")]
    public static extern void Wakeup(IntPtr ctx);

    [DllImport(Dll, EntryPoint = "mpv_request_log_messages")]
    public static extern int RequestLogMessages(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);

    /// <summary>把 mpv 返回的 char* 复制为托管字符串并释放。</summary>
    public static string? StringFromMpvAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        string s = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        Free(ptr);
        return s;
    }

    public static string? GetPropertyString(IntPtr ctx, string name)
        => StringFromMpvAndFree(GetPropertyStringNative(ctx, name));
}

/// <summary>mpv_format：属性/事件数据格式。</summary>
internal static class MpvFormat
{
    public const int None = 0;
    public const int String = 1;
    public const int OsdString = 2;
    public const int Flag = 3;
    public const int Int64 = 4;
    public const int Double = 5;
    public const int Node = 6;
}

/// <summary>mpv_event_id：事件类型。</summary>
internal static class MpvEventId
{
    public const int None = 0;
    public const int Shutdown = 1;
    public const int LogMessage = 2;
    public const int GetPropertyReply = 3;
    public const int SetPropertyReply = 4;
    public const int CommandReply = 5;
    public const int StartFile = 6;
    public const int EndFile = 7;
    public const int FileLoaded = 8;
    public const int ClientMessage = 9;
    public const int VideoReconfig = 10;
    public const int AudioReconfig = 11;
    public const int Seek = 12;
    public const int PlaybackRestart = 13;
    public const int PropertyChange = 14;
    public const int QueueOverflow = 15;
    public const int Hook = 16;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public int EventId;
    public int Error;
    public ulong ReplyUserdata;
    public IntPtr Data;
}

/// <summary>MPV_EVENT_PROPERTY_CHANGE 的 data 负载。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public IntPtr Name; // char*
    public int Format;
    public IntPtr Data; // 指向对应格式的数据；DOUBLE→double*，STRING→char**
}
