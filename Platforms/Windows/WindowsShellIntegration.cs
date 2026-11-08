// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsShellIntegration.cs
//  说明：Windows Shell 集成。全部只写 HKEY_CURRENT_USER，不需要管理员权限。
//
//        注册模型（刻意保持「一个应用、一个 ProgId」，避免打开方式里出现重复项）：
//          1) ProgId：HKCU\Software\Classes\MD.Markdown
//             · 默认值      = MD（打开方式列表显示的名字）
//             · FriendlyTypeName = Markdown 文档
//             · DefaultIcon / shell\open\command
//          2) 扩展名：.md 等 → OpenWithProgids 写入 MD.Markdown
//          3) 右键菜单：SystemFileAssociations\.md\shell\MD（只对 Markdown 文件出现）
//             + Directory\shell\MD（文件夹）
//          4) Capabilities + RegisteredApplications → 系统「默认应用」列表
//
//        为什么不再写 Applications\MD.exe：
//          它与 OpenWithProgids 同时存在时，「打开方式」里会出现两个 MD 条目，
//          其中一个还只有类型名没有图标 —— 用户反馈的「怪怪的」主要来自这里。
//          现在统一由 ProgId 承担显示名与图标，注册表里只留一份。
//
//        ⚠ 为什么不能直接「一键设为默认」：
//        Windows 8 起默认关联由 UserChoice 哈希保护，微软明确禁止程序自行改写
//        （会被系统重置）。唯一合规做法是把应用注册干净，然后引导用户到系统
//        「默认应用」页面确认一次。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-11-08  重做默认打开方式：
//                   · 去掉 Applications\MD.exe 与 OpenWithProgids 的重复条目
//                   · ProgId 默认值改为应用名 MD，图标/命令只保留一份
//                   · 右键菜单从 *\shell\MD 收窄到 SystemFileAssociations\.md
//                   · Capabilities 补 ApplicationIcon，默认应用页显示正确图标
//                   · LaunchAdvancedAssociationUI 检查 HRESULT，失败再退到设置页
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
    private const string FriendlyTypeName = "Markdown 文档";
    private const string AppDescription = "轻量 Markdown 阅读器";

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

    /// <summary>完整注册是否齐备（ProgId + Capabilities + RegisteredApplications）。</summary>
    public static bool IsRegistered
    {
        get
        {
            try
            {
                using var progId = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
                using var capabilities = Registry.CurrentUser.OpenSubKey(@"Software\MD\Capabilities");
                using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
                if (progId is null || capabilities is null || registered is null)
                    return false;

                return registered.GetValue(AppName) is string value
                       && value.Equals(@"Software\MD\Capabilities", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>注册（幂等；会顺手清掉旧版本留下的重复/过宽条目）。</summary>
    public static (bool Ok, string Message) Register()
    {
        var exe = ExecutablePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return (false, "无法确定可执行文件路径");

        try
        {
            string openCommand = $"\"{exe}\" \"%1\"";
            string folderCommand = $"\"{exe}\" --folder \"%1\"";
            string icon = $"\"{exe}\",0";

            // ---- 0) 清掉旧版本留下的、会造成重复的键 ----
            DeleteTree($@"Software\Classes\Applications\{Path.GetFileName(exe)}");
            DeleteTree(@"Software\Classes\*\shell\MD");

            // ---- 1) ProgId：显示名 / 类型名 / 图标 / 命令 ----
            using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                // 默认值是「打开方式」列表里显示的应用名
                progId.SetValue(null, AppName);
                progId.SetValue("FriendlyTypeName", FriendlyTypeName);
                // 部分 Windows 版本会读 FriendlyAppName；与默认值一致，避免再出现第二个名字
                progId.SetValue("FriendlyAppName", AppName);

                using var iconKey = progId.CreateSubKey("DefaultIcon");
                iconKey.SetValue(null, icon);

                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(null, openCommand);
            }

            // ---- 2) 扩展名 → OpenWithProgids（只登记 ProgId，不再登记 Applications\MD.exe）----
            foreach (var ext in Extensions)
            {
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                // 旧版本可能写过 Applications\MD.exe 之类的值；不主动删别人的值，只补自己的
                extKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            // ---- 3) 右键菜单：只对 Markdown 文件出现，不再污染所有文件 ----
            foreach (var ext in Extensions)
            {
                using var ctx = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\SystemFileAssociations\{ext}\shell\MD");
                ctx.SetValue("MUIVerb", "用 MD 打开");
                ctx.SetValue("Icon", icon);
                using var command = ctx.CreateSubKey("command");
                command.SetValue(null, openCommand);
            }

            // ---- 右键菜单：文件夹 ----
            using (var ctx = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\MD"))
            {
                ctx.SetValue("MUIVerb", "用 MD 打开文件夹");
                ctx.SetValue("Icon", icon);
                using var command = ctx.CreateSubKey("command");
                command.SetValue(null, folderCommand);
            }

            // ---- 4) Capabilities：出现在「默认应用」列表里，并带图标 ----
            using (var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\MD\Capabilities"))
            {
                capabilities.SetValue("ApplicationName", AppName);
                capabilities.SetValue("ApplicationDescription", AppDescription);
                capabilities.SetValue("ApplicationIcon", icon);

                using var assoc = capabilities.CreateSubKey("FileAssociations");
                foreach (var ext in Extensions)
                    assoc.SetValue(ext, ProgId);
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registered.SetValue(AppName, @"Software\MD\Capabilities");
            }

            RefreshShell();
            return (true, "已注册：右键「打开方式」与系统「默认应用」里都能看到 MD");
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("Shell 注册失败", ex);
            return (false, $"注册失败：{ex.Message}");
        }
    }

    /// <summary>注销（只删自己写的键；同时兼容旧版本的旧键）。</summary>
    public static (bool Ok, string Message) Unregister()
    {
        try
        {
            var exe = ExecutablePath;

            DeleteTree($@"Software\Classes\{ProgId}");
            DeleteTree(@"Software\MD");
            DeleteTree(@"Software\Classes\*\shell\MD");
            DeleteTree(@"Software\Classes\Directory\shell\MD");

            // 旧版本注册过 Applications\MD.exe；一并清掉
            if (!string.IsNullOrEmpty(exe))
                DeleteTree($@"Software\Classes\Applications\{Path.GetFileName(exe)}");

            foreach (var ext in Extensions)
            {
                DeleteTree($@"Software\Classes\SystemFileAssociations\{ext}\shell\MD");

                using var extKey = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                extKey?.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            using (var registered = Registry.CurrentUser.OpenSubKey(
                       @"Software\RegisteredApplications", writable: true))
            {
                registered?.DeleteValue(AppName, throwOnMissingValue: false);
            }

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
    /// 打开系统「默认应用」相关页面。
    /// 优先用官方 IApplicationAssociationRegistrationUI（只列出本应用的关联，
    /// 用户勾选一次即可）；它不可用时退到 Windows 11 的按应用页，再退到总页面。
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        // 1) 官方高级关联 UI：HRESULT 必须显式检查，不能「调用了就当成功」
        try
        {
            var ui = (IApplicationAssociationRegistrationUI)new ApplicationAssociationRegistrationUI();
            int hr = ui.LaunchAdvancedAssociationUI(AppName);
            if (hr == 0)
                return;

            Services.DiagnosticsLog.Write($"LaunchAdvancedAssociationUI 返回 0x{hr:X8}，改用设置页");
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write("LaunchAdvancedAssociationUI 不可用，改用设置页", ex);
        }

        // 2) Windows 11：直接打开 MD 这一应用的默认值页面
        if (TryOpenSettings($"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(AppName)}"))
            return;

        // 3) 最后兜底：默认应用总页面
        TryOpenSettings("ms-settings:defaultapps");
    }

    private static bool TryOpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Write($"打开设置页失败: {uri}", ex);
            return false;
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
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x1000 /* SHCNF_FLUSH */,
                IntPtr.Zero, IntPtr.Zero);
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
