using System.Diagnostics;
using LSMA.Models;

namespace LSMA.Services;

public sealed class GameLaunchService(
    AppStateService state,
    GameRunLockService runLock,
    LoggingService logging)
{
    public LaunchCheckResult PrepareLaunch(LaunchTarget target, LaunchMode mode)
    {
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            return new LaunchCheckResult
            {
                Title = "游戏正在运行",
                Message = "游戏正在运行，不能再次启动。"
            };
        }

        if (state.HasPendingRecovery)
        {
            return new LaunchCheckResult
            {
                Title = "恢复尚未完成",
                Message = "存在未完成的安全恢复任务。请先完成或重新执行恢复，再启动游戏。"
            };
        }

        if (state.GameDirectory is not { } directory)
        {
            return new LaunchCheckResult
            {
                Title = "未连接游戏",
                Message = "请先选择有效的游戏目录。"
            };
        }

        var executable = target == LaunchTarget.Smapi
            ? Path.Combine(directory.Path, "StardewModdingAPI.exe")
            : Path.Combine(directory.Path, "Stardew Valley.exe");

        if (target == LaunchTarget.Smapi && !File.Exists(executable))
        {
            return new LaunchCheckResult
            {
                Title = "未安装 SMAPI",
                Message = "当前目录未找到 StardewModdingAPI.exe。是否改为启动原版游戏？",
                CanFallbackToVanilla = directory.HasVanilla
            };
        }

        if (!File.Exists(executable))
        {
            return new LaunchCheckResult
            {
                Title = "启动程序不存在",
                Message = "当前启动方式对应的程序文件不存在，请检查游戏目录。"
            };
        }

        if (mode != LaunchMode.Quick && state.LogSummary.HasCrash)
        {
            return new LaunchCheckResult
            {
                CanLaunch = true,
                RequiresConfirmation = true,
                Title = "检测到最近崩溃",
                Message = "最近的 SMAPI 日志包含崩溃记录。建议先查看详情；仍要启动吗？",
                ExecutablePath = executable
            };
        }

        var blockingMods = state.Mods.Count(mod => mod.IsEnabled
            && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Error));
        if (mode != LaunchMode.Quick && blockingMods > 0)
        {
            return new LaunchCheckResult
            {
                CanLaunch = true,
                RequiresConfirmation = true,
                Title = "模组存在阻断问题",
                Message = $"检测到 {blockingMods} 个启用中的模组存在错误。建议先使用一键修复禁用它们；仍要启动吗？",
                ExecutablePath = executable
            };
        }

        return new LaunchCheckResult { CanLaunch = true, ExecutablePath = executable };
    }

    public async Task<bool> LaunchAsync(
        LaunchTarget target,
        LaunchMode mode,
        Func<Task>? afterDiagnosticExit = null)
    {
        var directory = state.GameDirectory;
        if (directory is null)
        {
            return false;
        }

        var executable = target == LaunchTarget.Smapi
            ? Path.Combine(directory.Path, "StardewModdingAPI.exe")
            : Path.Combine(directory.Path, "Stardew Valley.exe");
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory.Path,
                UseShellExecute = true
            });
            if (process is null)
            {
                return false;
            }

            runLock.Track(process, mode, afterDiagnosticExit);
            await logging.InfoAsync($"启动游戏：{target} / {mode}");
            return true;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("启动游戏失败", exception);
            return false;
        }
    }
}
