// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/SettingsService.cs
//  说明：个性化设置的读写。
//        · 单文件 JSON：<配置目录>/settings.json
//        · 写入采用「临时文件 + 原子替换」，避免断电/崩溃写坏配置
//        · 变更后防抖 500ms 落盘，避免拖动窗口时高频写盘
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using System.Text.Json;
using MD.Models;

namespace MD.Services;

public sealed class SettingsService : IDisposable
{
    private readonly object _gate = new();
    private readonly System.Timers.Timer _debounce;
    private AppSettings _current = new();
    private bool _dirty;
    private bool _disposed;

    public SettingsService()
    {
        _debounce = new System.Timers.Timer(500) { AutoReset = false };
        _debounce.Elapsed += (_, _) => FlushNow();
        Load();
    }

    /// <summary>当前设置（直接修改后调用 <see cref="Touch"/> 标记待保存）。</summary>
    public AppSettings Current => _current;

    /// <summary>设置已落盘。</summary>
    public event EventHandler? Saved;

    /// <summary>配置文件的完整路径（用于「打开配置目录」）。</summary>
    public string FilePath => AppPaths.SettingsFile;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _current = new AppSettings();
                return;
            }

            var json = File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, ThemeJson.Options);
            _current = loaded is null ? new AppSettings() : Migrate(loaded);
        }
        catch (Exception ex)
        {
            // 配置损坏：备份后使用默认值，绝不让程序启动失败
            TryBackupCorrupted();
            _current = new AppSettings();
            DiagnosticsLog.Write("settings.json 读取失败，已回退默认值", ex);
        }
    }

    private static AppSettings Migrate(AppSettings s)
    {
        s.Appearance ??= new AppearanceSettings();
        s.Window ??= new WindowSettings();
        s.Reading ??= new ReadingSettings();
        s.Files ??= new FileSettings();
        s.Reading.ScrollPositions ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        s.Reading.LastSeen ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        s.Files.Recent ??= new List<RecentFile>();

        if (s.Window.Width < 480) s.Window.Width = 1180;
        if (s.Window.Height < 360) s.Window.Height = 840;
        if (s.Window.SidebarWidth is < 160 or > 640) s.Window.SidebarWidth = 268;
        if (string.IsNullOrWhiteSpace(s.Appearance.ThemeId)) s.Appearance.ThemeId = "github-light";
        if (string.IsNullOrWhiteSpace(s.Appearance.DarkThemeId)) s.Appearance.DarkThemeId = "github-dark";

        return s;
    }

    private void TryBackupCorrupted()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch
        {
            // 忽略备份失败
        }
    }

    /// <summary>标记设置已变更，稍后自动保存。</summary>
    public void Touch()
    {
        lock (_gate)
        {
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    /// <summary>立即保存（退出前调用）。</summary>
    public void Flush()
    {
        _debounce.Stop();
        FlushNow();
    }

    private void FlushNow()
    {
        lock (_gate)
        {
            if (!_dirty || _disposed)
                return;

            try
            {
                TrimRecent();
                var json = JsonSerializer.Serialize(_current, ThemeJson.Options);

                var path = FilePath;
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch
                    {
                        // 某些文件系统不支持 Replace，退化为覆盖写
                        File.Copy(tmp, path, overwrite: true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }

                _dirty = false;
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // 写盘失败必须留痕：否则表现为「设置永远不保存」这种极难定位的问题
                DiagnosticsLog.Write("settings.json 写入失败", ex);
            }
        }
    }

    private void TrimRecent()
    {
        var files = _current.Files;
        int max = Math.Clamp(files.MaxRecent, 3, 100);

        files.Recent = files.Recent
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path))
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.OpenedAt).First())
            .OrderByDescending(r => r.OpenedAt)
            .Take(max)
            .ToList();

        // 阅读进度记录做容量控制，避免 settings.json 无限膨胀
        if (_current.Reading.ScrollPositions.Count > 500)
        {
            var keep = _current.Reading.LastSeen
                .OrderByDescending(kv => kv.Value)
                .Take(300)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in _current.Reading.ScrollPositions.Keys.ToList())
                if (!keep.Contains(key))
                    _current.Reading.ScrollPositions.Remove(key);

            foreach (var key in _current.Reading.LastSeen.Keys.ToList())
                if (!keep.Contains(key))
                    _current.Reading.LastSeen.Remove(key);
        }
    }

    // -----------------------------------------------------------------------
    // 便捷读写
    // -----------------------------------------------------------------------

    public void RememberFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var files = _current.Files;
        files.LastFile = path;
        files.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        files.Recent.Insert(0, new RecentFile
        {
            Path = path,
            Name = Path.GetFileName(path),
            OpenedAt = DateTimeOffset.Now,
        });
        Touch();
    }

    public void RemoveRecent(string path)
    {
        _current.Files.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        Touch();
    }

    public void RememberScroll(string path, double progress)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _current.Reading.ScrollPositions[path] = Math.Clamp(progress, 0, 1);
        _current.Reading.LastSeen[path] = DateTime.UtcNow.Ticks;
        Touch();
    }

    public double GetScroll(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        return _current.Reading.ScrollPositions.TryGetValue(path, out var v) ? v : 0;
    }

    public void Dispose()
    {
        // 先落盘再置位，否则 FlushNow 会因 _disposed 直接返回，退出时丢配置
        Flush();
        _disposed = true;
        _debounce.Dispose();
    }
}
