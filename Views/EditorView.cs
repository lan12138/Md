// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/EditorView.cs
//  说明：轻量编辑模式。阅读器主体是一个原生多行文本框（Editor），
//        进入 / 退出编辑由主页面控制（Ctrl+E），保存由 Ctrl+S 触发。
//
//        为什么是「轻量」：定位始终是阅读器，编辑只服务于「顺手改几个字」，
//        所以不引入 Markdown 语法高亮、不做实时预览、不接管快捷键体系，
//        但**编码必须原样保留** —— 这是硬要求，改坏了文件比不能编辑更糟。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Models;
using MD.Rendering;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Views;

public sealed class EditorView : Grid
{
    private RenderContext _ctx;
    private readonly Editor _editor;
    private readonly Border _infoBar;
    private readonly Label _info;
    private readonly Label _dirtyFlag;
    private readonly Border _saveButton;
    private readonly Border _exitButton;

    private DocumentTab? _tab;
    private bool _suppress;

    public event Func<Task>? SaveRequested;
    public event Action? ExitRequested;
    public event Action? TextEdited;

    public EditorView(RenderContext ctx)
    {
        _ctx = ctx;
        IsVisible = false;

        _editor = new Editor
        {
            AutoSize = EditorAutoSizeOption.Disabled,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0),
        };
        _editor.TextChanged += (_, _) =>
        {
            if (_suppress)
                return;
            if (_tab is not null && !_tab.IsModified)
            {
                _tab.IsModified = true;
                PaintInfo();
            }
            TextEdited?.Invoke();
        };

        _info = new Label
        {
            FontSize = 11,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        };

        _dirtyFlag = new Label
        {
            FontSize = 11,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        _saveButton = BuildActionButton("保存 (Ctrl+S)", primary: true, () =>
        {
            if (SaveRequested is not null)
                return SaveRequested();
            return Task.CompletedTask;
        });

        _exitButton = BuildActionButton("退出编辑 (Ctrl+E)", primary: false, () =>
        {
            ExitRequested?.Invoke();
            return Task.CompletedTask;
        });

        var left = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children = { _info, _dirtyFlag },
        };

        var right = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { _saveButton, _exitButton },
        };

        var bar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
            Padding = new Thickness(_ctx.ContentPadding, 7, _ctx.ContentPadding, 7),
        };
        bar.Add(left, 0, 0);
        bar.Add(right, 1, 0);

        _infoBar = new Border
        {
            Content = bar,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) },
            Padding = 0,
        };

        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        // 注意：Grid 的 Add(view, col, row) 是扩展方法，必须以 this. 显式调用
        this.Add(_infoBar, 0, 0);
        this.Add(_editor, 0, 1);

        ApplyTheme(ctx);
    }

    private Border BuildActionButton(string text, bool primary, Func<Task> onClick)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontFamily = _ctx.BodyFont,
            TextColor = primary ? Colors.White : _ctx.CToolbarText,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        var border = new Border
        {
            Content = label,
            BackgroundColor = primary ? _ctx.CAccent : _ctx.CControlBg,
            Stroke = primary ? Colors.Transparent : _ctx.CControlBorder,
            StrokeThickness = primary ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Padding = new Thickness(12, 5),
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try { await onClick(); } catch { /* 忽略交互异常 */ }
        };
        border.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            border.BackgroundColor = primary ? _ctx.CAccent.WithAlpha(0.86f) : _ctx.CSidebarHoverBg;
        };
        pointer.PointerExited += (_, _) =>
        {
            border.BackgroundColor = primary ? _ctx.CAccent : _ctx.CControlBg;
        };
        border.GestureRecognizers.Add(pointer);

        return border;
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;

        BackgroundColor = ctx.CEditorBg;
        _infoBar.BackgroundColor = ctx.CToolbarBg;
        _info.TextColor = ctx.CTextSecondary;
        _info.FontFamily = ctx.MonoFont;
        _dirtyFlag.TextColor = ctx.CAccent;
        _dirtyFlag.FontFamily = ctx.BodyFont;

        _editor.FontFamily = ctx.MonoFont;
        _editor.FontSize = ctx.BodySize;
        _editor.TextColor = ctx.CText;
        // Editor 自身没有内边距属性，用 Margin 让正文列宽与阅读模式对齐
        _editor.Margin = new Thickness(ctx.ContentPadding, 6, ctx.ContentPadding, ctx.ContentPadding);
        _editor.MaximumWidthRequest = ctx.ContentWidth > 40 ? ctx.ContentWidth : 100000;

        _saveButton.BackgroundColor = ctx.CAccent;
        _exitButton.BackgroundColor = ctx.CControlBg;
        _exitButton.Stroke = ctx.CControlBorder;

        PaintInfo();
    }

    /// <summary>装入某个标签的内容。</summary>
    public void Load(DocumentTab tab)
    {
        _tab = tab;
        _suppress = true;
        _editor.Text = tab.Text;
        _suppress = false;
        PaintInfo();
    }

    /// <summary>把编辑器里的文本同步回标签（切标签 / 退出编辑前调用）。</summary>
    public void SyncToTab()
    {
        if (_tab is null)
            return;

        var text = _editor.Text ?? string.Empty;
        if (!string.Equals(text, _tab.Text, StringComparison.Ordinal))
        {
            _tab.Text = text;
        }
    }

    /// <summary>编辑框当前文本。</summary>
    public string CurrentText => _editor.Text ?? string.Empty;

    public void FocusEditor() => _editor.Focus();

    public void SetReadOnlyHint(string text)
    {
        _info.Text = text;
    }

    private void PaintInfo()
    {
        if (_tab is null)
        {
            _info.Text = "未打开文档";
            _dirtyFlag.Text = string.Empty;
            return;
        }

        var text = _tab.Text ?? string.Empty;
        int lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                lines++;
        }

        var path = string.IsNullOrEmpty(_tab.Path) ? "（未落盘）" : _tab.Path;
        _info.Text = $"{path}    {_tab.EncodingDisplay} · {_tab.NewLineName} · {lines} 行 · {text.Length} 字符";
        _dirtyFlag.Text = _tab.IsModified ? "● 未保存" : "已保存";
    }

    /// <summary>内容变化后刷新信息条。</summary>
    public void Refresh()
    {
        if (_tab is not null)
            _tab.Text = CurrentText;
        PaintInfo();
    }
}
