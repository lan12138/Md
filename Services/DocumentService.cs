// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/DocumentService.cs
//  说明：文档加载与保存。负责
//        · 异步读取字节流 + 编码嗅探（记录编码 / BOM / 换行风格）
//        · 在后台线程完成 Markdown 解析（大文件不阻塞 UI）
//        · 按 (路径, 修改时间, 长度) 做解析结果缓存，重复打开同一文件时秒开
//        · 保存时严格按原编码原样写回（临时文件 + 原子替换）
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  缓存命中时不再谎报 utf-8（旧实现一律返回 "utf-8"，
//                   会让 GBK 文件在编辑保存后被写成 UTF-8）；
//                   新增 Format / Text 与 SaveAsync
// -----------------------------------------------------------------------------

using System.Diagnostics;
using MD.Markdown;
using MD.Models;

namespace MD.Services;

public sealed class LoadResult
{
    public MdDocument? Document { get; init; }
    public string? Error { get; init; }
    public string EncodingName { get; init; } = "utf-8";
    public TimeSpan Elapsed { get; init; }
    public long ByteLength { get; init; }
    public bool FromCache { get; init; }

    /// <summary>原文（已解码）。编辑模式需要它作为初始内容。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>原文件的编码 / BOM / 换行风格。</summary>
    public TextFileFormat Format { get; init; } = TextFileFormat.DefaultUtf8;

    public bool Success => Document is not null;
}

/// <summary>保存结果。</summary>
public sealed class SaveResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>内容与磁盘一致，未发生写入（避免无谓地刷新修改时间）。</summary>
    public bool Skipped { get; init; }

    /// <summary>有字符无法用原编码表示，已被替换，建议另存为 UTF-8。</summary>
    public bool Lossy { get; init; }

    public static SaveResult Ok(bool skipped = false, bool lossy = false) =>
        new() { Success = true, Skipped = skipped, Lossy = lossy };

    public static SaveResult Fail(string error) => new() { Error = error };
}

public sealed class DocumentService
{
    private readonly MarkdownParser _parser = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private sealed record CacheEntry(DateTime ModifiedUtc, long Length, MdDocument Document, string Text, TextFileFormat Format);

    /// <summary>从磁盘加载文档。</summary>
    public async Task<LoadResult> LoadFileAsync(string path, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(path))
            return new LoadResult { Error = "路径为空", Elapsed = sw.Elapsed };

        if (!File.Exists(path))
            return new LoadResult { Error = $"文件不存在：{path}", Elapsed = sw.Elapsed };

        try
        {
            var info = new FileInfo(path);
            lock (_gate)
            {
                if (_cache.TryGetValue(path, out var hit) &&
                    hit.ModifiedUtc == info.LastWriteTimeUtc &&
                    hit.Length == info.Length)
                {
                    return new LoadResult
                    {
                        Document = hit.Document,
                        Text = hit.Text,
                        Format = hit.Format,
                        EncodingName = hit.Format.EncodingName,
                        Elapsed = sw.Elapsed,
                        ByteLength = info.Length,
                        FromCache = true,
                    };
                }
            }

            byte[] bytes;
            await using (var stream = new FileStream(
                             path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                             bufferSize: 64 * 1024, useAsync: true))
            {
                bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await stream.ReadAsync(bytes.AsMemory(read, bytes.Length - read), ct).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    read += n;
                }
                if (read != bytes.Length)
                    Array.Resize(ref bytes, read);
            }

            ct.ThrowIfCancellationRequested();

            var decoded = await Task.Run(() => TextEncodingDetector.Decode(bytes), ct).ConfigureAwait(false);
            var format = TextFileFormat.From(decoded, decoded.Text);
            var document = await Task.Run(() => _parser.Parse(decoded.Text, path), ct).ConfigureAwait(false);

            lock (_gate)
            {
                if (_cache.Count > 24)
                    _cache.Clear();
                _cache[path] = new CacheEntry(info.LastWriteTimeUtc, info.Length, document, decoded.Text, format);
            }

            return new LoadResult
            {
                Document = document,
                Text = decoded.Text,
                Format = format,
                EncodingName = format.EncodingName,
                Elapsed = sw.Elapsed,
                ByteLength = info.Length,
            };
        }
        catch (OperationCanceledException)
        {
            return new LoadResult { Error = "已取消", Elapsed = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return new LoadResult { Error = $"{ex.GetType().Name}: {ex.Message}", Elapsed = sw.Elapsed };
        }
    }

    /// <summary>从字符串加载（用于剪贴板 / 内置示例）。</summary>
    public async Task<LoadResult> LoadTextAsync(string text, string virtualPath = "示例.md", CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var document = await Task.Run(() => _parser.Parse(text, virtualPath), ct).ConfigureAwait(false);
        return new LoadResult
        {
            Document = document,
            Text = text,
            Format = TextFileFormat.From(new DecodedText(text, "utf-8", false, new System.Text.UTF8Encoding(false)), text),
            EncodingName = "utf-8",
            Elapsed = sw.Elapsed,
            ByteLength = System.Text.Encoding.UTF8.GetByteCount(text),
        };
    }

    /// <summary>
    /// 解析一段文本但**不写缓存**（编辑后重新渲染用；此时磁盘内容还没变）。
    /// </summary>
    public Task<MdDocument> ParseAsync(string text, string path, CancellationToken ct = default)
        => Task.Run(() => _parser.Parse(text, path), ct);

    /// <summary>
    /// 保存文本。严格按 <paramref name="format"/> 指定的编码 / BOM / 换行风格写回，
    /// 与磁盘内容一致时不写（避免无意义地刷新修改时间）。
    /// </summary>
    public async Task<SaveResult> SaveAsync(string path, string text, TextFileFormat format, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return SaveResult.Fail("路径为空");

        try
        {
            var bytes = format.Encode(text, out bool lossy);

            // 内容没变就不写：既省 IO，也避免 Git 里出现「只改了 mtime」的噪声
            if (File.Exists(path))
            {
                try
                {
                    var existing = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                    if (existing.AsSpan().SequenceEqual(bytes))
                        return SaveResult.Ok(skipped: true);
                }
                catch
                {
                    // 读不回来就照常写
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // 临时文件 + 原子替换：断电 / 崩溃不会留下半截文件
            var temp = Path.Combine(directory ?? ".", $".{Path.GetFileName(path)}.md-save-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                if (File.Exists(path))
                    File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(temp, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // 清理失败无所谓
                }
            }

            Invalidate(path);
            return SaveResult.Ok(lossy: lossy);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"保存失败: {path}", ex);
            return SaveResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Invalidate(string path)
    {
        lock (_gate)
            _cache.Remove(path);
    }

    public void ClearCache()
    {
        lock (_gate)
            _cache.Clear();
    }
}
