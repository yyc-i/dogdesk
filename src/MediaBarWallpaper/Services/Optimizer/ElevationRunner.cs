using System.Diagnostics;
using System.IO;

namespace MediaBarWallpaper.Services.Optimizer;

/// <summary>
/// 提权 PowerShell 执行器（系统优化模块硬约束实现）：
/// 脚本写入临时 .ps1 → powershell.exe -ExecutionPolicy Bypass -File 执行；
/// 需要系统修改时以 Verb="runas" 触发 UAC 提权；stdout/stderr 重定向到临时文件后读取。
/// 绝不由后台/定时器自动调用，仅在用户显式勾选并点击执行时调用。
/// </summary>
public static class ElevationRunner
{
    /// <summary>当前进程是否具有管理员权限。</summary>
    public static bool IsAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo("whoami", "/groups")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            // S-1-5-32-544 = Administrators 组，且当前已启用（用 * 标识不严谨，这里只做近似判断）
            return outp.Contains("S-1-5-32-544");
        }
        catch { return false; }
    }

    /// <summary>
    /// 执行一段 PowerShell 脚本。elevated=true 时弹 UAC（runas）。
    /// 返回 (是否成功, 合并输出文本)。
    /// </summary>
    public static (bool Ok, string Output) Run(string script, bool elevated, string? logTag = null)
    {
        string ps1 = Path.Combine(Path.GetTempPath(), $"mbw_opt_{Guid.NewGuid():N}.ps1");
        string outFile = ps1 + ".out";
        string errFile = ps1 + ".err";

        try
        {
            // 包一层：把所有输出/错误落盘，便于提权进程回读
            string wrapped =
                "$ErrorActionPreference='Continue'\n" +
                $"try {{\n{WrapLines(script)}\n}} catch {{ Write-Output ('ERROR: ' + $_.Exception.Message) }}\n" +
                $"*>&1 | Out-File -FilePath '{outFile}' -Encoding UTF8\n";
            File.WriteAllText(ps1, wrapped);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\" 2>\"{errFile}\"",
                UseShellExecute = elevated,           // runas 必须 UseShellExecute=true
                Verb = elevated ? "runas" : "",
                CreateNoWindow = !elevated,
            };
            if (!elevated)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            using var p = Process.Start(psi);
            if (p is null) return (false, "无法启动 PowerShell");
            p.WaitForExit(elevated ? 120000 : 60000);

            string output = "";
            if (File.Exists(outFile)) output = File.ReadAllText(outFile);
            if (File.Exists(errFile)) { string e = File.ReadAllText(errFile); if (!string.IsNullOrWhiteSpace(e)) output += "\n[stderr] " + e; }
            if (!elevated)
            {
                try { output = p.StandardOutput.ReadToEnd(); } catch { }
            }

            bool ok = p.ExitCode == 0 && !output.Contains("ERROR:");
            return (ok, string.IsNullOrWhiteSpace(output) ? "(无输出)" : output.Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "用户取消了 UAC 提权，或权限不足。");
        }
        catch (Exception ex)
        {
            return (false, "执行异常：" + ex.Message);
        }
        finally
        {
            TryDelete(ps1); TryDelete(ps1 + ".out"); TryDelete(ps1 + ".err");
        }
    }

    private static string WrapLines(string script)
    {
        // 逐行缩进，避免嵌套 try 语法问题
        var lines = script.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", System.Array.ConvertAll(lines, l => "  " + l));
    }

    private static void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
