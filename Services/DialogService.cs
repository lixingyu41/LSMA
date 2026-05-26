using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LSMA.Services;

public sealed class DialogService
{
    private XamlRoot? _xamlRoot;

    public void AttachRoot(FrameworkElement root)
    {
        _xamlRoot = root.XamlRoot;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        if (_xamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            XamlRoot = _xamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "继续")
    {
        if (_xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _xamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
