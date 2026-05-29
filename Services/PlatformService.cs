using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace LSMA.Services;

public sealed class PlatformService(LoggingService logging)
{
    private Window? _window;

    public void AttachWindow(Window window)
    {
        _window = window;
    }

    public async Task<string?> ChooseFolderAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> ChooseArchiveAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".7z");
        picker.FileTypeFilter.Add(".rar");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> ChooseJsonAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> ChooseJsonSavePathAsync(string suggestedName)
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> ChooseMarkdownSavePathAsync(string suggestedName)
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("Markdown", [".md"]);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task OpenFolderAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(path);
            await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"无法打开目录 {path}", exception);
            throw;
        }
    }

    public async Task OpenUriAsync(string uri)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(uri)))
            {
                throw new InvalidOperationException("系统未能打开该链接。");
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("无法打开外部链接", exception);
            throw;
        }
    }

    public void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public async Task<string?> GetClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Text)
                ? await content.GetTextAsync()
                : null;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("无法读取剪贴板文本", exception);
            return null;
        }
    }
}
