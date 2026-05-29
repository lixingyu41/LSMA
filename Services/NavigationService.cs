using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LSMA.Services;

public sealed class NavigationService
{
    private Frame? _frame;
    private object? _currentParameter;

    public void AttachFrame(Frame frame)
    {
        if (_frame == frame)
        {
            return;
        }

        if (_frame is not null)
        {
            _frame.Navigated -= Frame_Navigated;
        }

        _frame = frame;
        _frame.Navigated += Frame_Navigated;
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            return;
        }

        if (_frame.CurrentSourcePageType == pageType && Equals(_currentParameter, parameter))
        {
            return;
        }

        _frame.Navigate(pageType, parameter);
    }

    public bool GoBack()
    {
        if (_frame?.CanGoBack != true)
        {
            return false;
        }

        _frame.GoBack();
        return true;
    }

    public bool GoForward()
    {
        if (_frame?.CanGoForward != true)
        {
            return false;
        }

        _frame.GoForward();
        return true;
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        _currentParameter = e.Parameter;
    }
}
