// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/ShellCommandLine.cs
//  说明：命令行形式的「外壳集成」指令，供安装器 / 批处理 / 自动化测试调用：
//
//          MD.exe --register-shell     注册右键「打开方式」与默认应用条目
//          MD.exe --unregister-shell   移除上面注册的全部内容
//          MD.exe --default-apps       打开系统「默认应用」设置页
//
//        退出码：0 成功 / 1 失败，便于脚本判断。
//
//        ⚠ 为什么放在 MauiProgram 最前面执行：
//        CreateMauiApp 返回之后 MAUI 才会建主窗口。如果把这段放在页面里，
//        脚本化调用时会先闪出一个窗口再退出，观感很差。
//        在这里直接 Environment.Exit，连窗口都不会创建。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Services;

public static class ShellCommandLine
{
    /// <summary>
    /// 扫描命令行；命中外壳指令就执行并结束进程（不返回）。
    /// 没有命中则立即返回，走正常界面流程。
    /// </summary>
    public static void TryRunAndExit()
    {
        string? command = null;

        try
        {
            var raw = Environment.GetCommandLineArgs();
            foreach (var arg in raw)
            {
                if (arg.Equals("--register-shell", StringComparison.OrdinalIgnoreCase))
                    command = "register";
                else if (arg.Equals("--unregister-shell", StringComparison.OrdinalIgnoreCase))
                    command = "unregister";
                else if (arg.Equals("--default-apps", StringComparison.OrdinalIgnoreCase))
                    command = "default-apps";
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("解析外壳指令失败", ex);
            return;
        }

        if (command is null)
            return;

        try
        {
            switch (command)
            {
                case "register":
                {
                    var (ok, message) = ShellService.Register();
                    DiagnosticsLog.Write($"命令行注册系统集成：ok={ok} :: {message}");
                    Environment.Exit(ok ? 0 : 1);
                    return;
                }

                case "unregister":
                {
                    var (ok, message) = ShellService.Unregister();
                    DiagnosticsLog.Write($"命令行注销系统集成：ok={ok} :: {message}");
                    Environment.Exit(ok ? 0 : 1);
                    return;
                }

                case "default-apps":
                    ShellService.OpenDefaultAppsSettings();
                    DiagnosticsLog.Write("命令行打开默认应用设置页");
                    Environment.Exit(0);
                    return;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"执行外壳指令失败: {command}", ex);
            Environment.Exit(1);
        }
    }
}
