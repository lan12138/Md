// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/DiagnosticsLog.cs
//  说明：极简运行日志。
//        为什么需要它：本项目是 GUI 程序，没有控制台，启动期一旦抛异常
//        往往是「窗口一闪就没了」或「进程活着但什么都没有」，无从下手。
//        这里把关键启动节点与未捕获异常落到 <配置目录>/md.log。
//        刻意不引入任何日志框架：单文件发布时少一个依赖就少一份体积和风险。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Text;

namespace MD.Services;

public static class DiagnosticsLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static bool _ready;

    /// <summary>日志文件路径；初始化失败时为 null。</summary>
    public static string? FilePath { get; private set; }

    /// <summary>写日志失败不应影响主流程，全部异常都吞掉。</summary>
    public static void Init()
    {
        lock (Gate)
        {
            if (_ready)
                return;
            _ready = true;

            try
            {
                FilePath = AppPaths.LogFile;

                // 超过上限就轮转一次，避免长期运行把日志写成几十 MB
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = FilePath + ".1";
                    try { File.Delete(old); } catch { /* 忽略 */ }
                    try { File.Move(FilePath, old); } catch { /* 忽略 */ }
                }

                Write("========== 启动 ==========");
                Write($"版本  : {typeof(DiagnosticsLog).Assembly.GetName().Version}");
                Write($"运行时: {Environment.Version} / {Environment.OSVersion}");
                Write($"命令行: {string.Join(" ", Environment.GetCommandLineArgs())}");
            }
            catch
            {
                FilePath = null;
            }
        }
    }

    public static void Write(string message)
    {
        if (!_ready)
            Init();

        lock (Gate)
        {
            if (FilePath is null)
                return;

            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
            catch
            {
                // 日志写不进去就算了，不能因此让程序出错
            }
        }
    }

    public static void Write(string context, Exception? ex)
    {
        if (ex is null)
        {
            Write(context);
            return;
        }

        Write($"{context} :: {ex.GetType().FullName}: {ex.Message}");
        Write(ex.ToString());
    }
}
