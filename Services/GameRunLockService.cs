using System.Diagnostics;
using LSMA.Models;
using Microsoft.UI.Dispatching;

namespace LSMA.Services;

public sealed class GameRunLockService(AppStateService state, LoggingService logging)
{
    private DispatcherQueue? _dispatcher;

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool Refresh()
    {
        var isRunning = IsGameProcessRunning();
        state.IsGameRunning = isRunning;
        return isRunning;
    }

    public void Track(Process process, LaunchMode mode, Func<Task>? afterDiagnosticExit = null)
    {
        state.IsGameRunning = true;
        process.EnableRaisingEvents = true;
        process.Exited += async (_, _) =>
        {
            if (_dispatcher is null)
            {
                return;
            }

            _dispatcher.TryEnqueue(() => state.IsGameRunning = IsGameProcessRunning());
            if (mode == LaunchMode.Diagnostic && afterDiagnosticExit is not null)
            {
                await Task.Delay(800);
                _dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        await afterDiagnosticExit();
                    }
                    catch (Exception exception)
                    {
                        await logging.ErrorAsync("诊断启动退出后的检查失败", exception);
                    }
                });
            }
        };
    }

    private static bool IsGameProcessRunning()
    {
        return Process.GetProcessesByName("Stardew Valley").Length > 0
            || Process.GetProcessesByName("StardewModdingAPI").Length > 0;
    }
}
