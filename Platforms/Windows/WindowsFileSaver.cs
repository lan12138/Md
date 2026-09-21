// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Platforms/Windows/WindowsFileSaver.cs
//  说明：「另存为」对话框。MAUI 只提供打开用的 FilePicker，没有保存用 API，
//        所以这里直接用 WinRT 的 FileSavePicker，并同样需要先关联窗口句柄。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

#if WINDOWS
using Windows.Storage.Pickers;

namespace MD.Platforms.Windows;

internal static class WindowsFileSaver
{
    public static async Task<string?> PickAsync(
        Microsoft.Maui.Controls.Window? window,
        string suggestedName,
        string typeLabel,
        string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };

        picker.FileTypeChoices.Add(typeLabel, new List<string> { extension });

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window?.Handler?.PlatformView);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
#endif
