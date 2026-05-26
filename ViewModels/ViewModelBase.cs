using CommunityToolkit.Mvvm.ComponentModel;

namespace LSMA.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string _progressText = string.Empty;
    private string? _feedbackMessage;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string ProgressText
    {
        get => _progressText;
        protected set => SetProperty(ref _progressText, value);
    }

    public string? FeedbackMessage
    {
        get => _feedbackMessage;
        protected set => SetProperty(ref _feedbackMessage, value);
    }
}
