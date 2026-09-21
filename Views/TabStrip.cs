// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/TabStrip.cs
//  说明：文档标签栏。打开文件夹后逐篇阅读时，用它管理已打开的文档：
//        · 点击切换、× 关闭（有未保存修改时先问过用户）
//        · 未保存的标签在标题后显示实心圆点
//        · 标签过多时横向滚动，当前标签自动滚入视野
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Models;
using MD.Rendering;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Views;

/// <summary>标签栏需要的最小信息。用独立的轻量类型，避免把整个 DocumentTab 暴露给视图。</summary>
public sealed record TabChip(string Id, string Title, bool IsModified, bool IsActive, string Tooltip);

public sealed class TabStrip : Grid
{
    private RenderContext _ctx;
    private readonly ScrollView _scroller;
    private readonly HorizontalStackLayout _items;
    private readonly BoxView _hairline;
    private readonly Label _emptyHint;

    private List<TabChip> _tabs = new();

    public event Action<string>? TabSelected;
    public event Action<string>? TabClosed;

    public TabStrip(RenderContext ctx)
    {
        _ctx = ctx;

        _items = new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(8, 6, 8, 6) };
        _scroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _items,
        };

        _emptyHint = new Label
        {
            Text = "打开文件夹后，这里会列出正在阅读的文档",
            FontSize = 11.5,
            FontFamily = ctx.BodyFont,
            TextColor = ctx.CTextSecondary,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsVisible = false,
        };

        _hairline = new BoxView { HeightRequest = 1 };

        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1)));

        // 注意：Grid 的 Add(view, col, row) 是扩展方法，必须以 this. 显式调用
        this.Add(_scroller, 0, 0);
        this.Add(_emptyHint, 0, 0);
        this.Add(_hairline, 0, 1);

        ApplyTheme(ctx);
    }

    public void SetTabs(IReadOnlyList<TabChip> tabs)
    {
        _tabs = tabs.ToList();
        Rebuild();
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        BackgroundColor = ctx.CToolbarBg;
        _scroller.BackgroundColor = Colors.Transparent;
        _hairline.Color = ctx.CToolbarBorder;
        _emptyHint.TextColor = ctx.CTextSecondary;
        _emptyHint.FontFamily = ctx.BodyFont;
        Rebuild();
    }

    private void Rebuild()
    {
        _items.Clear();

        bool any = _tabs.Count > 0;
        _scroller.IsVisible = any;
        _emptyHint.IsVisible = !any;

        foreach (var tab in _tabs)
            _items.Add(BuildChip(tab));
    }

    private View BuildChip(TabChip tab)
    {
        var title = new Label
        {
            Text = tab.Title,
            FontSize = 12,
            FontFamily = _ctx.BodyFont,
            FontAttributes = tab.IsActive ? FontAttributes.Bold : FontAttributes.None,
            TextColor = tab.IsActive ? _ctx.CSidebarTextActive : _ctx.CTextSecondary,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            MaximumWidthRequest = 190,
        };

        var dot = new Label
        {
            Text = "●",
            FontSize = 8,
            TextColor = _ctx.CAccent,
            VerticalTextAlignment = TextAlignment.Center,
            IsVisible = tab.IsModified,
        };

        var close = new Label
        {
            Text = "✕",
            FontSize = 10,
            TextColor = tab.IsActive ? _ctx.CTextSecondary : _ctx.CTextSecondary,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 16,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 5,
        };
        grid.Add(title, 0, 0);
        grid.Add(dot, 1, 0);
        grid.Add(close, 2, 0);

        var chip = new Border
        {
            Content = grid,
            BackgroundColor = tab.IsActive ? _ctx.CSidebarActiveBg : Colors.Transparent,
            Stroke = tab.IsActive ? _ctx.CAccent : _ctx.CToolbarBorder,
            StrokeThickness = tab.IsActive ? 1 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Padding = new Thickness(10, 4, 5, 4),
            VerticalOptions = LayoutOptions.Center,
        };

        SemanticProperties.SetDescription(chip, tab.Tooltip);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => TabSelected?.Invoke(tab.Id);
        chip.GestureRecognizers.Add(tap);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (_, _) => TabClosed?.Invoke(tab.Id);
        close.GestureRecognizers.Add(closeTap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            if (!tab.IsActive)
                chip.BackgroundColor = _ctx.CSidebarHoverBg;
        };
        pointer.PointerExited += (_, _) =>
        {
            if (!tab.IsActive)
                chip.BackgroundColor = Colors.Transparent;
        };
        chip.GestureRecognizers.Add(pointer);

        return chip;
    }

    /// <summary>把当前标签滚入视野。</summary>
    public void EnsureActiveVisible()
    {
        int index = _tabs.FindIndex(t => t.IsActive);
        if (index < 0 || index >= _items.Count)
            return;

        try
        {
            if (_items.Children[index] is not View child)
                return;

            // ScrollView 只有「按坐标滚动」这一个异步接口，元素版本在 MAUI 10 里没有，
            // 这里用子控件的布局 X 估算目标位置（留 80px 让上一个标签露一点）。
            var x = Math.Max(0, child.X - 80);
            _ = _scroller.ScrollToAsync(x, 0, false);
        }
        catch
        {
            // 布局尚未完成时忽略
        }
    }
}
