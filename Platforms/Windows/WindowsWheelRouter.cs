// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsWheelRouter.cs
//  说明：鼠标滚轮统一路由。
//
//        要解决两件事：
//          1) 代码块为了横向滚动套了一层 ScrollViewer。WinUI 里横向可滚的
//             ScrollViewer 会把纵向滚轮吃掉（拿去横向滚动），于是「焦点在代码块上
//             时滚轮翻页失效」。
//          2) Ctrl + 滚轮 调节字号。
//
//        做法：在页面根元素上以 handledEventsToo: true 挂 PointerWheelChanged。
//        WinUI 没有隧道事件，类处理器一定先跑，所以这里只能「事后纠正」：
//        当指针所在的 ScrollViewer 无法纵向滚动时，把纵向滚动转交给最近一个
//        能纵向滚动的祖先（阅读区列表），并把被抢走的横向偏移还原回去。
//        兜底：若找不到可纵向滚动的祖先，就什么都不做，行为与原生一致。
//
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MD.Platforms.Windows;

internal static class WindowsWheelRouter
{
    private static readonly Dictionary<ScrollViewer, double> LastHorizontal = new();
    private static bool _attached;

    // ---- 诊断用（排查「滚轮没被转交」时看 md.log；限流，避免刷屏）----
    private static readonly System.Diagnostics.Stopwatch DiagClock = System.Diagnostics.Stopwatch.StartNew();
    private static long _lastDiagTick = -10000;
    private static string _lastDiag = string.Empty;

    private static void Diag(string message)
    {
        var now = DiagClock.ElapsedMilliseconds;
        if (message == _lastDiag && now - _lastDiagTick < 1000)
            return;
        _lastDiag = message;
        _lastDiagTick = now;
        Services.DiagnosticsLog.Write($"[滚轮] {message}");
    }

    /// <summary>
    /// 挂载滚轮路由。
    /// </summary>
    /// <param name="root">页面根元素（MAUI 的 ContentPanel）。</param>
    /// <param name="onZoom">Ctrl + 滚轮的回调，参数为 ±1（一格）。</param>
    public static void Attach(FrameworkElement root, Action<int> onZoom)
    {
        if (_attached)
            return;
        _attached = true;

        root.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((sender, e) => OnWheel(sender, e, onZoom)),
            handledEventsToo: true);
    }

    private static void OnWheel(object sender, PointerRoutedEventArgs e, Action<int> onZoom)
    {
        if (sender is not UIElement root)
            return;

        int delta;
        try
        {
            delta = e.GetCurrentPoint(root).Properties.MouseWheelDelta;
        }
        catch
        {
            return;
        }

        if (delta == 0)
            return;

        // ---- Ctrl + 滚轮：字号 ----
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
        {
            onZoom(delta > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }

        // ---- Shift + 滚轮：保留平台默认的横向滚动 ----
        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            return;

        var inner = FindAncestor<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (inner is null)
        {
            Diag($"指针所在处往上找不到 ScrollViewer（源={e.OriginalSource?.GetType().Name}）");
            return;
        }

        // 自己能纵向滚动就别抢
        if (CanScrollVertically(inner))
        {
            Diag($"内层 ScrollViewer 本身可纵向滚动，放行（可滚高度={inner.ScrollableHeight:0}）");
            return;
        }

        var outer = FindScrollableAncestor(inner);
        if (outer is null)
        {
            Diag("往上找不到可纵向滚动的祖先 ScrollViewer，放行");
            return;
        }

        // 内层横向可滚的 ScrollViewer 可能已经被类处理器横向滚了一格，还原它。
        if (LastHorizontal.TryGetValue(inner, out var previous) &&
            Math.Abs(inner.HorizontalOffset - previous) > 0.5)
        {
            inner.ChangeView(previous, null, null, true);
        }

        Remember(inner);

        try
        {
            double target = Math.Max(0, outer.VerticalOffset - delta);
            bool ok = outer.ChangeView(null, target, null, false);
            Diag($"已把滚轮转交给祖先 ScrollViewer：{outer.VerticalOffset:0} → {target:0}（返回值={ok}）");
            e.Handled = true;
        }
        catch (Exception ex)
        {
            // 目标已销毁时忽略
            Services.DiagnosticsLog.Write("[滚轮] 转交失败", ex);
        }
    }

    /// <summary>记录横向偏移（用 ViewChanged 保持最新，供下一次滚轮还原）。</summary>
    private static void Remember(ScrollViewer viewer)
    {
        if (!LastHorizontal.ContainsKey(viewer))
        {
            if (LastHorizontal.Count > 64)
                LastHorizontal.Clear();

            viewer.ViewChanged += (s, _) =>
            {
                if (s is ScrollViewer v)
                    LastHorizontal[v] = v.HorizontalOffset;
            };
        }

        LastHorizontal[viewer] = viewer.HorizontalOffset;
    }

    private static bool CanScrollVertically(ScrollViewer viewer)
    {
        try
        {
            return viewer.VerticalScrollMode != Microsoft.UI.Xaml.Controls.ScrollMode.Disabled &&
                   viewer.ScrollableHeight > 0.5;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>沿可视树向上找最近一个「能纵向滚动」的 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollableAncestor(DependencyObject? node)
    {
        var current = node;
        int guard = 0;
        while (current is not null && guard++ < 64)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer viewer && CanScrollVertically(viewer))
                return viewer;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        var current = node;
        int guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current is T hit)
                return hit;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
#endif
