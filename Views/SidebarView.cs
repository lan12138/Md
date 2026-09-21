// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/SidebarView.cs
//  说明：左侧栏。三个页签：
//        · 大纲：文档标题树，点击跳转；滚动正文时自动高亮当前章节
//        · 文件：打开文件夹后的 Markdown 文件树，可展开 / 收起
//        · 最近：最近打开的文件，可移除失效记录
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  修两个问题：
//                   1) 页签格曾设了 Grid.SetRowSpan(tabs, 2)，使选中的页签按钮
//                      被拉伸成一整块强调色矩形铺满整个侧栏，与正文底色正面冲突。
//                      现在页签只占第一行，并按主题底色派生页签条 / 当前项底色。
//                   2) 新增「文件」页签承载文件夹浏览。
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Models;
using MD.Rendering;
using MD.Services;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Views;

/// <summary>大纲行。自己持有「是否当前章节」，滚动时只改颜色，不重建列表。</summary>
public sealed class OutlineRow : Border
{
    private readonly Label _label;
    private RenderContext _ctx;
    private bool _active;

    public OutlineNode Node { get; }

    public OutlineRow(OutlineNode node, RenderContext ctx)
    {
        Node = node;
        _ctx = ctx;

        _label = new Label
        {
            Text = node.Text,
            FontSize = 12.5,
            FontFamily = ctx.BodyFont,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        };

        Content = _label;
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) };
        Padding = new Thickness(8 + node.Indent * 12, 5, 8, 5);
        Margin = new Thickness(6, 1);
        BackgroundColor = Colors.Transparent;

        SemanticProperties.SetDescription(this, $"{new string('#', Math.Clamp(node.Level, 1, 6))} {node.Text}");

        Paint();

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            if (!_active)
                BackgroundColor = _ctx.CSidebarHoverBg;
        };
        pointer.PointerExited += (_, _) =>
        {
            if (!_active)
                BackgroundColor = Colors.Transparent;
        };
        GestureRecognizers.Add(pointer);
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        Paint();
    }

    public void SetActive(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        Paint();
    }

    private void Paint()
    {
        _label.TextColor = _active ? _ctx.CSidebarTextActive : _ctx.CSidebarText;
        _label.FontAttributes = _active ? FontAttributes.Bold : FontAttributes.None;
        // 当前章节用「比侧栏底色深/浅几个色号」的底色，而不是强调色实心块
        BackgroundColor = _active ? _ctx.CSidebarActiveBg : Colors.Transparent;
    }
}

/// <summary>文件夹树里的一行。目录可展开，文件可打开。</summary>
public sealed class FolderRowView : Border
{
    private readonly Label _arrow;
    private readonly Label _name;
    private readonly Label _dot;
    private readonly FolderRow _row;
    private RenderContext _ctx;

    public FolderRow Row => _row;

    public FolderRowView(FolderRow row, RenderContext ctx)
    {
        _row = row;
        _ctx = ctx;

        _arrow = new Label
        {
            Text = row.Glyph,
            FontSize = 11,
            FontFamily = ctx.BodyFont,
            TextColor = ctx.CTextSecondary,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 14,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        _name = new Label
        {
            Text = row.Name,
            FontSize = 12.5,
            FontFamily = ctx.BodyFont,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        };

        _dot = new Label
        {
            Text = "●",
            FontSize = 8,
            TextColor = ctx.CAccent,
            VerticalTextAlignment = TextAlignment.Center,
            IsVisible = row.IsOpen && !row.IsCurrent,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 3,
        };
        grid.Add(_arrow, 0, 0);
        grid.Add(_name, 1, 0);
        grid.Add(_dot, 2, 0);

        Content = grid;
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) };
        Padding = new Thickness(6 + row.Depth * 12, 4, 8, 4);
        Margin = new Thickness(4, 1);
        BackgroundColor = Colors.Transparent;

        SemanticProperties.SetDescription(this, row.IsDirectory ? $"文件夹 {row.Name}" : $"文件 {row.Name}");

        Paint();

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            if (!row.IsCurrent)
                BackgroundColor = _ctx.CSidebarHoverBg;
        };
        pointer.PointerExited += (_, _) =>
        {
            if (!row.IsCurrent)
                BackgroundColor = Colors.Transparent;
        };
        GestureRecognizers.Add(pointer);
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        Paint();
    }

    private void Paint()
    {
        bool current = _row.IsCurrent;
        _name.TextColor = current ? _ctx.CSidebarTextActive : (Row.IsDirectory ? _ctx.CText : _ctx.CSidebarText);
        _name.FontAttributes = current || Row.IsDirectory ? FontAttributes.Bold : FontAttributes.None;
        _arrow.TextColor = current ? _ctx.CSidebarTextActive : _ctx.CTextSecondary;
        _dot.IsVisible = _row.IsOpen && !current;
        BackgroundColor = current ? _ctx.CSidebarActiveBg : Colors.Transparent;
    }
}

