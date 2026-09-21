// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：MauiProgram.cs
//  说明：应用入口。
//        注意本项目不注册自定义字体：正文与代码分别使用各平台的系统字体
//        （Windows: Segoe UI / Consolas，Android: sans-serif / monospace），
//        这样单文件发布时不需要外带字体资源。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  接入 DiagnosticsLog 与全局未捕获异常兜底
//                   （GUI 程序没有控制台，启动期异常否则完全不可见）
//  修改：2026-09-21  最前面处理外壳集成命令行指令（--register-shell 等），
//                   此时尚未建窗口，脚本化调用不会闪窗
//  修改：2026-09-21  接入单实例运行：已有实例时把参数转交过去再退出
// -----------------------------------------------------------------------------

using MD.Markdown;
using MD.Services;

namespace MD;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // 日志尽早初始化：它是「窗口没出来」这类问题的唯一线索
        DiagnosticsLog.Init();

        // 全局兜底：任何未捕获异常都留痕，而不是让进程悄悄消失
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticsLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticsLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // 提前注册中文代码页（GB18030/GBK），避免首次打开 GBK 文档时才初始化
        TextEncodingDetector.EnsureProvider();

        // 外壳集成指令：命中就执行完直接退出，不会创建窗口
        ShellCommandLine.TryRunAndExit();

        // 单实例：已有实例在运行时，把命令行参数转交给它，本进程立刻退出。
        // 必须在这里（建窗口之前）判断，否则第二个实例会先闪出一个窗口再消失。
        // --new-instance 是逃生阀：显式要求再开一个进程（并排对比两份文档时用）。
        bool forcedNewInstance = Environment.GetCommandLineArgs()
            .Any(arg => arg.Equals("--new-instance", StringComparison.OrdinalIgnoreCase));

        if (!forcedNewInstance && !SingleInstance.TryAcquire(Environment.GetCommandLineArgs()))
        {
            DiagnosticsLog.Write("已有实例在运行，本进程退出（外壳集成指令在此之前已处理完）");
            Environment.Exit(0);
        }

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        return builder.Build();
    }
}
