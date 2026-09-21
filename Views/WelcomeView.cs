// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/WelcomeView.cs
//  说明：空状态首屏。没有打开文档时展示：品牌标识、打开入口、最近文件、快捷键提示。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Models;
using MD.Rendering;
using MD.Services;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Views;

public sealed class WelcomeView : Grid
{
    private RenderContext _ctx;

    public event Func<Task>? OpenRequested;
    public event Func<Task>? FolderRequested;
    public event Func<Task>? SampleRequested;
    public event Func<string, Task>? RecentRequested;

    public WelcomeView(RenderContext ctx)
    {
        _ctx = ctx;
        BackgroundColor = Colors.Transparent;
        Rebuild();
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        Rebuild();
    }

    public void Rebuild()
    {
        Children.Clear();
        BackgroundColor = _ctx.CEditorBg;

        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 520,
            Padding = new Thickness(28, 20),
        };

        stack.Add(BuildMark());
        stack.Add(new Label
        {
            Text = "MD",
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CHeading,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        });
        stack.Add(new Label
        {
            Text = "跨平台 Markdown 阅读器 · .NET 10 + MAUI",
            FontSize = 12.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var actions = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 26, 0, 0),
        };
        actions.Add(UiKit.FlatButton("打开 Markdown 文件", _ctx,
            async () => { if (OpenRequested is not null) await OpenRequested(); },
            fontSize: 13.5, primary: true, horizontalPadding: 20));
        actions.Add(UiKit.FlatButton("打开文件夹", _ctx,
            async () => { if (FolderRequested is not null) await FolderRequested(); },
            fontSize: 13.5, horizontalPadding: 18));
        actions.Add(UiKit.FlatButton("打开示例文档", _ctx,
            async () => { if (SampleRequested is not null) await SampleRequested(); },
            fontSize: 13.5, horizontalPadding: 18));
        stack.Add(actions);

        stack.Add(new Label
        {
            Text = "打开文件夹后，侧栏「文件」页签会列出其中的 Markdown 文件，逐篇阅读自动进入标签页管理",
            FontSize = 11.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 1.6,
        });

        var recent = BuildRecent();
        if (recent is not null)
            stack.Add(recent);

        stack.Add(new Label
        {
            Text = "Ctrl+O 打开文件 · Ctrl+Shift+O 打开文件夹 · Ctrl+E 编辑 · Ctrl+S 保存\nCtrl+B 侧栏 · Ctrl+加减 或 Ctrl+滚轮 调字号 · Ctrl+F 专注模式 · F5 重新加载",
            FontSize = 11.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0),
            LineHeight = 1.7,
        });

        Add(stack);
    }

    private View BuildMark()
    {
        var border = new Border
        {
            WidthRequest = 84,
            HeightRequest = 84,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            HorizontalOptions = LayoutOptions.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb("#4F46E5"), Offset = 0f },
                    new GradientStop { Color = Color.FromArgb("#6D28D9"), Offset = 0.55f },
                    new GradientStop { Color = Color.FromArgb("#0EA5E9"), Offset = 1f },
                },
            },
            Content = new Label
            {
                Text = "M↓",
                FontSize = 30,
                FontAttributes = FontAttributes.Bold,
                FontFamily = _ctx.MonoFont,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };
        return border;
    }

    private View? BuildRecent()
    {
        var recent = AppHost.Settings.Current.Files.Recent
            .Where(r => r.Exists)
            .Take(5)
            .ToList();

        if (recent.Count == 0)
            return null;

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            Margin = new Thickness(0, 26, 0, 0),
        };

        stack.Add(new Label
        {
            Text = "最近打开",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            Margin = new Thickness(4, 0, 0, 6),
        });

        foreach (var file in recent)
            stack.Add(BuildRecentRow(file));

        return stack;
    }

    private View BuildRecentRow(RecentFile file)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 10,
            Padding = new Thickness(10, 7),
        };

        grid.Add(new Label
        {
            Text = "📄",
            FontSize = 13,
            VerticalTextAlignment = TextAlignment.Center,
        }, 0, 0);

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
        grid.Add(texts, 1, 0);

        var row = new Border
        {
            Content = grid,
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Padding = 0,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (RecentRequested is not null)
                await RecentRequested(file.Path);
        };
        row.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => row.BackgroundColor = _ctx.CSidebarHoverBg;
        pointer.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
        row.GestureRecognizers.Add(pointer);

        return row;
    }
}
