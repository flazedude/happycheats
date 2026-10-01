using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static HappyCheats.Core.ModuleIO;

namespace HappyCheats.Core;

/// <summary>Owns only the inspected game's single aging boolean. Never patches executable code.</summary>
internal sealed class HotelArchitectAging : IDisposable
{
    internal sealed record Context(string Directory, int ProcessId, DateTime Started, string World);
    internal readonly record struct State(bool Available, bool Enabled, byte NativeValue);
    internal interface IMemory : IDisposable { byte Read(); void Write(byte expected, byte value); }
    private sealed record Ownership(Context Context, byte Original);
    private readonly object gate = new();
    private readonly Func<Context, bool, IMemory> open;
    private readonly Func<Context, bool> currentWorld;
    private Ownership? owned;
    private bool disposed;
    private readonly EventHandler onExit;

    internal HotelArchitectAging(Func<Context, bool> currentWorld, Func<Context, bool, IMemory>? open = null)
    {
        this.currentWorld = currentWorld;
        this.open = open ?? ((context, writable) => new NativeMemory(context, writable));
        onExit = (_, _) => Dispose();
        AppDomain.CurrentDomain.ProcessExit += onExit;
    }

    internal State Observe(Context? context)
    {
        lock (gate) {
            if (disposed) return default;
            // Settings are recreated for a new world. Never put an old world's
            // original setting into the newly loaded world's difficulty data.
            if (context != null && owned is { } previous && previous.Context != context) ClearOwnership();
            if (context == null || !currentWorld(context)) return default;
            using var memory = open(context, false);
            var value = memory.Read();
            if (value > 1) throw new IOException("Hotel Architect's aging setting is not a boolean.");
            if (owned != null && value != 0) ClearOwnership(); // The game took control of its setting.
            return new State(true, owned != null && value == 0, value);
        }
    }

