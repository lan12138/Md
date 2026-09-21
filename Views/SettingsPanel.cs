// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/SettingsPanel.cs
//  说明：设置面板（覆盖层）。所有改动即时写入 settings.json。
//        · 主题：亮色 / 暗色各选一套，可跟随系统
//        · 排版：字号、行高、正文宽度，拖动时实时预览
//        · 阅读：代码行号、图片加载、状态栏、大纲同步、专注模式
//        · 配置：打开配置目录、恢复默认
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  「正文宽度」由滑杆改为预设（自适应/窄/标准/宽/铺满），
//                   解决最大化后正文只有窄窄一条的问题；
//                   新增「系统集成」：右键「打开方式」与设为默认 Markdown 阅读器
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Models;
using MD.Rendering;
using MD.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace MD.Views;

public sealed class SettingsPanel : Grid
{
    private RenderContext _ctx;
    private readonly Border _panel;
    private readonly VerticalStackLayout _content;
    private readonly Action _onLayoutChanged;
    private readonly Action<string> _toast;

    public event Action? CloseRequested;

    public SettingsPanel(RenderContext ctx, Action onLayoutChanged, Action<string> toast)
    {
        _ctx = ctx;
        _onLayoutChanged = onLayoutChanged;
        _toast = toast;

        BackgroundColor = ctx.CScrim;
        IsVisible = false;

        _content = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(22, 4, 22, 22) };

        var scroller = new ScrollView { Content = _content };

