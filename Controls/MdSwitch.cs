// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Controls/MdSwitch.cs
//  说明：自绘开关。原生 Switch 在 Windows / Android 上观感差异大且难以统一配色，
//        这里用 Border 自绘「胶囊 + 圆点」，保证各平台与主题下完全一致。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Rendering;
using Microsoft.Maui.Controls.Shapes;

namespace MD.Controls;

public sealed class MdSwitch : Grid
{
    private readonly Border _track;
    private readonly Border _thumb;
    private readonly Label _label;
    private RenderContext _ctx;
    private bool _isOn;

    public event EventHandler<bool>? Toggled;

    public MdSwitch(string text, RenderContext ctx, bool initial, double fontSize = 13, string? hint = null)
    {
        _ctx = ctx;
        _isOn = initial;

        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnSpacing = 14;

        var texts = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        _label = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = ctx.BodyFont,
            TextColor = ctx.CText,
        };
        texts.Add(_label);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            texts.Add(new Label
            {
                Text = hint,
                FontSize = fontSize - 2,
                FontFamily = ctx.BodyFont,
                TextColor = ctx.CTextSecondary,
                LineHeight = 1.4,
            });
        }
        this.Add(texts, 0, 0);

        _thumb = new Border
        {
            WidthRequest = 18,
            HeightRequest = 18,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) },
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
        };

        _track = new Border
        {
            Content = _thumb,
            WidthRequest = 42,
            HeightRequest = 24,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(3),
            VerticalOptions = LayoutOptions.Center,
        };
        this.Add(_track, 1, 0);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => IsOn = !IsOn;
        _track.GestureRecognizers.Add(tap);
        GestureRecognizers.Add(tap);

        Paint();
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
                return;
            _isOn = value;
            Paint();
            Toggled?.Invoke(this, value);
        }
    }

    public void ApplyTheme(RenderContext ctx)
    {
        _ctx = ctx;
        _label.TextColor = ctx.CText;
        Paint();
    }

    private void Paint()
    {
        _track.BackgroundColor = _isOn ? _ctx.CAccent : _ctx.CControlBorder;
        _thumb.BackgroundColor = Colors.White;
        _thumb.HorizontalOptions = _isOn ? LayoutOptions.End : LayoutOptions.Start;
    }
}