public sealed class SidebarView : Grid
{
    private RenderContext _ctx;
    private readonly CollectionView _outlineList;
    private readonly CollectionView _recentList;
    private readonly CollectionView _folderList;
    private readonly Grid _tabs;
    private readonly BoxView _headerHairline;
    private readonly BoxView _separator;
    private readonly Label _folderCaption;
    private readonly List<WeakReference<OutlineRow>> _rows = new();
    private readonly List<WeakReference<FolderRowView>> _folderRows = new();

    private int _tab; // 0 = 大纲, 1 = 文件, 2 = 最近
    private MdDocument? _document;
    private int _activeBlockIndex = -1;
    private FolderNode? _folderRoot;
    private List<FolderRow> _folderRowsSource = new();

    public event Action<OutlineNode>? OutlineSelected;
    public event Func<string, Task>? RecentSelected;
    public event Action<string>? RecentRemoved;
    public event Func<string, Task>? FileSelected;
    public event Action<FolderNode>? FolderToggled;
    public event Func<Task>? OpenFolderRequested;

    public SidebarView(RenderContext ctx)
    {
        _ctx = ctx;

        _outlineList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() => new SidebarItemHost(SidebarKind.Outline, _ctx, this, OnOutlineRowCreated)),
            BackgroundColor = Colors.Transparent,
        };

        _folderList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() => new SidebarItemHost(SidebarKind.Folder, _ctx, this, null)),
            IsVisible = false,
            BackgroundColor = Colors.Transparent,
        };

        _recentList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 2 },
            ItemTemplate = new DataTemplate(() => new SidebarItemHost(SidebarKind.Recent, _ctx, this, null)),
            IsVisible = false,
            BackgroundColor = Colors.Transparent,
        };

        _tabs = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 4,
            Padding = new Thickness(8, 8, 8, 6),
        };

        _headerHairline = new BoxView { HeightRequest = 1 };
        _separator = new BoxView { WidthRequest = 1 };

        _folderCaption = new Label
        {
            FontSize = 11,
            FontFamily = ctx.BodyFont,
            TextColor = ctx.CTextSecondary,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            Padding = new Thickness(10, 6, 10, 4),
            IsVisible = false,
        };

        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 页签条
        RowDefinitions.Add(new RowDefinition(new GridLength(1))); // 页签条下分隔线
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 文件夹路径说明
        RowDefinitions.Add(new RowDefinition(GridLength.Star));   // 列表

        this.Add(_tabs, 0, 0);
        this.Add(_headerHairline, 0, 1);
        this.Add(_folderCaption, 0, 2);
        this.Add(_outlineList, 0, 3);
        this.Add(_folderList, 0, 3);
        this.Add(_recentList, 0, 3);
        this.Add(_separator, 1, 0);
        Grid.SetRowSpan(_separator, 4);

        BuildTabs();
        ApplyTheme(ctx);
    }

    private void OnOutlineRowCreated(OutlineRow row)
    {
        _rows.Add(new WeakReference<OutlineRow>(row));
        if (_activeBlockIndex >= 0)
            row.SetActive(row.Node.BlockIndex == _activeBlockIndex);
    }

    internal void OnFolderRowCreated(FolderRowView row)
    {
        _folderRows.Add(new WeakReference<FolderRowView>(row));
    }

    internal void RaiseOutlineSelected(OutlineNode node) => OutlineSelected?.Invoke(node);

    internal void RaiseFolderToggled(FolderNode node) => FolderToggled?.Invoke(node);

    internal void RaiseRecentSelected(string path)
    {
        if (RecentSelected is not null)
            _ = RecentSelected(path);
    }

    internal void RaiseFileSelected(string path)
    {
        if (FileSelected is not null)
            _ = FileSelected(path);
    }

    internal void RaiseRecentRemoved(string path) => RecentRemoved?.Invoke(path);

    internal RenderContext Context => _ctx;

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        BackgroundColor = ctx.CSidebarBg;
        _tabs.BackgroundColor = ctx.CSidebarHeaderBg;
        _headerHairline.Color = ctx.CSidebarBorder;
        _separator.Color = ctx.CSidebarBorder;
        _folderCaption.TextColor = ctx.CTextSecondary;
        BuildTabs();

        foreach (var weak in _rows.ToList())
        {
            if (weak.TryGetTarget(out var row))
                row.ApplyTheme(ctx);
            else
                _rows.Remove(weak);
        }

        foreach (var weak in _folderRows.ToList())
        {
            if (weak.TryGetTarget(out var row))
                row.ApplyTheme(ctx);
            else
                _folderRows.Remove(weak);
        }

        // 列表项本身也要重建才能换底色 / 文字色
        RefreshLists();
    }

    public void SetWidth(double width)
    {
        WidthRequest = Math.Clamp(width, 180, 640);
    }

    public int Tab
    {
        get => _tab;
        set => SetTab(value);
    }

    public string TabName => _tab switch
    {
        1 => "folder",
        2 => "recent",
        _ => "outline",
    };

    public void SetTabByName(string? name)
    {
        SetTab(name switch
        {
            "folder" => 1,
            "recent" => 2,
            _ => 0,
        });
    }

    public void SetTab(int tab)
    {
        if (_tab == tab)
        {
            RefreshLists();
            return;
        }
        _tab = tab;
        BuildTabs();
        RefreshLists();
    }

    private void BuildTabs()
    {
        _tabs.Clear();
        _tabs.Add(TabButton("大纲", 0), 0, 0);
        _tabs.Add(TabButton("文件", 1), 1, 0);
        _tabs.Add(TabButton("最近", 2), 2, 0);
    }

    /// <summary>
    /// 页签按钮：选中态用「比侧栏底色深/浅几个色号」的底色 + 强调色文字，
    /// 不再用强调色实心块（那会和正文底色正面冲突，非常突兀）。
    /// </summary>
    private View TabButton(string text, int index)
    {
        bool selected = _tab == index;

        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontFamily = _ctx.BodyFont,
            FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = selected ? _ctx.CSidebarTextActive : _ctx.CSidebarText,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var border = new Border
        {
            Content = label,
            BackgroundColor = selected ? _ctx.CSidebarActiveBg : Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Padding = new Thickness(4, 5),
            // 关键：不要让按钮在网格里被拉伸，否则会变成一整块色块
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetTab(index);
        border.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            if (_tab != index)
                border.BackgroundColor = _ctx.CSidebarHoverBg;
        };
        pointer.PointerExited += (_, _) =>
        {
            border.BackgroundColor = _tab == index ? _ctx.CSidebarActiveBg : Colors.Transparent;
        };
        border.GestureRecognizers.Add(pointer);

        return border;
    }

    public void SetDocument(MdDocument? document)
    {
        _document = document;
        _activeBlockIndex = -1;
        _rows.Clear();
        RefreshLists();
    }

    /// <summary>设置（或清空）文件夹树。</summary>
    public void SetFolder(FolderNode? root)
    {
        _folderRoot = root;
        _folderRows.Clear();
        RefreshLists();
    }

    /// <summary>当前所在文件夹路径（用于「文件」页签的说明行）。</summary>
    public void SetFolderCaption(string? folder)
    {
        _folderCaption.Text = string.IsNullOrEmpty(folder) ? string.Empty : folder;
        _folderCaption.IsVisible = _tab == 1 && !string.IsNullOrEmpty(folder);
    }

    public void ToggleFolder(FolderNode node)
    {
        if (!node.IsDirectory)
            return;
        node.IsExpanded = !node.IsExpanded;
        RefreshLists();
    }

    /// <summary>刷新文件树（当前文档 / 已打开标签变化后调用）。</summary>
    public void RefreshFolder(string? currentPath, IReadOnlyCollection<string> openPaths)
    {
        if (_folderRoot is null)
            return;

        FolderService.MarkState(_folderRoot, currentPath, openPaths);
        if (!string.IsNullOrEmpty(currentPath))
            FolderService.ExpandTo(_folderRoot, currentPath);

        if (_tab == 1)
            RefreshLists();
    }

    private void RefreshLists()
    {
        _outlineList.IsVisible = _tab == 0;
        _folderList.IsVisible = _tab == 1;
        _recentList.IsVisible = _tab == 2;
        _folderCaption.IsVisible = _tab == 1 && !string.IsNullOrWhiteSpace(_folderCaption.Text);

        if (_tab == 0)
        {
            _outlineList.ItemsSource = _document?.Outline;
        }
        else if (_tab == 1)
        {
            if (_folderRoot is null)
            {
                _folderList.ItemsSource = null;
                _folderList.EmptyView = BuildEmptyFolderView();
            }
            else
            {
                _folderRowsSource = new List<FolderRow>();
                FolderService.Flatten(_folderRoot, _folderRowsSource, 0);
                _folderList.EmptyView = null;
                _folderList.ItemsSource = _folderRowsSource;
            }
        }
        else
        {
            _recentList.ItemsSource = AppHost.Settings.Current.Files.Recent
                .Where(r => r.Exists)
                .ToList();
        }
    }

    private View BuildEmptyFolderView()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(14, 24),
        };
        stack.Add(new Label
        {
            Text = "还没有打开文件夹",
            FontSize = 12.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
        });
        stack.Add(new Label
        {
            Text = "打开一个文件夹后，可以在这里浏览、按标签页阅读其中的 Markdown 文件",
            FontSize = 11,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            LineHeight = 1.6,
        });

        var button = UiKit.FlatButton("打开文件夹", _ctx, async () =>
        {
            if (OpenFolderRequested is not null)
                await OpenFolderRequested();
        }, 12.5, primary: true, horizontalPadding: 16);
        button.HorizontalOptions = LayoutOptions.Center;
        stack.Add(button);

        return stack;
    }

    /// <summary>最近文件发生变化时刷新列表（当前不在「最近」页签也不会出错）。</summary>
    public void RefreshRecent()
    {
        if (_tab == 2)
            RefreshLists();
    }

    /// <summary>根据正文当前位置高亮大纲行。</summary>
    public void SetActiveBlock(int blockIndex)
    {
        if (_document is null || _document.Outline.Count == 0)
            return;

        int target = -1;
        OutlineNode? node = null;
        foreach (var item in _document.Outline)
        {
            if (item.BlockIndex <= blockIndex)
            {
                target = item.BlockIndex;
                node = item;
            }
            else
            {
                break;
            }
        }

        if (target == _activeBlockIndex)
            return;
        _activeBlockIndex = target;

        foreach (var weak in _rows.ToList())
        {
            if (weak.TryGetTarget(out var row))
                row.SetActive(row.Node.BlockIndex == target);
            else
                _rows.Remove(weak);
        }

        if (_tab == 0 && node is not null)
        {
            try
            {
                _outlineList.ScrollTo(node, position: ScrollToPosition.MakeVisible, animate: false);
            }
            catch
            {
                // 列表尚未完成布局时可能抛异常，忽略即可
            }
        }
    }
}

