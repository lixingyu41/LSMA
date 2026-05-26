using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace LSMA.Pages;

public sealed partial class ModsPage : Page
{
    public ModsPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Mods;
    }

    private void Package_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "预检模组压缩包";
        }
    }

    private async void Package_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is StorageFile file)
        {
            await App.Current.Services.Mods.InspectPackageAsync(file.Path);
        }
    }
}
