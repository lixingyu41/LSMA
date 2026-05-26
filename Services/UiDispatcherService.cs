using Microsoft.UI.Dispatching;

namespace LSMA.Services;

public sealed class UiDispatcherService
{
    private DispatcherQueue? _dispatcher;

    public void Attach(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Enqueue(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() => action());
    }
}
