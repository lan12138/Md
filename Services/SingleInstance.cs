// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/SingleInstance.cs
//  说明：单实例运行。
//
//        为什么需要：程序注册了 .md 的文件关联与右键菜单，用户在资源管理器里
//        双击三篇文档，理应得到「同一个窗口里的三个标签」，而不是三个 MD.exe
//        进程、三份 settings.json 写入者、三个抢同一把配置的窗口。
//
//        做法：
//          ① 命名互斥体（Mutex）判断自己是不是第一个实例；
//          ② 不是第一个 → 用命名管道（Named Pipe）把命令行参数转交给第一个实例，
//             然后立刻退出（此时还没建窗口，所以不会闪窗）；
//          ③ 是第一个 → 起一个后台管道监听循环，收到参数后在 UI 线程上打开。
//
//        为什么用管道而不是 WM_COPYDATA：管道不需要先拿到对方窗口句柄
//        （句柄要靠 FindWindow 猜窗口标题，改标题就失效），而且能直接传结构化数据。
//
//        命名带上会话号：互斥体本身加 Local\ 前缀已是会话级，
//        但命名管道在 \\.\pipe\ 下是**全机器**可见的，不带会话号会让
//        同一台机器上另一个登录用户的 MD 抢到我们的消息。
//
//        ⚠ 启动顺序很重要：必须在 CreateMauiApp() 里、建窗口之前调用 TryAcquire，
//          否则第二个实例会先闪出一个窗口再退出。见 MauiProgram.cs。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MD.Services;

public static class SingleInstance
{
    /// <summary>保护 _handler 与 Pending 的锁。</summary>
    private static readonly object Gate = new();

    /// <summary>界面尚未接管时收到的参数，等 SetHandler 时一次性补投。</summary>
    private static readonly List<string[]> Pending = new();

    private static Action<string[]>? _handler;

    /// <summary>只要进程活着就得持有它：一旦被 GC 回收，单实例约束就失效了。</summary>
    private static Mutex? _mutex;

    private static bool _serverStarted;

    private static readonly string Scope = BuildScope();

    private static readonly string MutexName = $@"Local\MD.Markdown.SingleInstance.{Scope}";

    private static readonly string PipeName = $"MD.Markdown.Pipe.{Scope}";

    /// <summary>
    /// 抢占「主实例」身份。
    /// </summary>
    /// <param name="rawArgs">
    /// 传 <see cref="Environment.GetCommandLineArgs"/>（第 0 项是 exe 路径，会被跳过）。
    /// </param>
    /// <returns>
    /// true = 我是主实例，继续正常启动；
    /// false = 已有实例在运行、参数已转交，调用方**必须立即退出**（不要建窗口）。
    /// </returns>
    public static bool TryAcquire(string[] rawArgs)
    {
        var payload = rawArgs.Length > 1 ? rawArgs[1..] : Array.Empty<string>();

        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName, out bool createdNew);

            if (!createdNew)
            {
                DiagnosticsLog.Write("检测到已有实例在运行，尝试转交命令行参数");

                if (Forward(payload))
                {
                    DiagnosticsLog.Write($"参数已转交给运行中的实例（{payload.Length} 个），本进程退出");
                    return false;
                }

                // 对方可能正好在退出（互斥体还在、管道已关）。此时降级为独立启动：
                // 宁可短暂出现两个窗口，也不要让用户双击文档之后什么都没发生。
                DiagnosticsLog.Write("转交失败（运行中的实例可能正在退出），降级为独立启动");
            }
        }
        catch (Exception ex)
        {
            // 互斥体都建不起来（权限受限等）也不该让程序打不开
            DiagnosticsLog.Write("单实例互斥体创建失败，跳过单实例检查", ex);
        }

        StartServer();
        return true;
    }

    /// <summary>
    /// 注册参数接收者。**在界面准备好之后再调用**：早于它到达的参数会被排队，
    /// 在这里一次性补投，所以不会丢。
    /// </summary>
    /// <param name="handler">在**后台线程**上被调用，需要自己切回 UI 线程。</param>
    public static void SetHandler(Action<string[]> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        List<string[]> drain;
        lock (Gate)
        {
            _handler = handler;
            drain = new List<string[]>(Pending);
            Pending.Clear();
        }

        if (drain.Count > 0)
            DiagnosticsLog.Write($"补投启动期间收到的 {drain.Count} 批参数");

        foreach (var args in drain)
            Invoke(handler, args);
    }

    // ------------------------------------------------------------------
    // 第二个实例：把参数送出去
    // ------------------------------------------------------------------

    private static bool Forward(string[] payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        // 第二个实例是刚被资源管理器启动的，**它**才有前台权限。
        // 把这个权限让出去，运行中的实例才能把窗口拉到前台（否则 SetForegroundWindow
        // 会被系统静默拒绝，表现为「文件开了但窗口没跳出来」）。
#if WINDOWS
        try { AllowSetForegroundWindow(AsfwAny); } catch { /* 忽略 */ }
#endif

        // 主实例的监听是异步起的：连续双击两个文件时它可能还没就绪，所以重试几次
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
                client.Connect(1000);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                client.WaitForPipeDrain();
                return true;
            }
            catch (Exception ex)
            {
                if (attempt == 3)
                {
                    DiagnosticsLog.Write($"转交参数失败（已重试 {attempt} 次）", ex);
                    break;
                }
                Thread.Sleep(250);
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // 主实例：收参数
    // ------------------------------------------------------------------

    private static void StartServer()
    {
        if (_serverStarted)
            return;

        _serverStarted = true;

        // 监听循环自己会兜住所有异常，这里不需要 await，也不需要保存 Task
        _ = Task.Run(ListenLoopAsync);
        DiagnosticsLog.Write($"单实例监听已启动（管道 {PipeName}）");
    }

    private static async Task ListenLoopAsync()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync();
                Dispatch(payload);
            }
            catch (Exception ex)
            {
                // 监听循环必须活着：一次异常（比如客户端写到一半被结束）不能让
                // 单实例能力永久失效 —— 那之后每个双击都会开出新进程。
                DiagnosticsLog.Write("单实例管道监听异常，1 秒后重试", ex);
                try { await Task.Delay(1000); } catch { return; }
            }
        }
    }

    private static void Dispatch(string payload)
    {
        string[] args;
        try
        {
            args = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"第二个实例传来的参数无法解析：{payload}", ex);
            return;
        }

        DiagnosticsLog.Write($"收到第二个实例转交的参数（{args.Length} 个）：{string.Join(" | ", args)}");

        Action<string[]>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null)
            {
                // 界面还没准备好接管，先排队
                Pending.Add(args);
                return;
            }
        }

        Invoke(handler, args);
    }

    private static void Invoke(Action<string[]> handler, string[] args)
    {
        try
        {
            handler(args);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("处理第二个实例的参数失败", ex);
        }
    }

    // ------------------------------------------------------------------

    /// <summary>区分不同登录会话，避免命名管道跨用户串台。</summary>
    private static string BuildScope()
    {
        try
        {
            return Process.GetCurrentProcess().SessionId.ToString();
        }
        catch
        {
            return "0";
        }
    }

#if WINDOWS
    private const uint AsfwAny = 0xFFFFFFFF;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
#endif
}
