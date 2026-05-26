using CommunityToolkit.Mvvm.ComponentModel;
using LSMA.Models;

namespace LSMA.Services;

public sealed class AppStateService : ObservableObject
{
    private GameDirectoryState? _gameDirectory;
    private bool _isGameRunning;
    private SmapiLogSummary _logSummary = new();
    private SaveInfo? _currentSave;
    private IReadOnlyList<ModInfo> _mods = [];
    private bool _hasPendingRecovery;

    public GameDirectoryState? GameDirectory
    {
        get => _gameDirectory;
        set
        {
            if (SetProperty(ref _gameDirectory, value))
            {
                OnPropertyChanged(nameof(IsGameConfigured));
            }
        }
    }

    public bool IsGameConfigured => GameDirectory is not null;

    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetProperty(ref _isGameRunning, value);
    }

    public SmapiLogSummary LogSummary
    {
        get => _logSummary;
        set => SetProperty(ref _logSummary, value);
    }

    public SaveInfo? CurrentSave
    {
        get => _currentSave;
        set => SetProperty(ref _currentSave, value);
    }

    public IReadOnlyList<ModInfo> Mods
    {
        get => _mods;
        set => SetProperty(ref _mods, value);
    }

    public bool HasPendingRecovery
    {
        get => _hasPendingRecovery;
        set => SetProperty(ref _hasPendingRecovery, value);
    }
}
