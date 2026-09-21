// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/ReaderPage.cs
//  说明：主阅读页。整页用 C# 构建（不依赖 XAML），结构为：
//
//        ┌──────────────────────────────────────────────────────┐
//        │ 工具栏  侧栏开关 / 文件名 / 打开 · 字号 · 主题 · 设置 │
//        ├──────────────────────────────────────────────────────┤
//        │ 标签栏（打开文件夹后逐篇阅读时出现）                  │
//        ├────────────┬─────────────────────────────────────────┤
//        │  侧栏       │  阅读区（虚拟化块列表，列宽随窗口自适应）│
//        │ 大纲|文件|最近│ 或 编辑区（轻量编辑，保存保持原编码）   │
//        ├────────────┴─────────────────────────────────────────┤
//        │ 状态栏  编码·换行·字数·行数    当前章节        进度    │
//        └──────────────────────────────────────────────────────┘
//        + 设置覆盖层 + 轻提示
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  按反馈整改：
//                    · 新增标签栏管理多文档、新增「打开文件夹」
//                    · 新增轻量编辑（保存严格保持原编码 / BOM / 换行）
//                    · 正文列宽改为随窗口自适应（全屏不再只有窄窄一条）
//                    · 接管滚轮：焦点在代码块上也能翻页；Ctrl+滚轮调字号
//                    · 状态栏补充换行风格与修改状态
//  修改：2026-09-21  单实例运行：接管第二个实例转交来的参数，
//                   在已有窗口里开成新标签并把窗口提前台
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Markdown;
using MD.Models;
using MD.Rendering;
using MD.Services;
using Microsoft.Maui.Controls.Shapes;

// Microsoft.Maui.Controls.Shapes 里也有一个 Path（矢量图形），会与 System.IO.Path 冲突
using Path = System.IO.Path;

namespace MD.Views;

public sealed class ReaderPage : ContentPage
{
    private readonly SettingsService _settings = AppHost.Settings;
    private readonly ThemeCatalog _themes = AppHost.Themes;
    private readonly DocumentService _docSvc = AppHost.Documents;
    private readonly FolderService _folders = AppHost.Folders;
    private readonly RenderContext _ctx;

    // ---- 布局 ----
    private readonly Grid _root;
    private readonly Border _toolbar;
    private readonly BoxView _toolbarHairline;
    private readonly Grid _toolbarHost;
    private readonly TabStrip _tabs;
    private readonly Grid _body;
    private readonly SidebarView _sidebar;
    private readonly Grid _readerHost;
    private readonly CollectionView _blocks;
    private readonly EditorView _editor;
    private readonly WelcomeView _welcome;
    private readonly ProgressBar _progress;
    private readonly Grid _statusBar;
    private readonly Label _statusLeft;
    private readonly Label _statusCenter;
    private readonly Label _statusRight;
    private readonly SettingsPanel _settingsPanel;
    private readonly Toast _toast;
    private readonly Border _focusExit;

    // ---- 状态 ----
    private readonly List<WeakReference<BlockHost>> _hosts = new();
    private readonly List<DocumentTab> _openTabs = new();
    private DocumentTab? _active;
    private FolderNode? _folderRoot;
    private string _loadSummary = string.Empty;
    private double _progressValue;
    private bool _isLoading;
    private bool _dragActive;
    private string _lastLightThemeId = "github-light";
    private CancellationTokenSource? _loadCts;
    private bool _restorePending;

    // Ctrl+滚轮调字号的合并触发（避免每一格滚轮都重建一次控件树）
    private double _pendingFontDelta;
    private bool _fontFlushQueued;

    public ReaderPage()
    {
        // 先建 Toast：下面的上下文回调、侧栏回调在赋值之后就会被引用；
        // 提前赋值也能让可空分析确认该字段非空（否则报 CS8602）。
        _toast = new Toast();

        var theme = _themes.Resolve(_settings.Current, IsSystemDark());

        // 注意：用局部变量承载上下文，再赋给 _ctx。
        // 若直接在对象初始化器里写 `Notify = msg => _toast.Show(_ctx, msg)`，
        // 此时 _ctx 尚在赋值过程中，可空分析会判定其为 null（CS8602 / CS8604）。
        var ctx = new RenderContext(theme, _settings.Current);
        ctx.Notify = message => _toast.Show(ctx, message);
        ctx.LinkHandler = HandleLinkAsync;
        _ctx = ctx;

        Title = "MD";

        // ---------------- 工具栏 ----------------
        _toolbarHost = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            Padding = new Thickness(ToolbarPaddingLeft, 7, 10, 7),
        };

