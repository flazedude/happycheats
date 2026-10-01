using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HappyCheats.App;

internal readonly record struct Civ6HotkeyScope(int ProcessId, string? SessionId, bool Busy, bool EldenRing = false)
{
    public bool Matches(int foreground) => ProcessId > 0 && foreground == ProcessId && !string.IsNullOrEmpty(SessionId);
    public static bool EldenRingShortcut(string shortcut) => shortcut is "F1" or "F2" or "F3" or "F4" or "F12";
    public bool Accepts(string shortcut) => !EldenRing || EldenRingShortcut(shortcut);
}

internal readonly record struct Civ6HotkeyRequest(int ProcessId, string SessionId, string Shortcut)
{
    public bool Matches(Civ6HotkeyScope scope, int foreground) => !scope.Busy && scope.Matches(foreground)
        && scope.ProcessId == ProcessId && scope.SessionId == SessionId && scope.Accepts(Shortcut);
}

internal readonly record struct Civ6HotkeyDecision(bool Suppress, Civ6HotkeyRequest? Request = null);

// Civilization VI/VII and Elden Ring share this process/session guard.
// Only function keys and modifier state are retained. No typed text is collected.
internal sealed class Civ6HotkeyRouter
{
    private readonly bool[] _down = new bool[256];
    private readonly bool[] _intercepted = new bool[12];
    internal static bool Interested(int key) => key is >= 0x70 and <= 0x7B or 0x10 or 0x11 or 0x12
        or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
    internal void SeedHeld(int key) { if (Interested(key)) _down[key] = true; }

    internal static string? Shortcut(int key, bool shift, bool control, bool alt, bool windows)
    {
        if (control || alt || windows || key < 0x70 || key > (shift ? 0x73 : 0x7B)) return null;
        return (shift ? "Shift+" : "") + "F" + (key - 0x70 + 1);
    }

    internal Civ6HotkeyDecision Process(int key, bool down, Civ6HotkeyScope scope, int foreground)
    {
        if (!Interested(key)) return default;
        var wasDown = _down[key];
        _down[key] = down;
        if (key < 0x70 || key > 0x7B) return default;
        var index = key - 0x70;
        var scoped = scope.Matches(foreground);
        if (!down)
        {
            var suppressUp = scoped && _intercepted[index];
            _intercepted[index] = false;
            return new(suppressUp);
        }
        var shortcut = Shortcut(key, _down[0x10] || _down[0xA0] || _down[0xA1],
            _down[0x11] || _down[0xA2] || _down[0xA3], _down[0x12] || _down[0xA4] || _down[0xA5],
            _down[0x5B] || _down[0x5C]);
        if (shortcut != null && !scope.Accepts(shortcut)) shortcut = null;
        // Consume repeats without firing, even if the press began outside the game.
        // Once intercepted, keep the remainder of that press out of the game.
        var suppress = scoped && (shortcut != null || _intercepted[index]);
        _intercepted[index] |= suppress;
        var request = suppress && !wasDown && !scope.Busy && shortcut != null
            ? new Civ6HotkeyRequest(scope.ProcessId, scope.SessionId!, shortcut) : (Civ6HotkeyRequest?)null;
        return new(suppress, request);
    }
}

internal sealed class Civ6KeyboardHook : IDisposable
{
    private readonly Civ6HotkeyRouter _router = new();
    private readonly Func<Civ6HotkeyScope> _scope;
    private readonly Action<Civ6HotkeyRequest> _queue;
    private readonly HookProc _callback;
    private IntPtr _hook;

    internal Civ6KeyboardHook(Func<Civ6HotkeyScope> scope, Action<Civ6HotkeyRequest> queue)
    {
        _scope = scope;
        _queue = queue;
        _callback = Callback;
        // Seed only left/right modifiers; the generic aliases would remain latched
        // when Windows subsequently reports a left/right release.
        foreach (var key in Enumerable.Range(0x70, 12).Concat(new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }))
            if ((GetAsyncKeyState(key) & 0x8000) != 0) _router.SeedHeld(key);
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() is 0x100 or 0x101 or 0x104 or 0x105)
        {
            var key = Marshal.ReadInt32(data);
            if (Civ6HotkeyRouter.Interested(key))
            {
                var down = message.ToInt64() is 0x100 or 0x104;
                var decision = _router.Process(key, down, _scope(), ForegroundProcessId());
                // The callback only reads small state and queues work; never wait
                // for the game or enumerate processes in a low-level hook.
                if (decision.Request is { } request) _queue(request);
                if (decision.Suppress) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    internal static int ForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        return unchecked((int)process);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
