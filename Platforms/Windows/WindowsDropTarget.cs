#if WINDOWS
// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsDropTarget.cs
//  说明：Windows 端的「拖拽文件到窗口打开」。
//        MAUI 的 DropGestureRecognizer 拿不到本地文件路径，这里直接接
//        WinUI 的 DragOver / Drop 事件，可以拿到 StorageFile.Path。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
//  修改：2026-09-21  DataPackageOperation 二义性（MAUI 全局 using 里也有同名枚举）→ 走别名
// -----------------------------------------------------------------------------

using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

// Microsoft.Maui.Controls 也存在 DataPackageOperation（MAUI 的拖拽枚举），
// 且 MAUI 项目默认全局 using 了该命名空间，故必须用别名钉死到 WinUI 那个。
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace MD.Platforms.Windows;

internal static class WindowsDropTarget
{
    public static void Enable(
        VisualElement element,
        Func<string, Task> onFileDropped,
        Action<bool>? onDragStateChanged = null)
    {
        element.HandlerChanged += (_, _) =>
        {
            if (element.Handler?.PlatformView is not UIElement ui)
                return;

            ui.AllowDrop = true;

            ui.DragOver += (_, e) =>
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                try
                {
                    e.DragUIOverride.Caption = "用 MD 打开";
                    e.DragUIOverride.IsCaptionVisible = true;
                    e.DragUIOverride.IsContentVisible = true;
                }
                catch
                {
                    // 某些系统版本没有 DragUIOverride，忽略
                }
                onDragStateChanged?.Invoke(true);
            };

            ui.DragLeave += (_, _) => onDragStateChanged?.Invoke(false);

            ui.Drop += async (_, e) =>
            {
                onDragStateChanged?.Invoke(false);
                try
                {
                    if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                        return;

                    var items = await e.DataView.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        if (item is StorageFile file && !string.IsNullOrEmpty(file.Path))
                            await onFileDropped(file.Path);
                    }
                }
                catch
                {
                    // 拖拽失败不影响阅读
                }
            };
        };
    }
}
#endif