        _toolbar = new Border
        {
            Content = _toolbarHost,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) },
            Padding = 0,
        };

        // ---------------- 标签栏 ----------------
        _tabs = new TabStrip(_ctx);
        _tabs.TabSelected += ActivateTabById;
        _tabs.TabClosed += id => _ = CloseTabAsync(FindTab(id));

        // ---------------- 侧栏 ----------------
        _sidebar = new SidebarView(_ctx);
        _sidebar.OutlineSelected += node => ScrollToBlock(node.BlockIndex);
        _sidebar.RecentSelected += path => OpenFileAsync(path);
        _sidebar.FileSelected += path => OpenFileAsync(path);
        _sidebar.FolderToggled += node => _sidebar.ToggleFolder(node);
        _sidebar.OpenFolderRequested += PickFolderAsync;
        _sidebar.RecentRemoved += path =>
        {
            _settings.RemoveRecent(path);
            _sidebar.SetDocument(_active?.Document);
            _toast.Show(_ctx, "已从最近列表移除");
        };
        _sidebar.SetTabByName(_settings.Current.Workspace.SidebarTab);
        _sidebar.IsVisible = _settings.Current.Window.SidebarVisible && !_settings.Current.Reading.FocusMode;

        // ---------------- 阅读区 ----------------
        _blocks = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() =>
            {
                var host = new BlockHost(_ctx);
                _hosts.Add(new WeakReference<BlockHost>(host));
                return host;
            }),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(_ctx.ContentPadding, 8, _ctx.ContentPadding, 34),
        };
        _blocks.Scrolled += (_, e) => OnScrolled(e);

        _editor = new EditorView(_ctx);
        _editor.SaveRequested += SaveActiveAsync;
        _editor.ExitRequested += () =>
        {
            _ = SetEditModeAsync(false);
        };
        _editor.TextEdited += () =>
        {
            RefreshTabStrip();
            UpdateStatusBar();
        };

        _welcome = new WelcomeView(_ctx);
        _welcome.OpenRequested += PickFileAsync;
        _welcome.FolderRequested += PickFolderAsync;
        _welcome.SampleRequested += OpenSampleAsync;
        _welcome.RecentRequested += OpenFileAsync;

        _progress = new ProgressBar
        {
            Progress = 0,
            HeightRequest = 2,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
        };

        _readerHost = new Grid();
        _readerHost.Add(_welcome);
        _readerHost.Add(_blocks);
        _readerHost.Add(_editor);
        _readerHost.Add(_progress);
        // 阅读区宽度变化时重算正文列宽（全屏 / 还原窗口 / 拖动侧栏都会触发）
        _readerHost.SizeChanged += (_, _) => OnReaderSizeChanged();

        // ---------------- 状态栏 ----------------
        _statusLeft = StatusLabel(TextAlignment.Start);
        _statusCenter = StatusLabel(TextAlignment.Center);
        _statusRight = StatusLabel(TextAlignment.End);

        _statusBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 12,
            Padding = new Thickness(12, 0, 12, 0),
            HeightRequest = 26,
        };
        _statusBar.Add(_statusLeft, 0, 0);
        _statusBar.Add(_statusCenter, 1, 0);
        _statusBar.Add(_statusRight, 2, 0);

        // ---------------- 主体 ----------------
        _body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 0,
        };
        _body.Add(_sidebar, 0, 0);
        _body.Add(_readerHost, 1, 0);

        // ---------------- 根 ----------------
        _root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // 工具栏
                new RowDefinition(new GridLength(1)), // 工具栏下分隔线
                new RowDefinition(GridLength.Auto),   // 标签栏
                new RowDefinition(GridLength.Star),   // 主体
                new RowDefinition(GridLength.Auto),   // 状态栏
            },
        };

        _toolbarHairline = new BoxView { HeightRequest = 1 };
        _root.Add(_toolbar, 0, 0);
        _root.Add(_toolbarHairline, 0, 1);
        _root.Add(_tabs, 0, 2);
        _root.Add(_body, 0, 3);
        _root.Add(_statusBar, 0, 4);

        _settingsPanel = new SettingsPanel(_ctx, OnLayoutSettingsChanged, message => _toast.Show(_ctx, message));
        _settingsPanel.CloseRequested += () => _settingsPanel.Hide();

        _focusExit = BuildFocusExitButton();
        _focusExit.IsVisible = false;

        _root.Add(_settingsPanel, 0, 0);
        Grid.SetRowSpan(_settingsPanel, 5);
        _root.Add(_toast, 0, 3);
        _root.Add(_focusExit, 0, 0);
        Grid.SetRowSpan(_focusExit, 5);

        Content = _root;

        ApplyThemeInternal(rebuildBlocks: false);
        _sidebar.SetWidth(_settings.Current.Window.SidebarWidth);

        // 系统明暗切换时自动换肤
        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += OnSystemThemeChanged;

        AttachMenu();
        HookDropTarget();

        DiagnosticsLog.Write("ReaderPage 构造完成");
    }

    private static double ToolbarPaddingLeft
    {
        get
        {
#if ANDROID
            return 6;
#else
            return 12;
#endif
        }
    }

    private static bool IsSystemDark() => Application.Current?.RequestedTheme == AppTheme.Dark;

    private static Label StatusLabel(TextAlignment alignment) => new()
    {
        FontSize = 11,
        LineBreakMode = LineBreakMode.TailTruncation,
        VerticalTextAlignment = TextAlignment.Center,
        HorizontalTextAlignment = alignment,
    };

    private Border BuildFocusExitButton()
    {
        var button = UiKit.GlyphButton("⤢", "退出专注模式 (Ctrl+F)", _ctx, () =>
        {
            _settings.Current.Reading.FocusMode = false;
            _settings.Touch();
            UpdateChrome();
            return Task.CompletedTask;
        }, 30, 14);
        button.HorizontalOptions = LayoutOptions.End;
        button.VerticalOptions = LayoutOptions.Start;
        button.Margin = new Thickness(0, 8, 10, 0);

        return new Border
        {
            Content = button,
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
        };
    }

    // =======================================================================
    // 窗口接入：滚轮路由
    // =======================================================================

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if WINDOWS
        // 页面根元素就绪后挂滚轮路由：
        //  · Ctrl + 滚轮 = 调字号
        //  · 其余情况，若指针所在 ScrollViewer 不能纵向滚动（代码块就是），
        //    把滚动转交给阅读区列表，避免「焦点在代码块上滚轮翻页失效」
        if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement root)
        {
            Platforms.Windows.WindowsWheelRouter.Attach(root, step => QueueFontSizeChange(step * 0.5));
            DiagnosticsLog.Write("已挂载滚轮路由");
        }
