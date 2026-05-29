using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace LSMA.Services;

public sealed class PageAccelerationService
{
    private const int CachedPageCount = 6;
    private Frame? _frame;
    private bool _enabled = true;

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
        ApplyToFrame();
    }

    public void Apply(bool enabled)
    {
        _enabled = enabled;
        ApplyToFrame();
    }

    private void Frame_Navigated(object sender, NavigationEventArgs e)
    {
        ApplyToCurrentPage();
    }

    private void ApplyToFrame()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.CacheSize = _enabled ? CachedPageCount : 0;
        _frame.CacheMode = CreateCacheMode();
        ApplyToCurrentPage();
    }

    private void ApplyToCurrentPage()
    {
        if (_frame?.Content is not Page page)
        {
            return;
        }

        page.NavigationCacheMode = _enabled
            ? NavigationCacheMode.Enabled
            : NavigationCacheMode.Disabled;
        page.CacheMode = CreateCacheMode();

        if (page.Content is UIElement root)
        {
            root.CacheMode = CreateCacheMode();
        }
    }

    private CacheMode? CreateCacheMode()
    {
        return _enabled ? new BitmapCache() : null;
    }
}
