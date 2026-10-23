using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using HappyCheats.Core;

namespace HappyCheats.App;

/// <summary>Resolves a module's exact installation targets away from the UI/input timer.</summary>
internal static class GameWindowLocator
{
    internal static Task<(int[] ProcessIds, IntPtr Window)> ReadAsync(IGameModule module, GameInstallation installation) => Task.Run(() =>
    {
        if (!installation.IsInstalled || string.IsNullOrWhiteSpace(installation.Directory))
            return (Array.Empty<int>(), IntPtr.Zero);
        try
        {
            var processes = new Dictionary<int, DateTime>();
            foreach (var executable in module.GameExecutables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Module metadata must remain inside the selected installation. A
                // matching filename elsewhere never grants foreground shortcuts.
                if (string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable))
                    return (Array.Empty<int>(), IntPtr.Zero);
                ModuleIO.SafePath(installation.Directory, executable);
                foreach (var process in ModuleIO.Processes(installation.Directory, executable))
                    processes[process.Id] = process.Started;
            }
            return SelectUniqueWindow(processes.Keys, processId =>
            {
                using var process = Process.GetProcessById(processId);
                // A process may exit and its PID may be recycled between the
                // executable check and reading the window handle.
                if (process.HasExited || process.StartTime.ToUniversalTime() != processes[processId])
                    throw new InvalidOperationException("The game process changed during window discovery.");
                return process.MainWindowHandle;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Window targeting is optional; races and inaccessible processes must
            // hide the menu without taking a healthy trainer session offline.
            return (Array.Empty<int>(), IntPtr.Zero);
        }
    });

    internal static (int[] ProcessIds, IntPtr Window) SelectUniqueWindow(IEnumerable<int> processIds, Func<int, IntPtr> readWindow)
    {
        var candidates = processIds.Distinct().ToArray();
        if (candidates.Length != 1 || candidates[0] <= 0) return (Array.Empty<int>(), IntPtr.Zero);
        return (candidates, readWindow(candidates[0]));
    }
}