    internal bool HasOwnership { get { lock (gate) return owned != null; } }
    internal void RestoreOwned() { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); Restore(); } }

    internal void Set(Context context, bool disableAging)
    {
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!currentWorld(context)) throw new InvalidOperationException("Hotel Architect's world changed. Refresh before editing aging.");
            if (owned is { } previous && previous.Context != context) ClearOwnership();
            if (!disableAging) { Restore(context); return; }
            using var memory = open(context, true);
            var original = memory.Read();
            if (original > 1) throw new IOException("Hotel Architect's aging setting is not a boolean.");
            if (owned != null && original == 0) return;
            if (!currentWorld(context)) throw new InvalidOperationException("Hotel Architect's world changed before the aging edit.");
            owned = new Ownership(context, original);
            memory.Write(original, 0); // Keep the recovery capture even if a readback fails after the write.
        }
    }

    private void ClearOwnership() { owned = null; }

    private void Restore(Context? requested = null)
    {
        var capture = owned;
        if (capture == null) return;
        if (requested != null && requested != capture.Context) { ClearOwnership(); return; }
        if (!currentWorld(capture.Context)) throw new InvalidOperationException("Waiting for the same Hotel Architect world before restoring aging.");
        using var memory = open(capture.Context, true);
        var value = memory.Read();
        if (value == 0) {
            if (!currentWorld(capture.Context)) throw new InvalidOperationException("Hotel Architect's world is unavailable during aging restoration.");
            memory.Write(0, capture.Original);
        }
        ClearOwnership(); // Preserve any later game change, including a re-enabled aging setting.
    }

    public void Dispose()
    {
        lock (gate) {
            if (disposed) return;
            try { Restore(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or ArgumentException) { }
            finally {
                owned = null;
                disposed = true;
                AppDomain.CurrentDomain.ProcessExit -= onExit;
            }
        }
    }

    // Values and instruction sentinels come from read-only inspection recorded
    // in artifacts/hotel-architect/verified-build.json. The sole writable target
    // is bEnableAging, byte +1 in the active difficulty settings' static object.
    internal const long TargetRva = 0x9323F21;
    internal const uint ImageTimestamp = 2252431335;
    internal const uint ImageSize = 168026112;
    private sealed class NativeMemory : IMemory
    {
        private readonly SafeProcessHandle handle;
        private readonly nint image;
        private readonly nint target;
        private readonly bool writable;
        private static readonly object fileGate = new();
        private static (string Path, long Length, DateTime Modified, DateTime Created)? verifiedFile;
        internal NativeMemory(Context context, bool writable)
        {
            this.writable = writable;
            var expected = SafePath(context.Directory, HotelArchitectModule.Executable);
            using var process = Process.GetProcessById(context.ProcessId);
            if (process.HasExited || process.StartTime.ToUniversalTime() != context.Started
                || !string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Hotel Architect's process changed.");
            ValidateExecutable(expected, force: writable);
            image = process.MainModule!.BaseAddress;
            target = checked(image + (nint)TargetRva);
            handle = OpenProcess(writable ? 0x438u : 0x410u, false, context.ProcessId);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access Hotel Architect's aging setting."); }
            try {
                var dos = ReadBytes(image, 64);
                var peOffset = BitConverter.ToInt32(dos, 0x3c);
                if (dos[0] != 'M' || dos[1] != 'Z' || peOffset < 64 || peOffset > 1024 * 1024) throw new IOException("Invalid Hotel Architect image header.");
                var pe = ReadBytes(image + peOffset, 88);
                if (BitConverter.ToUInt32(pe, 0) != 0x4550 || BitConverter.ToUInt16(pe, 4) != 0x8664
                    || BitConverter.ToUInt32(pe, 8) != ImageTimestamp || BitConverter.ToUInt16(pe, 24) != 0x20b
                    || BitConverter.ToUInt32(pe, 80) != ImageSize || TargetRva >= ImageSize)
                    throw new IOException("The loaded Hotel Architect image does not match the inspected build.");
                CheckSentinel(0x4a11b00, "488d0519249104c3");
                CheckSentinel(0x4789490, "c6410101c3");
                CheckSentinel(0x4a13165, "803db50d910400488bf80f8402010000");
                ValidateTargetRegion();
                if (Read() > 1) throw new IOException("Unexpected Hotel Architect aging value.");
            } catch { handle.Dispose(); throw; }
        }
        private static void ValidateExecutable(string path, bool force)
        {
            lock (fileGate) {
                var file = new FileInfo(path);
                var identity = (path, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
                if (!force && verifiedFile == identity) return;
                verifiedFile = null;
                if (!string.Equals(Hash(path), HotelArchitectModule.SupportedExecutableHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsupported Hotel Architect executable. Aging was not changed.");
                verifiedFile = identity;
            }
        }
        private byte[] ReadBytes(nint address, int length)
        {
            var bytes = new byte[length];
            if (!ReadProcessMemory(handle, address, bytes, (nuint)length, out var read) || read != (nuint)length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Hotel Architect's inspected aging data.");
            return bytes;
        }
        private void CheckSentinel(long rva, string hex)
        {
            var expected = Convert.FromHexString(hex);
            if (!ReadBytes(image + (nint)rva, expected.Length).SequenceEqual(expected))
                throw new IOException("Hotel Architect's aging code differs from the inspected build.");
        }
        private void ValidateTargetRegion()
        {
            if (VirtualQueryEx(handle, target, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0
                || region.AllocationBase != image || region.State != 0x1000 || region.Type != 0x1000000
                || (region.Protect & 0x100) != 0 || (region.Protect & 0xff) is not (0x04 or 0x08)
                || target < region.BaseAddress || (nuint)(target - region.BaseAddress) >= region.RegionSize)
                throw new IOException("Hotel Architect's aging byte is not in its expected writable image data.");
        }
        public byte Read() { ValidateTargetRegion(); return ReadBytes(target, 1)[0]; }
        public void Write(byte expected, byte value)
        {
            if (!writable || expected > 1 || value > 1) throw new IOException("Invalid aging edit.");
            if (Read() != expected) throw new IOException("Hotel Architect changed its aging setting before the edit.");
            if (value == expected) return;
            if (!WriteProcessMemory(handle, target, new[] { value }, 1, out var written) || written != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot update Hotel Architect's aging setting.");
            if (Read() != value) throw new IOException("Hotel Architect did not retain the requested aging setting.");
        }
        public void Dispose() => handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion { public nint BaseAddress, AllocationBase; public uint AllocationProtect; public nuint RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MemoryRegion memory, nuint size);
}