        _panel = new Border
        {
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children = { },
            },
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            WidthRequest = 470,
            MaximumHeightRequest = 720,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Padding = 0,
        };

        var grid = (Grid)_panel.Content;
        grid.Add(BuildHeader(), 0, 0);
        grid.Add(scroller, 0, 1);

        Add(_panel);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (CloseRequested is not null)
                CloseRequested();
        };
        GestureRecognizers.Add(tap);

        Rebuild();
        ApplyTheme(ctx);
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        BackgroundColor = ctx.CScrim;
        _panel.BackgroundColor = ctx.CPanelBg;
        _panel.Stroke = ctx.CPanelBorder;
        Rebuild();
    }

    public void Toggle()
    {
        IsVisible = !IsVisible;
        if (IsVisible)
            Rebuild();
    }

    public void Hide() => IsVisible = false;

    // -----------------------------------------------------------------------
    // 结构
    // -----------------------------------------------------------------------

    private View BuildHeader()
    {
        var title = new Label
        {
            Text = "设置",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CHeading,
            VerticalTextAlignment = TextAlignment.Center,
        };

        var close = UiKit.GlyphButton("✕", "关闭", _ctx, () =>
        {
            Hide();
            return Task.CompletedTask;
        }, 28, 13);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(20, 14, 12, 12),
        };
        header.Add(title, 0, 0);
        header.Add(close, 1, 0);
        return header;
    }

    private void Rebuild()
    {
        _content.Clear();
        var settings = AppHost.Settings.Current;

        BuildAppearance(settings);
        BuildTypography(settings);
        BuildReading(settings);
        BuildIntegration(settings);
        BuildConfig(settings);
    }

    // -----------------------------------------------------------------------
    // 系统集成
    // -----------------------------------------------------------------------

    private void BuildIntegration(AppSettings settings)
    {
        _content.Add(SectionTitle("系统集成"));

        _content.Add(new Label
        {
            Text = ShellService.IsSupported
                ? "注册后，在文件上右键 →「打开方式」里就能看到 MD；也可以把它设为 .md 的默认打开程序。"
                : "当前平台不支持注册系统文件关联。",
            FontSize = 11.5,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            LineHeight = 1.6,
            Margin = new Thickness(0, 2, 0, 8),
        });

        bool registered = ShellService.IsRegistered;

        var status = new Label
        {
            Text = registered ? "● 已注册到系统" : "○ 尚未注册",
            FontSize = 11.5,
            FontFamily = _ctx.BodyFont,
            TextColor = registered ? _ctx.CAccent : _ctx.CTextSecondary,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _content.Add(status);

        var row1 = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            AlignItems = FlexAlignItems.Start,
            Margin = new Thickness(0, 0, 0, 6),
        };

        row1.Add(BigAction(registered ? "重新注册" : "注册「打开方式」", primary: !registered, () =>
        {
            var (ok, message) = ShellService.Register();
            _toast(message);
            if (ok)
            {
                _onLayoutChanged();
                Rebuild();
            }
        }));

        row1.Add(BigAction("设为默认 .md 阅读器", primary: false, () =>
        {
            if (!ShellService.IsRegistered)
            {
                // 先注册，否则「默认应用」列表里根本没有 MD
                var (ok, message) = ShellService.Register();
                if (!ok)
                {
                    _toast(message);
                    return;
                }
            }

            ShellService.OpenDefaultAppsSettings();
            _toast("Windows 不允许程序自行改默认程序，已在系统设置里打开对应页面，请选择 MD");
        }));

        if (registered)
        {
            row1.Add(BigAction("移除注册", primary: false, () =>
            {
                var (_, message) = ShellService.Unregister();
                _toast(message);
                _onLayoutChanged();
                Rebuild();
            }));
        }

        _content.Add(row1);

        _content.Add(new Label
        {
            Text = "说明：注册只写当前用户（HKCU），不需要管理员权限；卸载时用「移除注册」即可清理干净。",
            FontSize = 11,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            LineHeight = 1.5,
            Margin = new Thickness(0, 2, 0, 0),
        });
    }

    private View BigAction(string text, bool primary, Action action)
    {
        var button = UiKit.FlatButton(text, _ctx, () =>
        {
            try { action(); } catch (Exception ex) { _toast($"操作失败：{ex.Message}"); }
            return Task.CompletedTask;
        }, 12.5, primary: primary, horizontalPadding: 13);
        button.Margin = new Thickness(0, 0, 8, 8);
        return button;
    }

    // -----------------------------------------------------------------------
    // 外观
    // -----------------------------------------------------------------------

    private void BuildAppearance(AppSettings settings)
    {
        _content.Add(SectionTitle("外观"));

        _content.Add(new MdSwitch("跟随系统明暗", _ctx, settings.Appearance.FollowSystemTheme, 13,
            "开启后按系统设置自动在「亮色主题 / 暗色主题」之间切换")
        {
            Margin = new Thickness(0, 2, 0, 8),
        }.Also(sw =>
        {
            sw.Toggled += (_, value) =>
            {
                settings.Appearance.FollowSystemTheme = value;
                AppHost.Settings.Touch();
                _onLayoutChanged();
            };
        }));

        _content.Add(ThemeChips("亮色主题", settings, isDark: false));
        _content.Add(ThemeChips("暗色主题", settings, isDark: true));
    }

    private View ThemeChips(string label, AppSettings settings, bool isDark)
    {
        var current = isDark ? settings.Appearance.DarkThemeId : settings.Appearance.ThemeId;

        var caption = new Label
        {
            Text = label,
            FontSize = 12,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            Margin = new Thickness(0, 10, 0, 6),
        };

        var wrap = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            AlignItems = FlexAlignItems.Start,
        };

        foreach (var theme in isDark ? AppHost.Themes.DarkThemes : AppHost.Themes.LightThemes)
        {
            bool selected = string.Equals(theme.Id, current, StringComparison.OrdinalIgnoreCase);

            var swatchRow = new HorizontalStackLayout { Spacing = 5 };
            swatchRow.Add(Swatch(theme.Colors.WindowBg));
            swatchRow.Add(Swatch(theme.Colors.Text));
            swatchRow.Add(Swatch(theme.Colors.Accent));

            var texts = new VerticalStackLayout { Spacing = 0 };
            texts.Add(swatchRow);
            texts.Add(new Label
            {
                Text = theme.Name,
                FontSize = 11,
                FontFamily = _ctx.BodyFont,
                TextColor = selected ? _ctx.CAccent : _ctx.CText,
            });

            var chip = new Border
            {
                Content = texts,
                BackgroundColor = selected ? _ctx.CSidebarHoverBg : _ctx.CControlBg,
                Stroke = selected ? _ctx.CAccent : _ctx.CControlBorder,
                StrokeThickness = selected ? 1.5 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                Padding = new Thickness(10, 7),
                Margin = new Thickness(0, 0, 8, 8),
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (isDark)
                    settings.Appearance.DarkThemeId = theme.Id;
                else
                    settings.Appearance.ThemeId = theme.Id;

                if (!settings.Appearance.FollowSystemTheme)
                    settings.Appearance.ThemeId = theme.Id;

                AppHost.Settings.Touch();
                _onLayoutChanged();
                Rebuild();
            };
            chip.GestureRecognizers.Add(tap);

            wrap.Add(chip);
        }

        return new VerticalStackLayout { Spacing = 0, Children = { caption, wrap } };
    }

    private static View Swatch(string hex) => new Border
    {
        Content = new BoxView { Color = Colors.Transparent },
        BackgroundColor = Palette.Get(hex),
        Stroke = Colors.Gray.WithAlpha(0.35f),
        StrokeThickness = 0.5,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) },
        WidthRequest = 14,
        HeightRequest = 14,
        Padding = 0,
    };

    // -----------------------------------------------------------------------
    // 排版
    // -----------------------------------------------------------------------

    private void BuildTypography(AppSettings settings)
    {
        _content.Add(SectionTitle("排版"));

        _content.Add(SliderRow(
            "正文字号",
            $"{_ctx.BodySize:0.#} pt",
            current: settings.Reading.FontSizeDelta,
            min: -5, max: 12, step: 0.5,
            onValue: (value, label) =>
            {
                settings.Reading.FontSizeDelta = value;
                AppHost.Settings.Touch();
                label.Text = $"{_ctx.Type.BaseFontSize + value:0.#} pt";
            }));

        _content.Add(SliderRow(
            "行高",
            $"{_ctx.LineHeight:0.##}",
            current: settings.Reading.LineHeightDelta,
            min: -0.6, max: 0.9, step: 0.05,
            onValue: (value, label) =>
            {
                settings.Reading.LineHeightDelta = value;
                AppHost.Settings.Touch();
                label.Text = $"{_ctx.Type.LineHeight + value:0.##}";
            }));

        _content.Add(WidthModeChips(settings));
    }

    /// <summary>
    /// 正文列宽预设。以前是一个 0~1400 的滑杆，默认「跟随主题」= 固定 820pt，
    /// 于是在 2560 宽的屏幕上最大化之后正文只占三分之一，两侧全是空白。
    /// 改成预设 + 自适应默认值，宽屏下自动放宽。
    /// </summary>
    private View WidthModeChips(AppSettings settings)
    {
        var caption = new Label
        {
            Text = "正文宽度",
            FontSize = 13,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CText,
            Margin = new Thickness(0, 6, 0, 6),
        };

        var hint = new Label
        {
            Text = "「自适应」会跟随主题的舒适宽度，并在宽窗口下自动放宽（上限 1.55 倍）",
            FontSize = 11,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            Margin = new Thickness(0, 0, 0, 8),
            LineHeight = 1.5,
        };

        var wrap = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            AlignItems = FlexAlignItems.Start,
        };

        (string Id, string Name)[] modes =
        {
            ("auto", "自适应"),
            ("narrow", "窄 700"),
            ("standard", "标准 900"),
            ("wide", "宽 1150"),
            ("full", "铺满"),
        };

        foreach (var (id, name) in modes)
        {
            bool selected = string.Equals(settings.Reading.ContentWidthMode, id, StringComparison.OrdinalIgnoreCase);

            var chip = new Border
            {
                Content = new Label
                {
                    Text = name,
                    FontSize = 12,
                    FontFamily = _ctx.BodyFont,
                    TextColor = selected ? _ctx.CAccent : _ctx.CText,
                },
                BackgroundColor = selected ? _ctx.CSidebarActiveBg : _ctx.CControlBg,
                Stroke = selected ? _ctx.CAccent : _ctx.CControlBorder,
                StrokeThickness = selected ? 1.5 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                Padding = new Thickness(11, 6),
                Margin = new Thickness(0, 0, 8, 8),
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                settings.Reading.ContentWidthMode = id;
                AppHost.Settings.Touch();
                _onLayoutChanged();
                Rebuild();
            };
            chip.GestureRecognizers.Add(tap);

            wrap.Add(chip);
        }

        return new VerticalStackLayout
        {
            Spacing = 0,
            Margin = new Thickness(0, 4, 0, 6),
            Children = { caption, hint, wrap },
        };
    }

    // -----------------------------------------------------------------------
    // 阅读
    // -----------------------------------------------------------------------

    private void BuildReading(AppSettings settings)
    {
        _content.Add(SectionTitle("阅读"));

        _content.Add(new MdSwitch("代码块显示行号", _ctx, settings.Reading.CodeLineNumbers) { Margin = new Thickness(0, 2, 0, 10) }
            .Also(sw => sw.Toggled += (_, v) =>
            {
                settings.Reading.CodeLineNumbers = v;
                AppHost.Settings.Touch();
                _onLayoutChanged();
            }));

        _content.Add(new MdSwitch("加载图片", _ctx, settings.Reading.LoadImages) { Margin = new Thickness(0, 0, 0, 10) }
            .Also(sw => sw.Toggled += (_, v) =>
            {
                settings.Reading.LoadImages = v;
                AppHost.Settings.Touch();
                _onLayoutChanged();
            }));

        _content.Add(new MdSwitch("显示状态栏", _ctx, settings.Reading.StatusBarVisible) { Margin = new Thickness(0, 0, 0, 10) }
            .Also(sw => sw.Toggled += (_, v) =>
            {
                settings.Reading.StatusBarVisible = v;
                AppHost.Settings.Touch();
                _onLayoutChanged();
            }));

        _content.Add(new MdSwitch("大纲自动同步", _ctx, settings.Reading.SyncOutline, 13, "滚动正文时自动高亮并滚动左侧大纲")
        {
            Margin = new Thickness(0, 0, 0, 10),
        }.Also(sw => sw.Toggled += (_, v) =>
        {
            settings.Reading.SyncOutline = v;
            AppHost.Settings.Touch();
        }));

        _content.Add(new MdSwitch("专注模式", _ctx, settings.Reading.FocusMode, 13, "隐藏工具栏、侧栏与状态栏，只留正文")
        {
            Margin = new Thickness(0, 0, 0, 10),
        }.Also(sw => sw.Toggled += (_, v) =>
        {
            settings.Reading.FocusMode = v;
            AppHost.Settings.Touch();
            _onLayoutChanged();
        }));
    }

    // -----------------------------------------------------------------------
    // 配置
    // -----------------------------------------------------------------------

    private void BuildConfig(AppSettings settings)
    {
        _content.Add(SectionTitle("配置"));

        _content.Add(new Label
        {
            Text = AppPaths.SettingsFile,
            FontSize = 11.5,
            FontFamily = _ctx.MonoFont,
            TextColor = _ctx.CTextSecondary,
            LineBreakMode = LineBreakMode.MiddleTruncation,
            Margin = new Thickness(0, 2, 0, 8),
        });

        var actions = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 2, 0, 4) };

        actions.Add(UiKit.FlatButton("打开配置目录", _ctx, () =>
        {
            PlatformLauncher.OpenFolder(AppPaths.ConfigRoot);
            return Task.CompletedTask;
        }, 12.5, horizontalPadding: 14));

        actions.Add(UiKit.FlatButton("打开主题目录", _ctx, () =>
        {
            PlatformLauncher.OpenFolder(AppPaths.ThemesDirectory);
            return Task.CompletedTask;
        }, 12.5, horizontalPadding: 14));

        actions.Add(UiKit.FlatButton("恢复默认设置", _ctx, () =>
        {
            var defaults = new AppSettings();
            var current = AppHost.Settings.Current;
            current.Appearance = defaults.Appearance;
            current.Reading = defaults.Reading;
            current.Window.SidebarWidth = defaults.Window.SidebarWidth;
            current.Window.SidebarVisible = defaults.Window.SidebarVisible;
            AppHost.Settings.Touch();
            _toast("已恢复默认设置");
            _onLayoutChanged();
            Rebuild();
            return Task.CompletedTask;
        }, 12.5, horizontalPadding: 14));

        _content.Add(actions);

        _content.Add(new Label
        {
            Text = $"MD · 版本 {AppHost.Version} · .NET 10 + MAUI 原生渲染",
            FontSize = 11,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CTextSecondary,
            Margin = new Thickness(0, 14, 0, 0),
        });
    }

    // -----------------------------------------------------------------------
    // 小工具
    // -----------------------------------------------------------------------

    private View SectionTitle(string text) => new Label
    {
        Text = text,
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        FontFamily = _ctx.BodyFont,
        TextColor = _ctx.CAccent,
        Margin = new Thickness(0, 16, 0, 8),
    };

    private View SliderRow(
        string title,
        string initialValueText,
        double current,
        double min,
        double max,
        double step,
        Action<double, Label> onValue)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 13,
            FontFamily = _ctx.BodyFont,
            TextColor = _ctx.CText,
            VerticalTextAlignment = TextAlignment.Center,
        };

        var valueLabel = new Label
        {
            Text = initialValueText,
            FontSize = 12,
            FontFamily = _ctx.MonoFont,
            TextColor = _ctx.CTextSecondary,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumWidthRequest = 78,
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Add(titleLabel, 0, 0);
        header.Add(valueLabel, 1, 0);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(current, min, max),
            MinimumTrackColor = _ctx.CAccent,
            MaximumTrackColor = _ctx.CControlBorder,
            ThumbColor = _ctx.CAccent,
            Margin = new Thickness(-4, -2, -4, 0),
        };

        slider.ValueChanged += (_, e) =>
        {
            var value = Math.Round(e.NewValue / step) * step;
            onValue(value, valueLabel);
        };

        slider.DragCompleted += (_, _) => _onLayoutChanged();

        return new VerticalStackLayout
        {
            Spacing = 2,
            Margin = new Thickness(0, 4, 0, 10),
            Children = { header, slider },
        };
    }
}

/// <summary>链式配置的小扩展（让「先构造再挂事件」的写法不那么啰嗦）。</summary>
internal static class UiExtensions
{
    public static T Also<T>(this T self, Action<T> configure)
    {
        configure(self);
        return self;
    }
}
