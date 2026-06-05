using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LSMA.Utilities;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    public void ReplaceWith(IEnumerable<T> items)
    {
        CheckReentrancy();
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        RaiseReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        var changed = false;
        _suppressNotifications = true;
        try
        {
            foreach (var item in items)
            {
                Items.Add(item);
                changed = true;
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        if (changed)
        {
            RaiseReset();
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnPropertyChanged(e);
        }
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
