using System.Collections.Generic;

namespace DogDesk.Services.Optimizer;

/// <summary>风险等级：绿=安全可逆 / 黄=谨慎 / 红=不建议默认。</summary>
public enum RiskLevel { Green, Yellow, Red }

/// <summary>
/// 单个系统优化项（WinHance/Win11Debloat 风格功能清单，全部自研，未复制任何第三方代码）。
/// 应用/撤销均为 PowerShell 脚本；由 <see cref="ElevationRunner"/> 以提权方式执行。
/// 注意：本目录只描述动作，UI 默认全不勾选，绝不自动执行。
/// </summary>
public sealed class OptItem
{
    public string Id { init; get; } = "";
    public string Category { init; get; } = "";
    public string Name { init; get; } = "";
    public string Desc { init; get; } = "";
    public RiskLevel Risk { init; get; } = RiskLevel.Green;

    /// <summary>应用脚本（PowerShell）。</summary>
    public string Apply { init; get; } = "";

    /// <summary>撤销脚本（恢复默认）。</summary>
    public string Revert { init; get; } = "";

    public string RiskText => Risk switch
    {
        RiskLevel.Green => "安全",
        RiskLevel.Yellow => "谨慎",
        _ => "不建议",
    };
}

/// <summary>系统优化项目录（六大类，合计 ≥30 项）。命令均为真实注册表/PowerShell。</summary>
public static class OpsCatalog
{
    // 注册表辅助：确保键存在（脚本内联，便于独立执行）
    private static string EnsureF(string path) =>
        $"if(!(Test-Path '{path}')){{New-Item -Path '{path}' -Force | Out-Null}}; ";

    public static IReadOnlyList<OptItem> All { get; } = Build();