internal enum SidebarKind
{
    Outline,
    Folder,
    Recent,
}

/// <summary>根据绑定到的数据项类型生成对应的行控件。</summary>
internal sealed class SidebarItemHost : ContentView
{
    private readonly SidebarKind _kind;
    private readonly SidebarView _sidebar;
    private readonly Action<OutlineRow>? _onOutlineRow;
    private RenderContext _ctx;

    public SidebarItemHost(SidebarKind kind, RenderContext ctx, SidebarView sidebar, Action<OutlineRow>? onOutlineRow)
    {
        _kind = kind;
        _ctx = ctx;
        _sidebar = sidebar;
        _onOutlineRow = onOutlineRow;
        BackgroundColor = Colors.Transparent;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        _ctx = _sidebar.Context;

        switch (_kind)
        {
            case SidebarKind.Outline when BindingContext is OutlineNode node:
            {
                var row = new OutlineRow(node, _ctx);
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => _sidebar.RaiseOutlineSelected(node);
                row.GestureRecognizers.Add(tap);
                Content = row;
                _onOutlineRow?.Invoke(row);
                break;
            }

            case SidebarKind.Folder when BindingContext is FolderRow row:
            {
                var view = new FolderRowView(row, _ctx);
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) =>
                {
                    if (row.IsDirectory)
                        _sidebar.RaiseFolderToggled(row.Node);
                    else
                        _sidebar.RaiseFileSelected(row.Node.Path);
                };
                view.GestureRecognizers.Add(tap);
                Content = view;
                _sidebar.OnFolderRowCreated(view);
                break;
            }

            case SidebarKind.Recent when BindingContext is RecentFile file:
                Content = BuildRecentRow(file);
                break;

            default:
                Content = null;
                break;
        }
    }

    private View BuildRecentRow(RecentFile file)
    {
        var texts = new VerticalStackLayout { Spacing = 1 };
        texts.Add(new Label
        {
            Text = file.Name,
            FontSize = 12.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CText,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        });
        texts.Add(new Label
        {
            Text = file.SubTitle,
            FontSize = 10.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        });

        var remove = UiKit.GlyphButton("✕", "从列表移除", _ctx, () =>
        {
            _sidebar.RaiseRecentRemoved(file.Path);
            return Task.CompletedTask;
        }, 24, 11);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 4,
            Padding = new Thickness(10, 6),
        };
        grid.Add(texts, 0, 0);
        grid.Add(remove, 1, 0);

        var row = new Border
        {
            Content = grid,
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Margin = new Thickness(6, 1),
            Padding = 0,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _sidebar.RaiseRecentSelected(file.Path);
        row.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => row.BackgroundColor = _ctx.CSidebarHoverBg;
        pointer.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
        row.GestureRecognizers.Add(pointer);

        return row;
    }
}
