using System.Runtime.InteropServices;

namespace DogDesk.Mac.Services.Mpv;

/// <summary>
/// macOS 系统 libmpv.dylib 的运行时动态绑定。
/// 不使用 DllImport（NuGet 的 Endpne.LibMPV 仅有 Windows 包），
/// 而是 NativeLibrary.Load 依次尝试常见 Homebrew/系统路径，再用 NativeLibrary.GetExport
/// 取出函数指针转为委托，彻底避开 dylib 名解析问题。
/// </summary>
internal static class MpvLib
{
    // ---- 函数签名委托 ----
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr mpv_create_del();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_initialize_del(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void mpv_terminate_destroy_del(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void mpv_free_del(IntPtr ptr);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr mpv_error_string_del(int err);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_set_option_string_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_command_del(IntPtr ctx, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_command_string_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string args);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr mpv_get_property_string_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_set_property_string_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_get_property_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_set_property_del(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int mpv_observe_property_del(IntPtr ctx, ulong replyUserData,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr mpv_wait_event_del(IntPtr ctx, double timeoutSeconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void mpv_wakeup_del(IntPtr ctx);

    // ---- 委托实例 ----
    public static mpv_create_del Create { get; private set; } = null!;
    public static mpv_initialize_del Initialize { get; private set; } = null!;
    public static mpv_terminate_destroy_del TerminateDestroy { get; private set; } = null!;
    public static mpv_free_del Free { get; private set; } = null!;
    public static mpv_error_string_del ErrorString { get; private set; } = null!;
    public static mpv_set_option_string_del SetOptionString { get; private set; } = null!;
    public static mpv_command_del Command { get; private set; } = null!;
    public static mpv_command_string_del CommandString { get; private set; } = null!;
    public static mpv_get_property_string_del GetPropertyString { get; private set; } = null!;
    public static mpv_set_property_string_del SetPropertyString { get; private set; } = null!;
    public static mpv_get_property_del GetProperty { get; private set; } = null!;
    public static mpv_set_property_del SetProperty { get; private set; } = null!;
    public static mpv_observe_property_del ObserveProperty { get; private set; } = null!;
    public static mpv_wait_event_del WaitEvent { get; private set; } = null!;
    public static mpv_wakeup_del Wakeup { get; private set; } = null!;

    public static bool IsLoaded { get; private set; }
    public static string LoadedPath { get; private set; } = "";

    // mpv_format
    public const int FormatNone = 0;
    public const int FormatString = 1;
    public const int FormatFlag = 3;
    public const int FormatInt64 = 4;
    public const int FormatDouble = 5;

    // mpv_event_id
    public const int EventNone = 0;
    public const int EventShutdown = 1;
    public const int EventStartFile = 6;
    public const int EventEndFile = 7;
    public const int EventFileLoaded = 8;
    public const int EventPropertyChange = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    /// <summary>按优先级探测并加载 libmpv.dylib。成功返回 true。</summary>
    public static bool TryLoad(out string message)
    {
        if (IsLoaded) { message = LoadedPath; return true; }

        string[] candidates =
        {
            "/opt/homebrew/lib/libmpv.dylib",   // Apple Silicon Homebrew
            "/usr/local/lib/libmpv.dylib",      // Intel Homebrew
            "/usr/lib/libmpv.dylib",            // 系统路径兜底
        };

        // 以可用路径显式 Load 并保存句柄（GetExport 需要句柄）
        IntPtr handle = IntPtr.Zero;
        string? used = null;
        foreach (string path in candidates)
        {
            try
            {
                handle = NativeLibrary.Load(path);
                if (handle != IntPtr.Zero) { used = path; break; }
            }
            catch
            {
                // 路径不可用
            }
        }

        if (handle == IntPtr.Zero)
        {
            IsLoaded = false;
            message = "未找到 libmpv.dylib（请先 brew install mpv）";
            return false;
        }

        try
        {
            Bind(handle);
        }
        catch (Exception ex)
        {
            message = $"libmpv 导出函数缺失：{ex.Message}";
            return false;
        }

        IsLoaded = true;
        LoadedPath = used!;
        message = $"libmpv 已加载：{used}";
        return true;
    }

    private static void Bind(IntPtr h)
    {
        Create = Get<mpv_create_del>(h, "mpv_create");
        Initialize = Get<mpv_initialize_del>(h, "mpv_initialize");
        TerminateDestroy = Get<mpv_terminate_destroy_del>(h, "mpv_terminate_destroy");
        Free = Get<mpv_free_del>(h, "mpv_free");
        ErrorString = Get<mpv_error_string_del>(h, "mpv_error_string");
        SetOptionString = Get<mpv_set_option_string_del>(h, "mpv_set_option_string");
        Command = Get<mpv_command_del>(h, "mpv_command");
        CommandString = Get<mpv_command_string_del>(h, "mpv_command_string");
        GetPropertyString = Get<mpv_get_property_string_del>(h, "mpv_get_property_string");
        SetPropertyString = Get<mpv_set_property_string_del>(h, "mpv_set_property_string");
        GetProperty = Get<mpv_get_property_del>(h, "mpv_get_property");
        SetProperty = Get<mpv_set_property_del>(h, "mpv_set_property");
        ObserveProperty = Get<mpv_observe_property_del>(h, "mpv_observe_property");
        WaitEvent = Get<mpv_wait_event_del>(h, "mpv_wait_event");
        Wakeup = Get<mpv_wakeup_del>(h, "mpv_wakeup");
    }

    private static T Get<T>(IntPtr h, string name) where T : Delegate
    {
        IntPtr ptr = NativeLibrary.GetExport(h, name);
        if (ptr == IntPtr.Zero) throw new EntryPointNotFoundException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public static string? PtrToStringUtf8AndFree(IntPtr ctx, IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        string s = Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        Free(ptr);
        return s;
    }
}
