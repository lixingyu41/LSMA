using Microsoft.UI.Xaml.Controls;

namespace LSMA.Pages;

public sealed partial class SavesPage : Page
{
    public SavesPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Saves;
    }
}
