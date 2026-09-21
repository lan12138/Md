// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/Toast.cs
//  说明：轻量提示条（替代原生弹窗，不打断阅读）。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Controls;
using MD.Rendering;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Views;

public sealed class Toast : Grid
{
    private readonly Label _label;
    private readonly Border _border;
    private CancellationTokenSource? _cts;

    public Toast()
    {
        InputTransparent = true;
        IsVisible = false;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.End;

        _label = new Label
        {
            FontSize = 12.5,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        _border = new Border
        {
            Content = _label,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Padding = new Thickness(16, 9),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 28),
        };

        Add(_border);
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _border.BackgroundColor = ctx.CText.WithAlpha(0.92f);
        _label.TextColor = ctx.CEditorBg;
        _label.FontFamily = ctx.BodyFont;
    }

    public void Show(RenderContext ctx, string message, int durationMs = 1800)
    {
        ApplyTheme(ctx);
        _label.Text = message;
        IsVisible = true;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs, token);
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.Dispatch(() =>
                    {
                        if (!token.IsCancellationRequested)
                            IsVisible = false;
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // 被新的提示覆盖，正常
            }
        });
    }

    /// <summary>主题切换时避免残留旧配色。</summary>
    public void NotifyThemeChanged(RenderContext ctx)
    {
        if (IsVisible)
            ApplyTheme(ctx);
    }
}
