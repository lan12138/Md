// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsShellIntegration.cs
//  说明：Windows Shell 集成。全部只写 HKEY_CURRENT_USER，不需要管理员权限。
//
//        做三件事：
//          1) 注册 ProgId（MD.Markdown）+ .md 等扩展名的 OpenWithProgids
//             → 右键「打开方式」里出现 MD，且可以「始终使用此应用打开」
//          2) 注册 RegisteredApplications / Capabilities
//             → MD 出现在「默认应用」列表里，用户可以在系统设置里正式设为默认
//          3) 追加右键菜单项：文件「用 MD 打开」/ 文件夹「用 MD 打开文件夹」
//
//        ⚠ 为什么不能直接「一键设为默认」：
//        Windows 8 起，默认关联由 UserChoice 哈希保护，微软明确禁止程序自行改写
//        （会被系统重置）。唯一合规做法是把应用注册干净，然后引导用户到系统
//        「默认应用」页面确认一次。所以这里的「设为默认」按钮是「打开设置页」，
//        而不是偷偷改注册表 —— 那既无效也不正当。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MD.Platforms.Windows;

internal static class WindowsShellIntegration
{
    private const string ProgId = "MD.Markdown";
    private const string AppName = "MD";
    private const string FriendlyName = "Markdown 文档";

    /// <summary>参与关联的扩展名。</summary>
    private static readonly string[] Extensions = { ".md", ".markdown", ".mdown", ".mkd", ".mdx" };

    /// <summary>当前进程的可执行文件路径（单文件发布下也正确）。</summary>
    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                return path;
            return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
    }

    public static bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
                return key is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>注册（幂等，重复调用只是覆盖同样的值）。</summary>
    public static (bool Ok, string Message) Register()
    {
        var exe = ExecutablePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return (false, "无法确定可执行文件路径");

        try
        {
            string openCommand = $"\"{exe}\" \"%1\"";

            // ---- ProgId ----
            using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progId.SetValue(null, FriendlyName);
                progId.SetValue("FriendlyTypeName", FriendlyName);
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue(null, $"\"{exe}\",0");
                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(null, openCommand);
            }

            // ---- 扩展名 → OpenWithProgids ----
            foreach (var ext in Extensions)
            {
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                extKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            // ---- Applications 键：让「打开方式」列表里一定能看到 ----
            using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{Path.GetFileName(exe)}"))
            {
                app.SetValue("FriendlyAppName", AppName);
                using var command = app.CreateSubKey(@"shell\open\command");
                command.SetValue(null, openCommand);
                using var types = app.CreateSubKey("SupportedTypes");
                foreach (var ext in Extensions)
                    types.SetValue(ext, string.Empty);
            }

            // ---- 右键菜单：文件 ----
            using (var ctx = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\MD"))
            {
                ctx.SetValue("MUIVerb", "用 MD 打开");
                ctx.SetValue("Icon", $"\"{exe}\",0");
                using var command = ctx.CreateSubKey("command");
                command.SetValue(null, openCommand);
            }

            // ---- 右键菜单：文件夹 ----
            using (var ctx = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\MD"))
            {
                ctx.SetValue("MUIVerb", "用 MD 打开文件夹");
                ctx.SetValue("Icon", $"\"{exe}\",0");
                using var command = ctx.CreateSubKey("command");
                // --folder 是自定义开关，命令行解析里认它
                command.SetValue(null, $"\"{exe}\" --folder \"%1\"");
            }

            // ---- Capabilities：出现在「默认应用」列表里 ----
            using (var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\MD\Capabilities"))
            {
                capabilities.SetValue("ApplicationName", AppName);
                capabilities.SetValue("ApplicationDescription", "MD · Markdown 阅读器");
                using var assoc = capabilities.CreateSubKey("FileAssociations");
                foreach (var ext in Extensions)
                    assoc.SetValue(ext, ProgId);
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registered.SetValue(AppName, @"Software\MD\Capabilities");
            }

            RefreshShell();
            return (true, "已注册：右键「打开方式」里现在可以选择 MD");
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("Shell 注册失败", ex);
            return (false, $"注册失败：{ex.Message}");
        }
    }

    /// <summary>注销（只删自己写的键）。</summary>
    public static (bool Ok, string Message) Unregister()
    {
        try
        {
            DeleteTree($@"Software\Classes\{ProgId}");
            DeleteTree(@"Software\Classes\*\shell\MD");
            DeleteTree(@"Software\Classes\Directory\shell\MD");
            DeleteTree(@"Software\MD");

            foreach (var ext in Extensions)
            {
                using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                extKey?.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            {
                registered?.DeleteValue(AppName, throwOnMissingValue: false);
            }

            var exe = Path.GetFileName(ExecutablePath);
            if (!string.IsNullOrEmpty(exe))
                DeleteTree($@"Software\Classes\Applications\{exe}");

            RefreshShell();
            return (true, "已移除 MD 的右键菜单与「打开方式」注册");
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("Shell 注销失败", ex);
            return (false, $"移除失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 打开系统「默认应用」页面。
    /// Windows 不允许程序直接改默认关联（UserChoice 有哈希保护），
    /// 所以这里优先调传统关联 UI，失败则退到「默认应用」设置页。
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            var ui = (IApplicationAssociationRegistrationUI)new ApplicationAssociationRegistrationUI();
            ui.LaunchAdvancedAssociationUI(AppName);
            return;
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("LaunchAdvancedAssociationUI 不可用，退回设置页", ex);
        }

        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("打开默认应用设置失败", ex);
        }
    }

    /// <summary>打开系统「打开方式」对话框（让用户临时选一次）。</summary>
    public static void OpenWithDialog(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("打开「打开方式」对话框失败", ex);
        }
    }

    private static void DeleteTree(string subKey)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // 键不存在或权限不足时忽略
        }
    }

    /// <summary>通知资源管理器刷新关联缓存，否则右键菜单要等一会儿才出现。</summary>
    private static void RefreshShell()
    {
        try
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // 忽略
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    // ---- 传统「设置关联」对话框（Win10/11 上会跳到默认应用页面） ----

    [ComImport]
    [Guid("1968106d-f3b5-44cf-890e-116fcb9ecef1")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ApplicationAssociationRegistrationUI
    {
    }

    [ComImport]
    [Guid("1f76a169-f994-40ac-8fc8-0959e8874710")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistrationUI
    {
        [PreserveSig]
        int LaunchAdvancedAssociationUI([MarshalAs(UnmanagedType.LPWStr)] string appRegistrationName);
    }
}
#endif
