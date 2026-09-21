// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsFolderPicker.cs
//  说明：文件夹选择。MAUI 的 FilePicker 只能选文件，选文件夹得用 WinRT 的
//        FolderPicker，并且要先把它和当前窗口句柄关联，否则会直接抛异常。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using Windows.Storage.Pickers;

namespace MD.Platforms.Windows;

internal static class WindowsFolderPicker
{
    public static async Task<string?> PickAsync(Microsoft.Maui.Controls.Window? window, string? startAt = null)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        // FolderPicker 要求至少有一个类型过滤，否则调用即失败
        picker.FileTypeFilter.Add("*");

        if (!string.IsNullOrWhiteSpace(startAt) && Directory.Exists(startAt))
        {
            try
            {
                picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            }
            catch
            {
                // 忽略：位置建议失败不影响选择
            }
        }

        // 关键：必须把 picker 关联到窗口句柄，否则 WinUI 3 会抛
        // "A window handle must be obtained before the picker is used"
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window?.Handler?.PlatformView);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
#endif
