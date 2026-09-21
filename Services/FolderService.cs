// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Services/FolderService.cs
//  说明：打开文件夹后扫描其中的 Markdown 文件，形成可展开的树。
//        · 只收录 Markdown 相关扩展名
//        · 默认递归，但跳过 bin / obj / node_modules / .git 等噪声目录
//        · 单次扫描设上限，避免误选 C:\ 这类目录时把界面拖死
//        · 扫描在后台线程完成，UI 只拿到结果
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Models;

namespace MD.Services;

public sealed class FolderScanResult
{
    public FolderNode? Root { get; init; }
    public string? Error { get; init; }
    public int FileCount { get; init; }
    public bool Truncated { get; init; }

    public bool Success => Root is not null;
}

public sealed class FolderService
{
    /// <summary>单次扫描最多收录的文件数。</summary>
    private const int MaxFiles = 4000;

    /// <summary>递归深度上限。</summary>
    private const int MaxDepth = 12;

    private static readonly string[] MarkdownExtensions =
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mdx", ".mdtext", ".text", ".txt",
    };

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".vscode",
        "packages", "dist", "build", ".nuget", "__pycache__", "target",
    };

    public static bool IsMarkdownFile(string path) =>
        MarkdownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>扫描文件夹。</summary>
    public Task<FolderScanResult> ScanAsync(string folder, bool recursive, CancellationToken ct = default)
        => Task.Run(() => Scan(folder, recursive, ct), ct);

    private static FolderScanResult Scan(string folder, bool recursive, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return new FolderScanResult { Error = $"文件夹不存在：{folder}" };

            var full = Path.GetFullPath(folder);
            var root = new FolderNode
            {
                Path = full,
                Name = new DirectoryInfo(full).Name is { Length: > 0 } n ? n : full,
                IsDirectory = true,
                IsExpanded = true,
                Depth = 0,
            };

            int budget = MaxFiles;
            bool truncated = false;
            var rootCompleted = Fill(root, recursive, 0, ref budget, ref truncated, ct);

            root.FileCount = rootCompleted;
            return new FolderScanResult { Root = root, FileCount = rootCompleted, Truncated = truncated };
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"扫描文件夹失败: {folder}", ex);
            return new FolderScanResult { Error = ex.Message };
        }
    }

    /// <summary>填充某个目录的子节点，返回该子树下的文件数。</summary>
    private static int Fill(FolderNode dir, bool recursive, int depth, ref int budget, ref bool truncated, CancellationToken ct)
    {
        if (depth > MaxDepth || budget <= 0)
            return 0;

        int files = 0;

        IEnumerable<string> subDirs;
        IEnumerable<string> entries;
        try
        {
            subDirs = Directory.EnumerateDirectories(dir.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            entries = Directory.EnumerateFiles(dir.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // 无权限的目录直接跳过，不影响其它分支
            return 0;
        }

        // 文件在前、目录在后会让树看起来头重脚轻；目录在前更符合资源管理器习惯
        if (recursive)
        {
            foreach (var sub in subDirs)
            {
                ct.ThrowIfCancellationRequested();
                if (budget <= 0)
                {
                    truncated = true;
                    break;
                }

                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || SkipDirectories.Contains(name))
                    continue;

                var node = new FolderNode
                {
                    Path = sub,
                    Name = name,
                    IsDirectory = true,
                    Depth = depth + 1,
                };

                int count = Fill(node, recursive: true, depth + 1, ref budget, ref truncated, ct);
                if (count > 0)
                {
                    node.FileCount = count;
                    dir.Children.Add(node);
                    files += count;
                }
            }
        }

        foreach (var file in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (budget <= 0)
            {
                truncated = true;
                break;
            }

            if (!IsMarkdownFile(file))
                continue;

            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;

            dir.Children.Add(new FolderNode
            {
                Path = file,
                Name = name,
                IsDirectory = false,
                Depth = depth + 1,
            });

            files++;
            budget--;
        }

        return files;
    }

    /// <summary>把树按当前展开状态压平成可见行列表。</summary>
    public static void Flatten(FolderNode node, List<FolderRow> output, int depth)
    {
        foreach (var child in node.Children)
        {
            output.Add(new FolderRow(child, depth));
            if (child.IsDirectory && child.IsExpanded)
                Flatten(child, output, depth + 1);
        }
    }

    /// <summary>在树中查找路径对应的节点。</summary>
    public static FolderNode? Find(FolderNode node, string path)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDirectory && string.Equals(child.Path, path, StringComparison.OrdinalIgnoreCase))
                return child;
            if (child.IsDirectory)
            {
                var hit = Find(child, path);
                if (hit is not null)
                    return hit;
            }
        }
        return null;
    }

    /// <summary>展开到指定文件（把祖先目录逐级展开），返回是否命中。</summary>
    public static bool ExpandTo(FolderNode node, string path)
    {
        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                if (ExpandTo(child, path))
                {
                    child.IsExpanded = true;
                    return true;
                }
            }
            else if (string.Equals(child.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>清除所有 IsCurrent / IsOpen 标记，再按当前状态重设。</summary>
    public static void MarkState(FolderNode node, string? currentPath, IReadOnlyCollection<string> openPaths)
    {
        foreach (var child in node.Children)
        {
            child.IsCurrent = !child.IsDirectory &&
                              !string.IsNullOrEmpty(currentPath) &&
                              string.Equals(child.Path, currentPath, StringComparison.OrdinalIgnoreCase);

            child.IsOpen = !child.IsDirectory && openPaths.Contains(child.Path, StringComparer.OrdinalIgnoreCase);

            if (child.IsDirectory)
                MarkState(child, currentPath, openPaths);
        }
    }
}
