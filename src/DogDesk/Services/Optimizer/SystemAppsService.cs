using System.Collections.Generic;
using System.Threading.Tasks;

namespace DogDesk.Services.Optimizer;

/// <summary>一条已安装 Appx 应用。</summary>
public sealed class AppxPackage
{
    public string Name { init; get; } = "";
    public string FullName { init; get; } = "";
    public string Publisher { init; get; } = "";
    /// <summary>是否为系统/预装关键包（黄色"不建议默认卸载"）。</summary>
    public bool SystemComponent { init; get; }

    public string Display => $"{Name}  {(SystemComponent ? "〔保留/谨慎〕" : "")}";
}

/// <summary>
/// 软件与应用管理：Get-AppxPackage 列表 / Remove-AppxPackage 卸载 / winget 搜索与安装。
/// 全部通过 PowerShell/winget.exe 进程调用，运行时才执行，不 hook、不驻留。
/// </summary>
public static class SystemAppsService
{
    /// <summary>列出当前用户已安装的 Appx 应用（只读，不提权）。</summary>
    public static async Task<List<AppxPackage>> ListAppxAsync()
    {
        var list = new List<AppxPackage>();
        var (_, output) = await Task.Run(() => ElevationRunner.Run(
            "Get-AppxPackage | Select-Object Name,PackageFullName,PublisherId,IsFramework | ConvertTo-Json -Compress",
            elevated: false));
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                    list.Add(ParseOne(root));
                return list;
            }
            foreach (var el in root.EnumerateArray())
                list.Add(ParseOne(el));
        }
        catch { /* 无输出/解析失败返回空 */ }
        return list;
    }

    private static AppxPackage ParseOne(System.Text.Json.JsonElement el)
    {
        string Get(string k) => el.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
        bool sys = el.TryGetProperty("IsFramework", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.True;
        return new AppxPackage
        {
            Name = Get("Name"),
            FullName = Get("PackageFullName"),
            Publisher = Get("PublisherId"),
            SystemComponent = sys || IsSystemName(Get("Name")),
        };
    }

    private static bool IsSystemName(string name) =>
        name.StartsWith("Microsoft.WindowsStore") || name.StartsWith("Microsoft.SecHealthUI")
        || name.StartsWith("Microsoft.Windows.ShellExperience") || name.StartsWith("Microsoft.UI")
        || name.StartsWith("Microsoft.WindowsCalculator") || name.StartsWith("Microsoft.Windows.Photos");

    /// <summary>卸载一个 Appx 包（当前用户）。</summary>
    public static Task<(bool Ok, string Output)> UninstallAppxAsync(string fullName) =>
        Task.Run(() => ElevationRunner.Run(
            $"Remove-AppxPackage -Package '{fullName}' -ErrorAction Continue", elevated: true));

    /// <summary>winget 搜索（只读）。</summary>
    public static async Task<string> WingetSearchAsync(string query)
    {
        var (_, output) = await Task.Run(() => ElevationRunner.Run(
            $"winget search \"{query}\" --disable-interactivity", elevated: false));
        return output;
    }

    /// <summary>winget 安装（提权）。</summary>
    public static Task<(bool Ok, string Output)> WingetInstallAsync(string id) =>
        Task.Run(() => ElevationRunner.Run(
            $"winget install --id \"{id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            elevated: true));
}