#endif
    }

    /// <summary>
    /// 应用「窗口外壳」配色。主题应用点（ApplyThemeInternal）在页面构造期就会跑一次，
    /// 那时 WinUI 的 AppWindow 还没创建；等 Window.Created 之后再补一次，
    /// 系统绘制的标题栏才会真的跟着主题走。
    /// </summary>
    public void ApplyWindowChrome()
    {
#if WINDOWS
        // 系统标题栏（含菜单栏所在的那一条）按主题上色，见 WindowsTitleBar 里的说明
        Platforms.Windows.WindowsTitleBar.Apply(_ctx);
#endif
    }

    /// <summary>
    /// 把窗口提到前台。单实例模式下用户双击第二篇文档时，文件是在**已有窗口**里
    /// 打开的（见 Services/SingleInstance.cs），不显式提前台的话用户会以为程序没反应。
    /// </summary>
    public void ActivateWindow()
    {
#if WINDOWS
        Platforms.Windows.WindowsActivation.Activate();
#endif
    }

    // ------------------------------------------------------------------
    // 单实例：接收第二个实例转交来的命令行参数
    // ------------------------------------------------------------------

    /// <summary>
    /// 由 SingleInstance 在**后台线程**上调用，所以这里只负责切回 UI 线程。
    /// </summary>
    private void OnSecondInstanceArgs(string[] args)
    {
        Dispatcher.Dispatch(() => _ = HandleSecondInstanceAsync(args));
    }

    private async Task HandleSecondInstanceAsync(string[] args)
    {
        try
        {
            // 先提前台，再打开文件：用户双击后第一眼要看到的就是窗口跳出来
            ActivateWindow();

            var (file, folder) = ParseCommandLine(args, startIndex: 0);

            if (folder is not null)
            {
                DiagnosticsLog.Write($"[单实例] 打开文件夹: {folder}");
                await OpenFolderAsync(folder);
            }

            if (file is not null)
            {
                DiagnosticsLog.Write($"[单实例] 打开文件: {file}");
                await OpenFileAsync(file);
                _toast.Show(_ctx, "已在当前窗口打开：" + Path.GetFileName(file));
                return;
            }

            if (folder is not null)
                return;

            // 第二个实例没带参数（例如又双击了一次 exe）：只把窗口提到前台
            DiagnosticsLog.Write("[单实例] 本次转交未带文件/文件夹，仅激活窗口");
            _toast.Show(_ctx, "MD 已在运行");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("处理第二个实例的参数失败", ex);
            _toast.Show(_ctx, "打开失败：" + ex.Message);
        }
    }

    /// <summary>把连续的滚轮缩放合并成一次重建，滚动鼠标滚轮时才不会卡。</summary>
    private void QueueFontSizeChange(double delta)
    {
        _pendingFontDelta += delta;
        if (_fontFlushQueued)
            return;

        _fontFlushQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(70), () =>
        {
            _fontFlushQueued = false;
            var applied = _pendingFontDelta;
            _pendingFontDelta = 0;
            if (Math.Abs(applied) < 0.001)
                return;
            ChangeFontSize(applied);
        });
    }

    // =======================================================================
    // 主题
    // =======================================================================

    private void OnSystemThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        if (!_settings.Current.Appearance.FollowSystemTheme)
            return;

        Dispatcher.Dispatch(() => ApplyThemeInternal(rebuildBlocks: true));
    }

    /// <summary>重新解析主题并刷新整棵可视树。</summary>
    private void ApplyThemeInternal(bool rebuildBlocks)
    {
        var theme = _themes.Resolve(_settings.Current, IsSystemDark());
        _ctx.Update(theme, _settings.Current);

        // WinUI 侧的亮暗也必须跟着走：
        //  · 顶部那条标题栏区域是 MAUI「扩展进标题栏」自己画的，底色来自 WinUI 主题背景，
        //    不切的话暗色主题下会留一条系统亮色的带子；
        //  · 菜单、下拉、滚动条、右键菜单等系统控件的配色同样由它决定。
        // 放在这里而不是只设一次，是因为主题可以随时热切换。
        var desired = theme.IsDark ? AppTheme.Dark : AppTheme.Light;
        if (Application.Current is { } app && app.UserAppTheme != desired)
            app.UserAppTheme = desired;

        BackgroundColor = _ctx.CEditorBg;
        _toolbar.BackgroundColor = _ctx.CToolbarBg;
        _toolbar.Stroke = _dragActive ? _ctx.CAccent : _ctx.CToolbarBorder;
        _toolbar.StrokeThickness = _dragActive ? 2 : 1;
        _statusBar.BackgroundColor = _ctx.CStatusBarBg;
        _progress.ProgressColor = _ctx.CAccent;
        _readerHost.BackgroundColor = _ctx.CEditorBg;
        _body.BackgroundColor = _ctx.CEditorBg;

        foreach (var label in new[] { _statusLeft, _statusCenter, _statusRight })
        {
            label.TextColor = _ctx.CStatusBarText;
            label.FontFamily = _ctx.BodyFont;
        }

        _blocks.Margin = new Thickness(_ctx.ContentPadding, 8, _ctx.ContentPadding, 34);
        ApplyContentWidth();

        BuildToolbar();
        _tabs.ApplyTheme(_ctx);
        _welcome.ApplyTheme(_ctx);
        _sidebar.ApplyTheme(_ctx);
        _sidebar.SetWidth(_settings.Current.Window.SidebarWidth);
        _editor.ApplyTheme(_ctx);
        _settingsPanel.ApplyTheme(_ctx);
        _toast.NotifyThemeChanged(_ctx);
        RebuildFocusExit();

        // 窗口外壳（系统标题栏 / 窗口按钮）也要跟着主题走，
        // 否则暗色主题下顶上会留一条亮带
        ApplyWindowChrome();

        if (rebuildBlocks)
            RebuildBlocks();

        RefreshTabStrip();
        UpdateChrome();
        UpdateStatusBar();
    }

    private void RebuildFocusExit()
    {
        var inner = UiKit.GlyphButton("⤢", "退出专注模式 (Ctrl+F)", _ctx, () =>
        {
            _settings.Current.Reading.FocusMode = false;
            _settings.Touch();
            UpdateChrome();
            return Task.CompletedTask;
        }, 30, 14);
        inner.HorizontalOptions = LayoutOptions.End;
        inner.VerticalOptions = LayoutOptions.Start;
        inner.Margin = new Thickness(0, 8, 10, 0);
        _focusExit.Content = inner;
        _focusExit.BackgroundColor = _ctx.CWindowBg.WithAlpha(0.75f);
    }

    /// <summary>阅读区尺寸变化 → 重算正文列宽。</summary>
    private void OnReaderSizeChanged()
    {
        var width = _readerHost.Width;
        if (width <= 0 || Math.Abs(width - _ctx.AvailableWidth) < 1)
            return;

        _ctx.AvailableWidth = width;
        ApplyContentWidth();
    }

    private void ApplyContentWidth()
    {
        // 0 表示不限制（铺满）；否则给一个上限，让正文居中
        var width = _ctx.ContentWidth;
        _blocks.MaximumWidthRequest = width > 40 ? width : 100000;
        _editor.MaximumWidthRequest = width > 40 ? width : 100000;
    }

    /// <summary>字号 / 行高 / 列宽变化后需要重新测量所有块。</summary>
    private void RebuildBlocks()
    {
        foreach (var weak in _hosts.ToList())
        {
            if (weak.TryGetTarget(out var host))
                host.Rebuild();
            else
                _hosts.Remove(weak);
        }

        // 尺寸变化会让 CollectionView 的缓存失效，重新挂一次数据源最稳妥
        if (_active?.Document is not null)
        {
            var target = _active.CenterIndex;
            _blocks.ItemsSource = null;
            _blocks.ItemsSource = _active.Document.Blocks;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () =>
            {
                try
                {
                    _blocks.ScrollTo(Math.Max(0, target), position: ScrollToPosition.Start, animate: false);
                }
                catch
                {
                    // 忽略
                }
            });
        }
    }

    // =======================================================================
    // 工具栏
    // =======================================================================

    private void BuildToolbar()
    {
        _toolbarHost.Clear();

        var sidebarToggle = UiKit.GlyphButton("☰", "显示 / 隐藏侧栏 (Ctrl+B)", _ctx, () =>
        {
            var window = _settings.Current.Window;
            window.SidebarVisible = !window.SidebarVisible;
            _settings.Touch();
            UpdateChrome();
            return Task.CompletedTask;
        });

        var titleStack = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(2, 0),
        };
        titleStack.Add(new Label
        {
            Text = _active?.Title ?? "MD",
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CHeading,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        });
        titleStack.Add(new Label
        {
            Text = BuildSubtitle(),
            FontSize = 10.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        });

        var actions = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

        actions.Add(UiKit.FlatButton("打开", _ctx, PickFileAsync, 12.5, primary: true, horizontalPadding: 14));

        // 注：这里刻意用文字而不是 📁 / ✎ 之类的图形符号 ——
        // 工具栏字体是 Segoe UI，不含 emoji 字形，指定字体族后会渲染成方框。
        actions.Add(UiKit.FlatButton("文件夹", _ctx, PickFolderAsync, 12.5, horizontalPadding: 11));

        var editButton = UiKit.FlatButton(
            _active is { IsEditing: true } ? "完成编辑" : "编辑",
            _ctx,
            () =>
            {
                _ = SetEditModeAsync(_active is not { IsEditing: true });
                return Task.CompletedTask;
            },
            12.5,
            horizontalPadding: 11);
        actions.Add(editButton);

        actions.Add(UiKit.GlyphButton("A−", "减小字号 (Ctrl+-)", _ctx, () =>
        {
            ChangeFontSize(-0.5);
            return Task.CompletedTask;
        }, 30, 12.5));

        actions.Add(UiKit.GlyphButton("A＋", "增大字号 (Ctrl+=)", _ctx, () =>
        {
            ChangeFontSize(0.5);
            return Task.CompletedTask;
        }, 30, 12.5));

        actions.Add(UiKit.GlyphButton("◐", "切换亮色 / 暗色主题", _ctx, () =>
        {
            ToggleLightDark();
            return Task.CompletedTask;
        }, 30, 14));

        actions.Add(UiKit.GlyphButton("⟳", "重新加载 (F5)", _ctx, ReloadAsync, 30, 14));

        actions.Add(UiKit.GlyphButton("⚙", "设置", _ctx, () =>
        {
            _settingsPanel.Toggle();
            return Task.CompletedTask;
        }, 30, 14));

        _toolbarHost.Add(sidebarToggle, 0, 0);
        _toolbarHost.Add(titleStack, 1, 0);
        _toolbarHost.Add(actions, 2, 0);
    }

    private string BuildSubtitle()
    {
        if (_isLoading)
            return "正在解析…";

        if (_active is null)
            return "Ctrl+O 打开文件 · Ctrl+Shift+O 打开文件夹 · 支持拖拽";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_active.Path))
            parts.Add(Path.GetDirectoryName(_active.Path) ?? string.Empty);
        parts.Add(_active.EncodingDisplay);
        if (!string.IsNullOrEmpty(_loadSummary))
            parts.Add(_loadSummary);
        if (_active.IsModified)
            parts.Add("未保存");

        return string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private void ChangeFontSize(double delta)
    {
        var reading = _settings.Current.Reading;
        var next = Math.Clamp(reading.FontSizeDelta + delta, -5, 12);
        if (Math.Abs(next - reading.FontSizeDelta) < 0.001)
            return;

        reading.FontSizeDelta = next;
        _settings.Touch();
        ApplyThemeInternal(rebuildBlocks: true);
        _toast.Show(_ctx, $"字号 {_ctx.BodySize:0.#} pt");
    }

    private void ToggleLightDark()
    {
        var appearance = _settings.Current.Appearance;
        var current = _themes.Resolve(_settings.Current, IsSystemDark());
        bool wantDark = !current.IsDark;

        if (wantDark)
        {
            appearance.ThemeId = appearance.DarkThemeId;
        }
        else
        {
            appearance.ThemeId = _themes.LightThemes.Count > 0
                ? (string.IsNullOrWhiteSpace(_lastLightThemeId) ? _themes.LightThemes[0].Id : _lastLightThemeId)
                : appearance.ThemeId;
        }

        // 手动切换后不再跟随系统，否则按下按钮可能“看起来没反应”
        appearance.FollowSystemTheme = false;

        _settings.Touch();
        ApplyThemeInternal(rebuildBlocks: true);
        _toast.Show(_ctx, $"已切换到「{_themes.Resolve(_settings.Current, IsSystemDark()).Name}」");
    }

    private void OnLayoutSettingsChanged()
    {
        ApplyThemeInternal(rebuildBlocks: true);
    }

    private void UpdateChrome()
    {
        bool focus = _settings.Current.Reading.FocusMode;

        _toolbar.IsVisible = !focus;
        _statusBar.IsVisible = !focus && _settings.Current.Reading.StatusBarVisible;
        _sidebar.IsVisible = !focus && _settings.Current.Window.SidebarVisible;
        _focusExit.IsVisible = focus;
        // 只有一个标签时不占用垂直空间；编辑模式下也保留（方便切换文档）
        _tabs.IsVisible = _tabsCountVisible;

        if (!focus)
            _sidebar.SetWidth(_settings.Current.Window.SidebarWidth);

        _progress.IsVisible = _active?.Document is not null && !_editor.IsVisible;
        UpdateTitle();
    }

    private bool _tabsCountVisible => _openTabs.Count > 1 && !_settings.Current.Reading.FocusMode;

    private void UpdateTitle()
    {
        var name = _active?.Title ?? "MD";
        Title = _active is null ? "MD" : $"{name}{(_active.IsModified ? " *" : string.Empty)} — MD";
    }

    private void RefreshTabStrip()
    {
        _tabs.SetTabs(_openTabs.Select(t => new TabChip(
            t.Id,
            t.ShortTitle,
            IsModified: t.IsModified,
            IsActive: ReferenceEquals(t, _active),
            Tooltip: t.Tooltip)).ToList());

        _tabs.IsVisible = _tabsCountVisible;
        if (_tabs.IsVisible)
            _tabs.EnsureActiveVisible();
    }

    // =======================================================================
    // 文件
    // =======================================================================

    private async Task PickFileAsync()
    {
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".md", ".markdown", ".mdown", ".mkd", ".mdx", ".txt" } },
                { DevicePlatform.Android, new[] { "text/markdown", "text/plain", "text/*" } },
                { DevicePlatform.MacCatalyst, new[] { "md", "markdown", "txt" } },
                { DevicePlatform.iOS, new[] { "public.plain-text" } },
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择 Markdown 文件",
                FileTypes = fileTypes,
            });

            if (result is null)
                return;

            await OpenFileAsync(result.FullPath);
        }
        catch (Exception ex)
        {
            _toast.Show(_ctx, $"打开失败：{ex.Message}");
        }
    }

    // =======================================================================
    // 文件夹
    // =======================================================================

    private async Task PickFolderAsync()
    {
        if (!ShellService.IsSupported)
        {
            _toast.Show(_ctx, "当前平台暂不支持选择文件夹");
            return;
        }

        var folder = await FolderPicker.PickAsync(_settings.Current.Workspace.FolderPath);
        if (string.IsNullOrEmpty(folder))
            return;

        await OpenFolderAsync(folder);
    }

    public async Task OpenFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _toast.Show(_ctx, $"文件夹不存在：{folder}");
            return;
        }

        var workspace = _settings.Current.Workspace;
        workspace.FolderPath = folder;
        _settings.Touch();

        var result = await _folders.ScanAsync(folder, workspace.FolderRecursive);
        if (!result.Success || result.Root is null)
        {
            _toast.Show(_ctx, result.Error ?? "文件夹扫描失败");
            return;
        }

        _folderRoot = result.Root;
        _sidebar.SetFolder(_folderRoot);
        _sidebar.SetFolderCaption(folder);
        _sidebar.SetTab(1);
        workspace.SidebarTab = "folder";
        _settings.Touch();
        _sidebar.IsVisible = true;
        _ctx.BaseDirectory = folder;

        if (!_settings.Current.Window.SidebarVisible)
        {
            _settings.Current.Window.SidebarVisible = true;
            _settings.Touch();
        }

        RefreshFolderState();
        UpdateChrome();

        var name = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(name)) name = folder;
        var suffix = result.Truncated ? $"，已截断（仅收录前 {result.FileCount} 个）" : string.Empty;
        _toast.Show(_ctx, $"已打开文件夹：{name} · {result.FileCount} 个 Markdown 文件{suffix}");
        DiagnosticsLog.Write($"打开文件夹: {folder} 文件数={result.FileCount} 截断={result.Truncated}");
    }

    private void RefreshFolderState()
    {
        if (_folderRoot is null)
            return;

        var open = _openTabs
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .Select(t => t.Path)
            .ToArray();

        _sidebar.RefreshFolder(_active?.Path, open);
    }

    private async Task OpenSampleAsync()
    {
        var result = await _docSvc.LoadTextAsync(SampleDocument.Text, SampleDocument.FileName);
        if (!result.Success || result.Document is null)
        {
            _toast.Show(_ctx, result.Error ?? "示例文档加载失败");
            return;
        }

        var tab = new DocumentTab
        {
            Path = string.Empty,
            Title = "示例文档",
            Document = result.Document,
            Text = result.Text,
            Format = result.Format,
            ByteLength = result.ByteLength,
            LoadSummary = "内置示例",
        };

        _openTabs.Add(tab);
        ActivateTab(tab, restoreScroll: false);
        _toast.Show(_ctx, "已打开示例文档");
    }

    /// <summary>打开文件；若已在标签页中打开则直接切换过去。</summary>
    public async Task OpenFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        // 传进来的是文件夹时按文件夹处理（右键「用 MD 打开文件夹」会走到这里）
        if (Directory.Exists(path))
        {
            await OpenFolderAsync(path);
            return;
        }

        var existing = _openTabs.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.Path) && string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActivateTab(existing);
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        // 先占位一个标签，立刻给出反馈（大文件解析要时间）
        var tab = new DocumentTab
        {
            Path = path,
            Title = Path.GetFileName(path),
        };
        _openTabs.Add(tab);
        ActivateTab(tab, restoreScroll: true, deferLoad: true);

        _isLoading = true;
        BuildToolbar();

        var result = await _docSvc.LoadFileAsync(path, token);
        _isLoading = false;

        if (token.IsCancellationRequested)
            return;

        if (!result.Success || result.Document is null)
        {
            DiagnosticsLog.Write($"加载失败: {path} -> {result.Error}");
            _openTabs.Remove(tab);
            if (ReferenceEquals(_active, tab))
            {
                _active = _openTabs.LastOrDefault();
                if (_active is not null)
                    ActivateTab(_active, restoreScroll: true);
            }
            BuildToolbar();
            RefreshTabStrip();
            _toast.Show(_ctx, result.Error ?? "加载失败");
            return;
        }

        tab.Document = result.Document;
        tab.Text = result.Text;
        tab.Format = result.Format;
        tab.ByteLength = result.ByteLength;
        tab.LoadSummary = result.FromCache
            ? "缓存"
            : $"{result.ByteLength / 1024.0:0.#} KB · {result.Document.ParseTime.TotalMilliseconds:0} ms";

        if (ReferenceEquals(_active, tab))
            ApplyDocumentToView(tab, restoreScroll: true);

        _settings.RememberFile(path);
        _sidebar.RefreshRecent();
        RefreshTabStrip();
        RefreshFolderState();
    }

    /// <summary>切换到指定标签（必要时惰性加载内容）。</summary>
    private void ActivateTab(DocumentTab tab, bool restoreScroll = true, bool deferLoad = false)
    {
        // 先把当前标签的状态存下来
        PersistActiveTabState();

        _active = tab;
        _loadSummary = tab.LoadSummary;

        _sidebar.SetDocument(tab.Document);

        if (!deferLoad && tab.Document is null && !string.IsNullOrEmpty(tab.Path))
        {
            // 惰性加载：上次会话留下的标签，切换到时才真正读取
            _ = LoadTabAsync(tab, restoreScroll);
            return;
        }

        ApplyDocumentToView(tab, restoreScroll);
    }

    private async Task LoadTabAsync(DocumentTab tab, bool restoreScroll)
    {
        var result = await _docSvc.LoadFileAsync(tab.Path);
        if (!result.Success || result.Document is null)
        {
            _toast.Show(_ctx, result.Error ?? "加载失败");
            return;
        }

        tab.Document = result.Document;
        tab.Text = result.Text;
        tab.Format = result.Format;
        tab.ByteLength = result.ByteLength;
        tab.LoadSummary = result.FromCache
            ? "缓存"
            : $"{result.ByteLength / 1024.0:0.#} KB · {result.Document.ParseTime.TotalMilliseconds:0} ms";

        if (ReferenceEquals(_active, tab))
        {
            _loadSummary = tab.LoadSummary;
            ApplyDocumentToView(tab, restoreScroll);
        }

        RefreshTabStrip();
    }

    private void ActivateTabById(string id)
    {
        var tab = FindTab(id);
        if (tab is not null && !ReferenceEquals(tab, _active))
            ActivateTab(tab);
    }

    private DocumentTab? FindTab(string id) =>
        _openTabs.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>把某个标签的内容挂到阅读区。</summary>
    private void ApplyDocumentToView(DocumentTab tab, bool restoreScroll)
    {
        if (tab.IsEditing)
        {
            _editor.Load(tab);
            _blocks.IsVisible = false;
            _welcome.IsVisible = false;
            _editor.IsVisible = true;
            _progress.IsVisible = false;
        }
        else
        {
            _editor.IsVisible = false;
            _welcome.IsVisible = tab.Document is null;
            _blocks.IsVisible = tab.Document is not null;
        }

        _loadSummary = tab.LoadSummary;
        _ctx.BaseDirectory = string.IsNullOrEmpty(tab.Path)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(tab.Path) ?? Directory.GetCurrentDirectory();

        if (tab.Document is not null)
        {
            _hosts.Clear();
            _blocks.ItemsSource = tab.Document.Blocks;
            _sidebar.SetDocument(tab.Document);
            _progress.IsVisible = !tab.IsEditing;
        }

        var progress = restoreScroll ? ScrollOf(tab) : 0;
        _progressValue = progress;
        _progress.Progress = progress;
        tab.CenterIndex = 0;

        // 立刻滚动一次，等布局稳定后再对齐一次（否则虚拟化列表会停在顶部）
        if (tab.Document is not null)
        {
            var target = (int)Math.Round(progress * Math.Max(0, tab.Document.Blocks.Count - 1));
            _restorePending = progress > 0.001;
            ScrollToBlock(target, animate: false);
            if (_restorePending)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(160), () =>
                {
                    if (!_restorePending)
                        return;
                    _restorePending = false;
                    ScrollToBlock(target, animate: false);
                });
            }
        }

        BuildToolbar();
        RefreshTabStrip();
        UpdateChrome();
        UpdateStatusBar();
        RefreshFolderState();

        if (tab.Document is not null)
        {
            // 这条日志是「真的渲染出来了」的硬证据（不是「窗口出来了」）
            DiagnosticsLog.Write(
                $"已渲染: {tab.Path} 块数={tab.Document.Blocks.Count} 大纲={tab.Document.Outline.Count} " +
                $"字数={tab.Document.WordCount} 编码={tab.EncodingDisplay} 换行={tab.NewLineName} " +
                $"解析={tab.Document.ParseTime.TotalMilliseconds:0}ms 正文宽={_ctx.ContentWidth:0}");
        }

        _settings.Current.Workspace.ActiveTab = string.IsNullOrEmpty(tab.Path) ? null : tab.Path;
        _settings.Touch();
    }

    /// <summary>取该标签应恢复的滚动进度（磁盘文件优先用配置里记录的）。</summary>
    private double ScrollOf(DocumentTab tab)
    {
        if (!string.IsNullOrEmpty(tab.Path))
        {
            var saved = _settings.GetScroll(tab.Path);
            if (saved > 0.001)
                return saved;
        }
        return tab.Progress;
    }

    /// <summary>把当前标签的滚动位置 / 编辑内容存回标签与配置。</summary>
    private void PersistActiveTabState()
    {
        var tab = _active;
        if (tab is null)
            return;

        tab.CenterIndex = _activeCenterIndex;
        tab.Progress = _progressValue;
        if (tab.IsEditing)
            _editor.SyncToTab();

        if (!string.IsNullOrEmpty(tab.Path) && _progressValue > 0.001)
            _settings.RememberScroll(tab.Path, _progressValue);
    }

    private async Task ReloadAsync()
    {
        var tab = _active;
        if (tab is null || string.IsNullOrEmpty(tab.Path))
        {
            if (_active is null)
                await PickFileAsync();
            return;
        }

        if (tab.IsModified)
        {
            bool discard = await DisplayAlertAsync("重新加载", "当前文档有未保存的修改，重新加载会丢弃这些修改。", "丢弃并重新加载", "取消");
            if (!discard)
                return;
        }

        _docSvc.Invalidate(tab.Path);
        tab.Document = null;
        tab.IsModified = false;
        await LoadTabAsync(tab, restoreScroll: false);
        _toast.Show(_ctx, "已重新加载");
    }

    private void ScrollToBlock(int index, bool animate = true)
    {
        if (_active?.Document is null || index < 0 || index >= _active.Document.Blocks.Count)
            return;

        try
        {
            _blocks.ScrollTo(index, position: ScrollToPosition.Start, animate: animate);
        }
        catch
        {
            // 列表尚未完成布局时可能抛异常
        }
    }

    // =======================================================================
    // 标签页管理
    // =======================================================================

    private async Task CloseTabAsync(DocumentTab? tab)
    {
        if (tab is null)
            return;

        if (tab.IsEditing)
        {
            _editor.SyncToTab();
            tab.IsEditing = false;
        }

        if (tab.IsModified)
        {
            bool discard = await DisplayAlertAsync(
                "关闭标签",
                $"「{tab.Title}」有未保存的修改，确定关闭吗？",
                "丢弃修改并关闭",
                "取消");
            if (!discard)
                return;
        }

        int index = _openTabs.IndexOf(tab);
        if (index < 0)
            return;

        _openTabs.RemoveAt(index);

        if (ReferenceEquals(_active, tab))
        {
            _active = null;
            var next = _openTabs.Count == 0 ? null : _openTabs[Math.Min(index, _openTabs.Count - 1)];
            if (next is not null)
            {
                ActivateTab(next);
            }
            else
            {
                _blocks.ItemsSource = null;
                _hosts.Clear();
                _editor.IsVisible = false;
                _welcome.IsVisible = true;
                _blocks.IsVisible = false;
                _progress.IsVisible = false;
                _sidebar.SetDocument(null);
                _loadSummary = string.Empty;
                _settings.Current.Workspace.ActiveTab = null;
                _settings.Touch();
                BuildToolbar();
                RefreshTabStrip();
                UpdateChrome();
                UpdateStatusBar();
            }
        }
        else
        {
            RefreshTabStrip();
        }

        PersistOpenTabs();
        RefreshFolderState();
    }

    private void PersistOpenTabs()
    {
        var workspace = _settings.Current.Workspace;
        workspace.OpenTabs = _openTabs
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .Select(t => t.Path)
            .ToList();
        workspace.ActiveTab = string.IsNullOrEmpty(_active?.Path) ? null : _active!.Path;
        workspace.SidebarTab = _sidebar.TabName;
        _settings.Touch();
    }

    private void CycleTab(int step)
    {
        if (_openTabs.Count < 2)
            return;

        int index = _active is null ? -1 : _openTabs.IndexOf(_active);
        int next = ((index + step) % _openTabs.Count + _openTabs.Count) % _openTabs.Count;
        ActivateTab(_openTabs[next]);
    }

    // =======================================================================
    // 编辑
    // =======================================================================

    private async Task SetEditModeAsync(bool editing)
    {
        var tab = _active;
        if (tab is null)
        {
            _toast.Show(_ctx, "先打开一个文档再编辑");
            return;
        }

        if (tab.IsEditing == editing)
            return;

        if (editing)
        {
            if (tab.Document is null && !string.IsNullOrEmpty(tab.Path))
                await LoadTabAsync(tab, restoreScroll: false);

            tab.IsEditing = true;
            _editor.Load(tab);
            _editor.IsVisible = true;
            _blocks.IsVisible = false;
            _welcome.IsVisible = false;
            _progress.IsVisible = false;
            _editor.FocusEditor();
            RefreshTabStrip();
            UpdateChrome();
            UpdateStatusBar();
            _toast.Show(_ctx, "编辑模式 · Ctrl+S 保存 · Ctrl+E 退出");
            return;
        }

        _editor.SyncToTab();
        tab.IsEditing = false;
        _editor.IsVisible = false;

        // 退出编辑后立刻重排：让阅读视图反映刚才的改动
        if (tab.Document is not null || !string.IsNullOrEmpty(tab.Text))
            await ReparseAsync(tab);
        else
        {
            _blocks.IsVisible = tab.Document is not null;
            _welcome.IsVisible = tab.Document is null;
        }

        RefreshTabStrip();
        UpdateChrome();
        UpdateStatusBar();
    }

    private async Task ReparseAsync(DocumentTab tab)
    {
        var path = string.IsNullOrEmpty(tab.Path) ? "示例.md" : tab.Path;
        try
        {
            tab.Document = await _docSvc.ParseAsync(tab.Text, path);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"重新解析失败: {path}", ex);
            return;
        }

        if (!ReferenceEquals(_active, tab))
            return;

        _hosts.Clear();
        _blocks.ItemsSource = tab.Document.Blocks;
        _sidebar.SetDocument(tab.Document);
        _blocks.IsVisible = true;
        _welcome.IsVisible = false;
        _progress.IsVisible = true;
        UpdateStatusBar();
    }

    /// <summary>保存当前标签。</summary>
    private async Task SaveActiveAsync()
    {
        var tab = _active;
        if (tab is null)
            return;

        if (tab.IsEditing)
            _editor.SyncToTab();

        if (string.IsNullOrEmpty(tab.Path))
        {
            // 没有落盘文件（示例文档）：走「另存为」，新文件统一用 UTF-8 无 BOM + 系统换行
            var target = await FileSaver.PickAsync("示例.md", "Markdown 文件", ".md");
            if (string.IsNullOrEmpty(target))
                return;

            tab.Path = target;
            tab.Title = Path.GetFileName(target);
            tab.Format = TextFileFormat.DefaultUtf8;
        }

        var result = await _docSvc.SaveAsync(tab.Path, tab.Text, tab.Format);
        if (!result.Success)
        {
            _toast.Show(_ctx, $"保存失败：{result.Error}");
            return;
        }

        tab.IsModified = false;
        tab.LoadSummary = $"已保存 · {tab.EncodingDisplay} · {tab.NewLineName}";
        _loadSummary = tab.LoadSummary;

        _settings.RememberFile(tab.Path);
        _sidebar.RefreshRecent();

        // 保存后重新解析，保证阅读视图与磁盘一致
        await ReparseAsync(tab);

        RefreshTabStrip();
        RefreshFolderState();
        BuildToolbar();
        UpdateTitle();
        UpdateStatusBar();

        if (result.Lossy)
            _toast.Show(_ctx, $"已保存，但有字符无法用「{tab.EncodingDisplay}」表示已被替换，建议另存为 UTF-8");
        else if (result.Skipped)
            _toast.Show(_ctx, "内容没有变化，未写入文件");
        else
            _toast.Show(_ctx, $"已保存 · {tab.EncodingDisplay} · {tab.NewLineName}");
    }

    // =======================================================================
    // 滚动 / 状态
    // =======================================================================

    private int _activeCenterIndex;

    private void OnScrolled(ItemsViewScrolledEventArgs e)
    {
        if (_active?.Document is null)
            return;

        _activeCenterIndex = e.CenterItemIndex >= 0 ? e.CenterItemIndex : Math.Max(0, e.FirstVisibleItemIndex);
        _active.CenterIndex = _activeCenterIndex;
        _restorePending = false;

        int total = _active.Document.Blocks.Count;
        _progressValue = total <= 1 ? 0 : Math.Clamp(_activeCenterIndex / (double)(total - 1), 0, 1);
        _active.Progress = _progressValue;
        _progress.Progress = _progressValue;

        if (_settings.Current.Reading.SyncOutline)
            _sidebar.SetActiveBlock(Math.Max(0, e.FirstVisibleItemIndex));

        UpdateStatusBar();

        if (!string.IsNullOrEmpty(_active.Path))
            _settings.RememberScroll(_active.Path, _progressValue);

        // 滚动位置留痕（限流）：排查「进度条 / 大纲不跟着走」这类问题时看 md.log
        var nowTick = Environment.TickCount64;
        if (nowTick - _lastScrollLogTick > 1000)
        {
            _lastScrollLogTick = nowTick;
            DiagnosticsLog.Write($"[滚动] 块 {_activeCenterIndex}/{total - 1} 进度 {_progressValue:P0}");
        }
    }

    private long _lastScrollLogTick;

    private void UpdateStatusBar()
    {
        var tab = _active;
        if (tab?.Document is null)
        {
            _statusLeft.Text = tab is null ? "就绪" : $"{tab.EncodingDisplay} · {tab.NewLineName}";
            _statusCenter.Text = string.Empty;
            _statusRight.Text = "MD · .NET 10 + MAUI";
            return;
        }

        var left = $"{tab.EncodingDisplay}  ·  {tab.NewLineName}  ·  {tab.Document.WordCount} 字  ·  {tab.Document.SourceLineCount} 行";
        if (tab.IsModified)
            left += "  ·  ● 未保存";
        if (_openTabs.Count > 1)
            left += $"  ·  {_openTabs.Count} 个标签";
        _statusLeft.Text = left;

        _statusCenter.Text = CurrentHeading();

        var themeName = _ctx.Theme.Name;
        _statusRight.Text = $"{_progressValue * 100:0.0}%  ·  {themeName}";
    }

    private string CurrentHeading()
    {
        if (_active?.Document is null || _active.Document.Outline.Count == 0)
            return string.Empty;

        string text = string.Empty;
        foreach (var node in _active.Document.Outline)
        {
            if (node.BlockIndex <= _activeCenterIndex)
                text = node.Text;
            else
                break;
        }
        return text;
    }

    // =======================================================================
    // 链接
    // =======================================================================

    private async Task HandleLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        // 文档内锚点
        if (url.StartsWith('#'))
        {
            var anchor = Uri.UnescapeDataString(url[1..]);
            if (_active?.Document is not null && _active.Document.AnchorIndex.TryGetValue(anchor, out var index))
                ScrollToBlock(index);
            else
                _toast.Show(_ctx, $"未找到锚点：{anchor}");
            return;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            await PlatformLauncher.OpenUrlAsync(url);
            return;
        }

        var resolved = PlatformLauncher.ResolveLink(url, _ctx.BaseDirectory);
        var extension = Path.GetExtension(resolved);

        if (File.Exists(resolved) && IsMarkdown(extension))
        {
            await OpenFileAsync(resolved);
            return;
        }

        if (File.Exists(resolved))
        {
            PlatformLauncher.RevealFile(resolved);
            return;
        }

        _toast.Show(_ctx, $"链接目标不存在：{resolved}");
    }

    private static bool IsMarkdown(string extension) => extension.ToLowerInvariant() switch
    {
        ".md" or ".markdown" or ".mdown" or ".mkd" or ".mdx" or ".txt" => true,
        _ => false,
    };

    // =======================================================================
    // 拖拽
    // =======================================================================

    private void HookDropTarget()
    {
#if WINDOWS
        Platforms.Windows.WindowsDropTarget.Enable(
            this,
            async path =>
            {
                if (Directory.Exists(path))
                {
                    await OpenFolderAsync(path);
                }
                else if (IsMarkdown(Path.GetExtension(path)))
                {
                    await OpenFileAsync(path);
                }
                else
                {
                    _toast.Show(_ctx, $"不支持的文件类型：{Path.GetFileName(path)}");
                }
            },
            active =>
            {
                _dragActive = active;
                _toolbarHairline.Color = active ? _ctx.CAccent : _ctx.CToolbarBorder;
                _toolbarHairline.HeightRequest = active ? 2 : 1;
            });
#endif
    }

    // =======================================================================
    // 快捷键 / 菜单（桌面端）
    // =======================================================================

    /// <summary>
    /// 挂上原生菜单栏与快捷键。菜单栏挂在 Page 上（ContentPage.MenuBarItems），
    /// 其他平台没有菜单栏概念，会被平台忽略，因此这里用条件编译。
    /// </summary>
    private void AttachMenu()
    {
#if WINDOWS || MACCATALYST
        var fileMenu = new MenuBarItem { Text = "文件" };
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "打开文件…" }, "O", KeyboardAcceleratorModifiers.Ctrl, () => _ = PickFileAsync()));
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "打开文件夹…" }, "O", KeyboardAcceleratorModifiers.Ctrl | KeyboardAcceleratorModifiers.Shift, () => _ = PickFolderAsync()));
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "打开示例文档" }, "D", KeyboardAcceleratorModifiers.Ctrl, () => _ = OpenSampleAsync()));
        fileMenu.Add(new MenuFlyoutSeparator());
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "保存" }, "S", KeyboardAcceleratorModifiers.Ctrl, () => _ = SaveActiveAsync()));
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "关闭标签" }, "W", KeyboardAcceleratorModifiers.Ctrl, () => _ = CloseTabAsync(_active)));
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "重新加载" }, "F5", KeyboardAcceleratorModifiers.None, () => _ = ReloadAsync()));
        fileMenu.Add(new MenuFlyoutSeparator());
        // Ctrl+E 已被「编辑 / 退出编辑」占用，这里必须错开，否则两个菜单项抢同一个加速键
        fileMenu.Add(Accelerator(new MenuFlyoutItem { Text = "在文件管理器中显示" }, "E", KeyboardAcceleratorModifiers.Ctrl | KeyboardAcceleratorModifiers.Shift,
            () => PlatformLauncher.RevealFile(_active?.Path ?? string.Empty)));
        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "用系统「打开方式」打开…",
            Command = new Command(() => ShellService.OpenWithDialog(_active?.Path)),
        });
        fileMenu.Add(new MenuFlyoutSeparator());
        fileMenu.Add(new MenuFlyoutItem
        {
            Text = "退出",
            Command = new Command(() => Application.Current?.Quit()),
        });

        var viewMenu = new MenuBarItem { Text = "视图" };
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "编辑 / 退出编辑" }, "E", KeyboardAcceleratorModifiers.Ctrl, () => _ = SetEditModeAsync(_active is not { IsEditing: true })));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "显示 / 隐藏侧栏" }, "B", KeyboardAcceleratorModifiers.Ctrl, () =>
        {
            var windowSettings = _settings.Current.Window;
            windowSettings.SidebarVisible = !windowSettings.SidebarVisible;
            _settings.Touch();
            UpdateChrome();
        }));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "专注模式" }, "F", KeyboardAcceleratorModifiers.Ctrl, () =>
        {
            _settings.Current.Reading.FocusMode = !_settings.Current.Reading.FocusMode;
            _settings.Touch();
            UpdateChrome();
        }));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "切换亮色 / 暗色" }, "T", KeyboardAcceleratorModifiers.Ctrl, ToggleLightDark));
        viewMenu.Add(new MenuFlyoutSeparator());
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "增大字号" }, "OemPlus", KeyboardAcceleratorModifiers.Ctrl, () => ChangeFontSize(0.5)));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "减小字号" }, "OemMinus", KeyboardAcceleratorModifiers.Ctrl, () => ChangeFontSize(-0.5)));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "重置字号" }, "D0", KeyboardAcceleratorModifiers.Ctrl, () =>
        {
            _settings.Current.Reading.FontSizeDelta = 0;
            _settings.Touch();
            ApplyThemeInternal(rebuildBlocks: true);
        }));
        viewMenu.Add(new MenuFlyoutSeparator());
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "下一个标签" }, "Tab", KeyboardAcceleratorModifiers.Ctrl, () => CycleTab(1)));
        viewMenu.Add(Accelerator(new MenuFlyoutItem { Text = "上一个标签" }, "Tab", KeyboardAcceleratorModifiers.Ctrl | KeyboardAcceleratorModifiers.Shift, () => CycleTab(-1)));
        viewMenu.Add(new MenuFlyoutSeparator());
        viewMenu.Add(new MenuFlyoutItem { Text = "设置…", Command = new Command(() => _settingsPanel.Toggle()) });

        // ---- 工具：Windows 外壳集成（注册右键菜单 / 默认应用）+ 诊断入口 ----
        var toolsMenu = new MenuBarItem { Text = "工具" };
        toolsMenu.Add(new MenuFlyoutItem
        {
            Text = "注册到系统右键菜单…",
            Command = new Command(() => _ = RegisterShellAsync()),
        });
        toolsMenu.Add(new MenuFlyoutItem
        {
            Text = "设为默认 Markdown 阅读器…",
            Command = new Command(() => _ = SetDefaultReaderAsync()),
        });
        toolsMenu.Add(new MenuFlyoutItem
        {
            Text = "移除系统注册",
            Command = new Command(async () =>
            {
                var (ok, message) = ShellService.Unregister();
                await DisplayAlertAsync("系统集成", message, "好");
                DiagnosticsLog.Write($"菜单注销系统集成：ok={ok}");
            }),
        });
        toolsMenu.Add(new MenuFlyoutSeparator());
        toolsMenu.Add(new MenuFlyoutItem
        {
            Text = "打开设置与日志目录",
            Command = new Command(() => PlatformLauncher.OpenFolder(AppPaths.ConfigRoot)),
        });

        MenuBarItems.Add(fileMenu);
        MenuBarItems.Add(viewMenu);
        if (ShellService.IsSupported)
            MenuBarItems.Add(toolsMenu);

        // ⚠ 已知缺陷（实测，未修）：MAUI 10 的 Windows 端并不会把 Page.MenuBarItems
        //   渲染出来 —— 标题栏那一条只有窗口标题，没有「文件 / 视图 / 工具」。
        //   命令目前靠工具栏按钮、设置面板与键盘快捷键触达。
        //   先记一行日志，方便以后排查 / 回归时确认。
        DiagnosticsLog.Write($"菜单已挂载 {MenuBarItems.Count} 项（Windows 端目前不渲染，见 docs/PLATFORMS.md）");
