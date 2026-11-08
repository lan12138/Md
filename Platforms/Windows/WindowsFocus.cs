// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsFocus.cs
//  说明：程序化滚动前把焦点从虚拟化内容里挪走。
//
//        背景：代码块里有「复制」按钮和横向 ScrollViewer，点过之后焦点会停在
//        这一项里。此时点大纲让 CollectionView 滚动，WinUI 会回收这一项，
//        焦点对象的原生元素被销毁，随后可能直接
//        E_UNEXPECTED (0x8000FFFF) 原生闪退（不是托管异常，catch 不到）。
//        这里先把焦点交给 CollectionView 自己的外层 ScrollViewer：它是稳定的，
//        不参与 item 回收，也不会改变滚动位置。
//  作者：MD
//  创建：2026-11-08
//  修改：2026-11-08  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MD.Platforms.Windows;

internal static class WindowsFocus
{
    /// <summary>把焦点从列表项内部挪到列表外层 ScrollViewer；失败时静默。</summary>
    public static void ReleaseFromVirtualizedList(VisualElement list)
    {
        try
        {
            if (list.Handler?.PlatformView is not DependencyObject root)
                return;

            var viewer = FindScrollViewer(root, depth: 0);
            if (viewer is null || viewer.XamlRoot is null || !viewer.IsLoaded)
                return;

            viewer.Focus(FocusState.Programmatic);
        }
        catch
        {
            // 焦点操作失败不能影响跳转本身
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node, int depth)
    {
        if (depth > 8)
            return null;

        if (node is ScrollViewer sv)
            return sv;

        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(node);
        }
        catch
        {
            return null;
        }

        for (int i = 0; i < count; i++)
        {
            DependencyObject child;
            try
            {
                child = VisualTreeHelper.GetChild(node, i);
            }
            catch
            {
                continue;
            }

            var found = FindScrollViewer(child, depth + 1);
            if (found is not null)
                return found;
        }

        return null;
    }
}
#endif
