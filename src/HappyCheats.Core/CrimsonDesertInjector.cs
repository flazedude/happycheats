using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Loads the packaged trainer only after the user's explicit Connect command.</summary>
internal static class CrimsonDesertInjector
{
    internal const string RelativeDll = "bin64/HappyCheatsCrimsonDesert/HappyCheats.CrimsonDesert.dll";

    public static void Attach(string directory)
    {
        if (!Environment.Is64BitProcess) throw new IOException("Crimson Desert requires the 64-bit HappyCheats client.");
        var games = Processes(directory, "bin64/CrimsonDesert.exe");
        if (games.Length != 1) throw new IOException("Exactly one Crimson Desert process must be running.");
        var dll = SafePath(directory, RelativeDll);
        if (!File.Exists(dll)) throw new IOException("Install the Crimson Desert trainer first.");
        using var game = Process.GetProcessById(games[0].Id);
        using var handle = OpenProcess(0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400, false, game.Id);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot connect to Crimson Desert. Run both programs at the same permission level.");
        if (game.StartTime.ToUniversalTime() != games[0].Started || game.HasExited)
            throw new IOException("The game session changed. Try connecting again.");

        var existing = FindModule(game, Path.GetFileName(dll));
        if (existing is not null)
        {
            if (!string.Equals(Path.GetFullPath(existing.FileName), dll, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A different Crimson Desert trainer DLL is already loaded. Restart the game.");
            return;
        }

        // GetProcAddress may forward LoadLibraryW to KernelBase. Locate its actual
        // owning module and apply that module-relative offset to the target process.
        var loader = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
        if (loader == 0 || !GetModuleHandleExW(0x0004 | 0x0002, loader, out var owner))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var ownerName = new StringBuilder(32768);
        if (GetModuleFileNameW(owner, ownerName, ownerName.Capacity) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var targetOwner = FindModule(game, Path.GetFileName(ownerName.ToString()))
            ?? throw new IOException("The game's Windows loader module is unavailable.");
        var offset = loader.ToInt64() - owner.ToInt64();
        if (offset < 0 || offset >= targetOwner.ModuleMemorySize) throw new IOException("Unexpected Windows loader address.");
        var targetLoader = new nint(checked(targetOwner.BaseAddress.ToInt64() + offset));
        var bytes = Encoding.Unicode.GetBytes(dll + '\0');
        var remotePath = VirtualAllocEx(handle, 0, (nuint)bytes.Length, 0x3000, 0x04);
        if (remotePath == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var canFree = true;
        try
        {
            if (!WriteProcessMemory(handle, remotePath, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var thread = CreateRemoteThread(handle, 0, 0, targetLoader, remotePath, 0, out _);
            if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            canFree = false;
            var waited = WaitForSingleObject(thread, 15000);
            if (waited != 0)
                throw new IOException("The game has not finished connecting. Check trainer status before retrying; restart the game if it remains disconnected.");
            canFree = true;
            game.Refresh();
            var loaded = FindModule(game, Path.GetFileName(dll));
            if (loaded is null || !string.Equals(Path.GetFullPath(loaded.FileName), dll, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Windows did not load the trainer. Check that the installed payload is intact.");
        }
        finally
        {
            // If LoadLibrary is still running, its argument must remain allocated.
            // The allocation then belongs to the process until it exits.
            if (canFree) VirtualFreeEx(handle, remotePath, 0, 0x8000);
        }
    }

    private static ProcessModule? FindModule(Process process, string name)
    {
        foreach (ProcessModule module in process.Modules)
            if (string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase)) return module;
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetModuleFileNameW(nint module, StringBuilder name, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, nint attributes, nuint stackSize, nint start, nint parameter, uint flags, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