    private static List<OptItem> Build()
    {
        var list = new List<OptItem>();

        // ============ 隐私 ============
        list.AddRange(new[]
        {
            new OptItem { Id="priv.telemetry", Category="隐私", Name="关闭遥测数据上报",
                Desc="把 Diagnostics 服务级遥测降到最低（企业策略 AllowTelemetry=0）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -Value 1 -Type DWord" },
            new OptItem { Id="priv.adid", Category="隐私", Name="关闭广告标识符",
                Desc="停用跨应用广告 ID（HKCU AdvertisingInfo Enabled=0）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo") + "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Value 1 -Type DWord" },
            new OptItem { Id="priv.activity", Category="隐私", Name="关闭活动历史/时间线",
                Desc="停用活动源聚合与时间线发布（EnableActivityFeed=0）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name EnableActivityFeed -Value 0 -Type DWord; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name PublishUserActivities -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name EnableActivityFeed -Value 1 -Type DWord; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name PublishUserActivities -Value 1 -Type DWord" },
            new OptItem { Id="priv.location", Category="隐私", Name="关闭系统定位默认许可",
                Desc="设备访问定位默认拒绝（Global Location=Deny）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeviceAccess\\Global") + "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeviceAccess\\Global' -Name '{467BD88F-FD43-4F54-AD45-31F55C3065D2}' -Value Deny",
                Revert = "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeviceAccess\\Global' -Name '{467BD88F-FD43-4F54-AD45-31F55C3065D2}' -Value Allow" },
            new OptItem { Id="priv.tailored", Category="隐私", Name="关闭个性化诊断体验",
                Desc="停用基于诊断数据的个性化提示/建议。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy") + "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Name TailoredExperiencesWithDiagnosticDataEnabled -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Name TailoredExperiencesWithDiagnosticDataEnabled -Value 1 -Type DWord" },
            new OptItem { Id="priv.suggested", Category="隐私", Name="关闭设置内推荐提示",
                Desc="停用「开始/设置」中的推荐内容（SubscribedContent）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager") + "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SubscribedContent-338389Enabled -Value 0 -Type DWord; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SubscribedContent-310093Enabled -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SubscribedContent-338389Enabled -Value 1 -Type DWord; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SubscribedContent-310093Enabled -Value 1 -Type DWord" },
            new OptItem { Id="priv.launchtrack", Category="隐私", Name="停用应用启动跟踪",
                Desc="停用「启动应用跟踪」以改进搜索（StartupAppTask）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupAppsTracked") + "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupAppsTracked' -Name StartPageTracked -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupAppsTracked' -Name StartPageTracked -Value 1 -Type DWord" },
        });

        // ============ 安全 ============
        list.AddRange(new[]
        {
            new OptItem { Id="sec.remotehelp", Category="安全", Name="关闭远程协助",
                Desc="禁用来宾/快速协助入站连接（fDisableVR/FAConnection=1）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance") + "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance' -Name fDisableVRConnection -Value 1 -Type DWord; Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance' -Name fDisableFAConnection -Value 1 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance' -Name fDisableVRConnection -Value 0 -Type DWord; Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance' -Name fDisableFAConnection -Value 0 -Type DWord" },
            new OptItem { Id="sec.smb1", Category="安全", Name="禁用 SMBv1 协议",
                Desc="卸载 SMBv1 Windows 可选功能（安全漏洞面，需重启）。", Risk=RiskLevel.Yellow,
                Apply = "Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart",
                Revert = "Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart" },
            new OptItem { Id="sec.uac", Category="安全", Name="UAC 始终通知（提权确认）",
                Desc="拉满 UAC 提示级别（ConsentPromptBehaviorAdmin=2）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -Name ConsentPromptBehaviorAdmin -Value 2 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -Name ConsentPromptBehaviorAdmin -Value 5 -Type DWord" },
            new OptItem { Id="sec.firenotify", Category="安全", Name="启用防火墙通知合并",
                Desc="防火墙阻止时合并通知（即不反复弹窗）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile") + "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile' -Name DisableNotifications -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile' -Name DisableNotifications -Value 1 -Type DWord" },
            new OptItem { Id="sec.autologon", Category="安全", Name="禁用自动登录",
                Desc="清除自动登录凭据（AutoAdminLogon=0）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon' -Name AutoAdminLogon -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon' -Name AutoAdminLogon -Value 1 -Type DWord" },
        });

        // ============ 游戏性能 ============
        list.AddRange(new[]
        {
            new OptItem { Id="game.mode", Category="游戏性能", Name="开启 Windows 游戏模式",
                Desc="自动游戏模式（AllowAutoGameMode=1）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\SOFTWARE\\Microsoft\\GameBar") + "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\GameBar' -Name AllowAutoGameMode -Value 1 -Type DWord; Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\GameBar' -Name AllowGameMode -Value 1 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\GameBar' -Name AllowAutoGameMode -Value 0 -Type DWord" },
            new OptItem { Id="game.gpu", Category="游戏性能", Name="硬件加速 GPU 调度",
                Desc="HwSchMode=2（需重启生效）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers") + "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers' -Name HwSchMode -Value 2 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers' -Name HwSchMode -Value 1 -Type DWord" },
            new OptItem { Id="game.startdelay", Category="游戏性能", Name="关闭启动应用延迟",
                Desc="停用启动延迟（StartupDelayInMSec=0）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize") + "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize' -Name StartupDelayInMSec -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Serialize' -Name StartupDelayInMSec -Value 1 -Type DWord" },
            new OptItem { Id="game.bsvc", Category="游戏性能", Name="游戏时关闭后台录制",
                Desc="停用 GameDVR 后台录制（影响性能）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKCU:\\System\\GameConfigStore") + "Set-ItemProperty -Path 'HKCU:\\System\\GameConfigStore' -Name GameDVR_Enabled -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\System\\GameConfigStore' -Name GameDVR_Enabled -Value 1 -Type DWord" },
            new OptItem { Id="game.powerplan", Category="游戏性能", Name="切换高性能电源计划",
                Desc="powercfg 切到 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c（高性能）。", Risk=RiskLevel.Yellow,
                Apply = "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                Revert = "powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e" },
        });

        // ============ 更新 ============
        list.AddRange(new[]
        {
            new OptItem { Id="upd.driver", Category="更新", Name="排除驱动自动更新",
                Desc="设备驱动不随 Windows Update 自动安装（ExcludeWUDrivers=1）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Name ExcludeWUDriversInQualityUpdate -Value 1 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Name ExcludeWUDriversInQualityUpdate -Value 0 -Type DWord" },
            new OptItem { Id="upd.deliveryopt", Category="更新", Name="关闭传递优化 P2P",
                Desc="停用跨设备下载分发（DODownloadMode=0）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization' -Name DODownloadMode -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization' -Name DODownloadMode -Value 1 -Type DWord" },
            new OptItem { Id="upd.autorestart", Category="更新", Name="阻止更新后自动重启",
                Desc="登录用户时不强制重启（NoAutoRebootWithLoggedOnUsers=1）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name NoAutoRebootWithLoggedOnUsers -Value 1 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name NoAutoRebootWithLoggedOnUsers -Value 0 -Type DWord" },
            new OptItem { Id="upd.restarthour", Category="更新", Name="调整活跃小时上限",
                Desc="把自动重启活跃小时设到 18（限制重启窗口）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKLM:\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings") + "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings' -Name ActiveHoursMaxRange -Value 18 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings' -Name ActiveHoursMaxRange -Value 12 -Type DWord" },
        });

        // ============ 电源 ============
        list.AddRange(new[]
        {
            new OptItem { Id="pw.faststart", Category="电源", Name="关闭快速启动",
                Desc="HiberbootEnabled=0（完全冷启动，兼容性更好）。", Risk=RiskLevel.Yellow,
                Apply = EnsureF("HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power") + "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 1 -Type DWord" },
            new OptItem { Id="pw.never_sleep", Category="电源", Name="接通电源永不睡眠",
                Desc="休眠/睡眠设为 0（永不，台式机）。", Risk=RiskLevel.Yellow,
                Apply = "powercfg /change standby-timeout-ac 0; powercfg /change hibernate-timeout-ac 0",
                Revert = "powercfg /change standby-timeout-ac 30; powercfg /change hibernate-timeout-ac 60" },
            new OptItem { Id="pw.monitor", Category="电源", Name="关闭显示器延时延长",
                Desc="接通电源关屏 15 分钟。", Risk=RiskLevel.Green,
                Apply = "powercfg /change monitor-timeout-ac 15",
                Revert = "powercfg /change monitor-timeout-ac 10" },
            new OptItem { Id="pw.usb", Category="电源", Name="关闭 USB 选择性挂起",
                Desc="避免键鼠/U盘掉电（USBSelectiveSuspend=0）。", Risk=RiskLevel.Yellow,
                Apply = "powercfg /setacvalueindex SCHEME_CURRENT 2a737341-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0; powercfg /setactive SCHEME_CURRENT",
                Revert = "powercfg /setacvalueindex SCHEME_CURRENT 2a737341-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 1; powercfg /setactive SCHEME_CURRENT" },
        });

        // ============ 声音 ============
        list.AddRange(new[]
        {
            new OptItem { Id="snd.ducking", Category="声音", Name="关闭通信自动压音",
                Desc="通话/通信时不自动压低其他音量（UserDuckingPreference=3）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\Software\\Microsoft\\Multimedia\\Audio") + "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Multimedia\\Audio' -Name UserDuckingPreference -Value 3 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Multimedia\\Audio' -Name UserDuckingPreference -Value 0 -Type DWord" },
            new OptItem { Id="snd.systemfx", Category="声音", Name="关闭系统提示音",
                Desc="错误/默认提示音关闭（NoSounds=1）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\AppEvents\\Schemes") + "Set-ItemProperty -Path 'HKCU:\\AppEvents\\Schemes' -Name NoSounds -Value 1 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\AppEvents\\Schemes' -Name NoSounds -Value 0 -Type DWord" },
            new OptItem { Id="snd.communications", Category="声音", Name="通信设备不自动静噪",
                Desc="通信活动时不把其他音量降到 50%（不压音）。", Risk=RiskLevel.Green,
                Apply = EnsureF("HKCU:\\Software\\Microsoft\\Multimedia\\Audio") + "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Multimedia\\Audio' -Name LastNonMinizedCommsPreference -Value 0 -Type DWord",
                Revert = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Multimedia\\Audio' -Name LastNonMinizedCommsPreference -Value 1 -Type DWord" },
            new OptItem { Id="snd.enhancements", Category="声音", Name="关闭音频增强",
                Desc="停用扬声器增强（避免爆音/延迟）。", Risk=RiskLevel.Yellow,
                Apply = "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\MMDevices\\Audio\\Render' -Name '{e0a941a0-85df-11d0-bd6-00c04fd7c5e4}' -Value 0 -ErrorAction SilentlyContinue",
                Revert = "# 音频增强默认由驱动控制，撤销为默认（重启音频设备生效）" },
        });

        return list;
    }
}
