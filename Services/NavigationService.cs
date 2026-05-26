using Microsoft.UI.Xaml.Controls;

namespace LSMA.Services;

public sealed class NavigationService
{
    private Frame? _frame;

    public void AttachFrame(Frame frame)
    {
        _frame = frame;
    }

    public void Navigate(Type pageType)
    {
        if (_frame?.CurrentSourcePageType != pageType)
        {
            _frame?.Navigate(pageType);
        }
    }
}