#endif
    }

    /// <summary>菜单入口：注册右键菜单，并把结果如实告诉用户。</summary>
    private async Task RegisterShellAsync()
    {
        var (ok, message) = ShellService.Register();
        DiagnosticsLog.Write($"菜单注册系统集成：ok={ok} :: {message}");
        await DisplayAlertAsync("系统集成", message, "好");
    }

    /// <summary>
    /// 菜单入口：引导用户去系统「默认应用」页面。
    /// Windows 8 起默认关联受 UserChoice 哈希保护，程序无权代改，只能引导。
    /// </summary>
    private async Task SetDefaultReaderAsync()
    {
        var confirmed = await DisplayAlertAsync(
            "设为默认阅读器",
            "Windows 不允许程序自己改默认关联（受系统保护）。\n\n" +
            "接下来会打开系统的「默认应用」相关页面，请在那里找到 MD 并设为 .md 的默认打开方式。",
            "继续", "取消");
        if (!confirmed)
            return;

        // 先把注册补全，否则「默认应用」列表里可能还看不到 MD
        if (!ShellService.IsRegistered)
            ShellService.Register();

        ShellService.OpenDefaultAppsSettings();
    }

#if WINDOWS || MACCATALYST
    private static MenuFlyoutItem Accelerator(
        MenuFlyoutItem item,
        string key,
        KeyboardAcceleratorModifiers modifiers,
        Action action)
    {
        item.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        });
        item.Clicked += (_, _) => action();
        return item;
    }
