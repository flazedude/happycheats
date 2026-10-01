using System.Runtime.InteropServices;

namespace LaundryTrainer;

internal sealed class Hotkeys
{
    private readonly Dictionary<int, bool> _held = new();

    public bool Down(int virtualKey)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        return foregroundProcess == Environment.ProcessId && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    public bool Pressed(int virtualKey)
    {
        // Poll the high bit, not the shared "pressed since last call" flag.
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        var down = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        var wasHeld = _held.TryGetValue(virtualKey, out var value) && value;
        _held[virtualKey] = down;
        return foregroundProcess == Environment.ProcessId && down && !wasHeld;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