#endif

    // =======================================================================
    // 生命周期
    // =======================================================================

    /// <summary>窗口关闭前把会话状态写回配置（由 App 调用）。</summary>
    public void OnShutdown()
    {
        PersistActiveTabState();
        PersistOpenTabs();
        DiagnosticsLog.Write($"会话已保存：标签 {_openTabs.Count} 个，文件夹={_settings.Current.Workspace.FolderPath ?? "无"}");
    }

    /// <summary>窗口就绪后的初始化：命令行参数 / 上次会话 / 欢迎页。</summary>
    public async Task StartupAsync()
    {
        try
        {
            await StartupCoreAsync();
        }
        catch (Exception ex)
        {
            // 启动期异常不能静默：记录下来并把原因显示在界面上
            DiagnosticsLog.Write("StartupAsync 失败", ex);
            _welcome.IsVisible = true;
            _blocks.IsVisible = false;
            _toast.Show(_ctx, "启动失败：" + ex.Message);
        }
        finally
        {
            // 界面准备就绪后才接管第二个实例转交来的参数。
            // 在此之前到达的会由 SingleInstance 排队，注册时一次性补投，不会丢。
            // 放在 finally 里：即使启动失败，程序也已经能开文档了。
            SingleInstance.SetHandler(OnSecondInstanceArgs);
        }
    }

    private async Task StartupCoreAsync()
    {
        var args = ReadCommandLine();

        // 1) 先恢复上次会话的文件夹与标签（打开文件夹逐篇阅读的用法下，
        //    每次启动都应该回到原来的工作区，而不是只留一个孤零零的文件）
        await RestoreFolderSilentlyAsync();
        await RestoreTabsAsync();

        // 2) 命令行指定了文件夹
        if (args.Folder is not null)
        {
            DiagnosticsLog.Write($"命令行打开文件夹: {args.Folder}");
            await OpenFolderAsync(args.Folder);
            if (args.File is not null)
                await OpenFileAsync(args.File);
            return;
        }

        // 3) 命令行指定了文件（从资源管理器双击 / 右键打开走这条）
        if (args.File is not null)
        {
            DiagnosticsLog.Write($"命令行打开: {args.File}");
            await OpenFileAsync(args.File);
            return;
        }

        // 4) 会话里已经有恢复出来的标签就不用管了
        if (_openTabs.Count > 0)
            return;

        // 5) 上次打开的文件
        var last = _settings.Current.Files.LastFile;
        if (!string.IsNullOrEmpty(last) && File.Exists(last))
        {
            DiagnosticsLog.Write($"打开上次文件: {last}");
            await OpenFileAsync(last);
            return;
        }

        DiagnosticsLog.Write("无可打开内容 → 显示欢迎页");
        _welcome.IsVisible = true;
        _blocks.IsVisible = false;
        _progress.IsVisible = false;
        UpdateChrome();
        UpdateStatusBar();
    }

    /// <summary>
    /// 解析本进程的命令行。外壳集成指令（--register-shell 等）不在这里处理：
    /// 它们在 MauiProgram 里、建窗口之前就执行并退出了，所以本方法只管文件与文件夹。
    /// </summary>
    private (string? File, string? Folder) ReadCommandLine()
        => ParseCommandLine(Environment.GetCommandLineArgs(), startIndex: 1);

    /// <summary>
    /// 解析命令行参数（文件 / 文件夹）。
    /// </summary>
    /// <param name="raw">参数数组。本进程的数组第 0 项是 exe 路径，所以从 1 开始。</param>
    /// <param name="startIndex">
    /// 起始下标。单实例模式下第二个实例转交过来的是**裸参数**（没有 exe 前缀），传 0。
    /// </param>
    private static (string? File, string? Folder) ParseCommandLine(string[] raw, int startIndex)
    {
        string? file = null;
        string? folder = null;

        try
        {
            for (int i = startIndex; i < raw.Length; i++)
            {
                var candidate = raw[i];

                if (candidate.Equals("--folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < raw.Length)
                        folder = raw[++i];
                    continue;
                }

                if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith('-'))
                    continue;

                if (Directory.Exists(candidate))
                {
                    folder ??= candidate;
                    continue;
                }

                if (file is null && File.Exists(candidate) && IsMarkdown(Path.GetExtension(candidate)))
                    file = candidate;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("解析命令行参数失败", ex);
        }

        return (file, folder);
    }

    /// <summary>静默恢复上次的文件夹（失败不打扰用户）。</summary>
    private async Task RestoreFolderSilentlyAsync()
    {
        var folder = _settings.Current.Workspace.FolderPath;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;

        try
        {
            var result = await _folders.ScanAsync(folder, _settings.Current.Workspace.FolderRecursive);
            if (!result.Success || result.Root is null)
                return;

            _folderRoot = result.Root;
            _sidebar.SetFolder(_folderRoot);
            _sidebar.SetFolderCaption(folder);
            RefreshFolderState();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write("恢复文件夹失败", ex);
        }
    }

    /// <summary>恢复上次会话的标签页，返回是否恢复出了内容。</summary>
    private async Task<bool> RestoreTabsAsync()
    {
        var workspace = _settings.Current.Workspace;
        var paths = workspace.OpenTabs
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (paths.Count == 0)
            return false;

        // 只解析当前那一个，其余等切换时再读（打开 20 个标签也不会卡住启动）
        var activePath = workspace.ActiveTab;
        if (string.IsNullOrEmpty(activePath) || !paths.Contains(activePath, StringComparer.OrdinalIgnoreCase))
            activePath = paths[0];

        foreach (var path in paths)
        {
            var tab = new DocumentTab
            {
                Path = path,
                Title = Path.GetFileName(path),
            };
            _openTabs.Add(tab);
        }

        var active = _openTabs.First(t => string.Equals(t.Path, activePath, StringComparison.OrdinalIgnoreCase));
        _active = active;
        RefreshTabStrip();
        DiagnosticsLog.Write($"恢复 {_openTabs.Count} 个标签，激活 {activePath}");

        await LoadTabAsync(active, restoreScroll: true);
        return true;
    }
}
