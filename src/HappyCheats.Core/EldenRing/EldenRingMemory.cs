using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HappyCheats.Core.EldenRing;

public sealed record EldenRingState(bool Ready, string Context, int Health = 0, int MaxHealth = 0,
    int FP = 0, int MaxFP = 0, int Stamina = 0, int MaxStamina = 0,
    int? Runes = null, bool DamageProtected = false, bool UnlimitedConsumables = false);

/// <summary>Guarded resource, rune and owned debug-flag operations for a verified offline local player.</summary>
public sealed class EldenRingMemory : IDisposable
{
    public const int MaximumRunes = 999999999;
    // Layout facts are independently described by the pinned fromsoftware-rs and Grand Archives sources.
    internal const int PlayerFlagsOffset = 0x538, PlayerGameDataOffset = 0x580,
        MainPlayerGameDataOffset = 0x8, RuneCountOffset = 0x6C;
    private const uint DamageProtectionMask = 1u << 3, ConsumablesMask = 1u << 16;
    internal interface IMemory : IDisposable
    {
        byte[] Read(long address, int length);
        void Write(long address, byte[] value);
        void CheckEnvironment();
    }
    private readonly IMemory memory;
    private readonly long image;
    private bool disposed;
    private readonly object gate = new();
    private string flagsOwner = "";
    private readonly Dictionary<uint, bool> originalFlags = [];
    internal EldenRingMemory(IMemory memory, long image) { this.memory = memory; this.image = image; }

    public static EldenRingMemory Open(int processId, DateTime started, string executablePath, EldenRingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.VerifyExecutable(executablePath);
        EnsureNoProtectedProcesses();
        var native = new NativeMemory(processId, started, executablePath);
        try { return new EldenRingMemory(native, native.Image); }
        catch { native.Dispose(); throw; }
    }

    public static void EnsureNoProtectedProcesses()
    {
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            string name;
            try { name = process.ProcessName; }
            catch (InvalidOperationException) { continue; }
            if (name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
                || name.Equals("start_protected_game", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Close the protected launcher and Easy Anti-Cheat before connecting to offline Elden Ring.");
        }
    }

    public EldenRingState Read() { lock (gate) return Capture().State; }
    private sealed record CaptureResult(EldenRingState State, long Data, long Player = 0, string PlayerContext = "");
    private CaptureResult Capture()
    {
        var capture = CaptureCore();
        if (capture.Player != 0)
        {
            // Only traverse the freshly verified current player. This identity is independent of
            // transient managers and resource modules; PlayerGameData distinguishes reused addresses.
            try { capture = capture with { PlayerContext = PlayerFlagIdentity(capture.Player) }; }
            catch (Exception error) when (OptionalTargetError(error)) { }
        }
        // Absence is not proof of replacement. Keep original-bit bookkeeping, without ever
        // dereferencing the remembered address, until a current player can be identified again.
        if (flagsOwner.Length != 0 && capture.PlayerContext.Length != 0 && flagsOwner != capture.PlayerContext)
        {
            originalFlags.Clear();
            flagsOwner = "";
        }
        if (capture.Player == 0) return capture;
        var state = capture.State;
        // New optional targets cannot veto the original resource-only controls.
        try
        {
            var flags = UInt32(capture.Player + PlayerFlagsOffset);
            state = state with { DamageProtected = (flags & DamageProtectionMask) != 0,
                UnlimitedConsumables = (flags & ConsumablesMask) != 0 };
        }
        catch (Exception error) when (OptionalTargetError(error)) { }
        if (state.Ready)
        {
            try { state = state with { Runes = RuneTarget(capture).Runes }; }
            catch (Exception error) when (OptionalTargetError(error)) { }
        }
        return capture with { State = state };
    }
    private static bool OptionalTargetError(Exception error) => error is IOException or Win32Exception or InvalidOperationException;
    private string PlayerFlagIdentity(long player)
    {
        var data = Pointer(player + PlayerGameDataOffset);
        if (data == 0) throw new IOException("Elden Ring player effect identity is unavailable. Wait for the local character to finish loading.");
        return $"{player:X}:{data:X}";
    }
    private CaptureResult CaptureCore()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        memory.CheckEnvironment();
        var game = Pointer(image + EldenRingProfile.GameRva);
        var session = Pointer(image + EldenRingProfile.SessionRva);
        if (game == 0 || session == 0) throw new IOException("Elden Ring offline state is unavailable. Wait for the main menu.");
        var flags = memory.Read(game + 0xBC8, 3);
        // is_in_online_mode, server_connection_enabled, EventWorldType. Only the first and third
        // are state predicates; the second is a capability, so both boolean values are accepted.
        if (flags[0] != 0 || flags[1] > 1 || flags[2] != 0
            || Int32(session + 0xC) != 0 || Int32(session + 0x10) != 0
            || Pointer(session + 0x78) != Pointer(session + 0x80))
            throw new IOException("Elden Ring is online or in a multiplayer session. Trainer controls require offline single-player.");
        var steam = Pointer(session + 0x60);
        if (steam == 0 || Int32(steam + 0x710 + 0x24) != 0)
            throw new IOException("Elden Ring has an active or unknown network session. Trainer writes are blocked.");
        var world = Pointer(image + EldenRingProfile.WorldRva);
        if (world == 0) return new(new(false, ""), 0);
        var player = Pointer(world + 0x1E508);
        if (player == 0) return new(new(false, ""), 0);
        // Cross-check the global local player against slot zero of the independently described player set.
        var entries = Pointer(world + 0x10EF8);
        if (entries == 0 || Pointer(entries) != player || Pointer(player) != image + EldenRingProfile.PlayerVtableRva)
            throw new IOException("Elden Ring local player identity could not be verified.");
        var modules = Pointer(player + 0x190);
        if (modules == 0) return new(new(false, ""), 0, player);
        var data = Pointer(modules);
        if (data == 0) return new(new(false, ""), 0, player);
        if (Pointer(data + 8) != player) throw new IOException("Elden Ring resource data belongs to a different character.");
        var bytes = memory.Read(data + 0x138, 0x24);
        var hp = BitConverter.ToInt32(bytes, 0); var maxHp = BitConverter.ToInt32(bytes, 4);
        var fp = BitConverter.ToInt32(bytes, 0x10); var maxFp = BitConverter.ToInt32(bytes, 0x14);
        var sp = BitConverter.ToInt32(bytes, 0x1C); var maxSp = BitConverter.ToInt32(bytes, 0x20);
        ValidateResource(hp, maxHp); ValidateResource(fp, maxFp); ValidateResource(sp, maxSp);
        var context = $"{game:X}:{session:X}:{steam:X}:{world:X}:{player:X}:{modules:X}:{data:X}";
        return new(new(hp > 0, context, hp, maxHp, fp, maxFp, sp, maxSp), data, player);
    }

    internal static void ValidateResource(int current, int maximum)
    {
        if (maximum < 1 || maximum > 100000 || current < 0 || current > maximum)
            throw new IOException("Elden Ring resource values are outside the supported range.");
    }

    public EldenRingState Refill(EldenRingState expected, bool health, bool fp, bool stamina)
    {
        lock (gate)
        {
            var capture = CheckedCapture(expected);
            if (health) RefillOne(expected, capture.Data, 0x138, s => s.Health, s => s.MaxHealth);
            if (fp) RefillOne(expected, capture.Data, 0x148, s => s.FP, s => s.MaxFP);
            if (stamina) RefillOne(expected, capture.Data, 0x154, s => s.Stamina, s => s.MaxStamina);
            return CheckedCapture(expected).State;
        }
    }

    private sealed record RuneLocation(long PlayerData, long Address, int Runes);
    private RuneLocation RuneTarget(CaptureResult capture)
    {
        if (capture.Player == 0) throw new IOException("Elden Ring rune target is unavailable. Load a local character.");
        var manager = Pointer(image + EldenRingProfile.GameDataRva);
        if (manager == 0) throw new IOException("Elden Ring GameDataMan rune target is unavailable.");
        var data = Pointer(manager + MainPlayerGameDataOffset);
        if (data == 0 || Pointer(capture.Player + PlayerGameDataOffset) != data)
            throw new IOException("Elden Ring rune data does not match the current local character.");
        // PlayerGameData::game_data_man_index and is_main_player positively identify the host slot.
        if (Int32(data + 0xD0) != 0 || memory.Read(data + 0x8F0, 1)[0] != 1)
            throw new IOException("Elden Ring rune data is not the main local player's data.");
        var runes = Int32(data + RuneCountOffset);
        if (runes is < 0 or > MaximumRunes) throw new IOException("Elden Ring rune count is outside the supported range.");
        return new(data, data + RuneCountOffset, runes);
    }

    public EldenRingState SetRunes(EldenRingState expected, int value)
    {
        if (value is < 0 or > MaximumRunes) throw new ArgumentOutOfRangeException(nameof(value), $"Runes must be between 0 and {MaximumRunes}.");
        lock (gate)
        {
            var capture = CheckedCapture(expected);
            var target = RuneTarget(capture);
            if (target.Runes == value) return capture.State;
            WriteValue(target.Address, BitConverter.GetBytes(value), () =>
            {
                var fresh = CheckedCapture(expected);
                var latest = RuneTarget(fresh);
                if (fresh.Player != capture.Player || latest.PlayerData != target.PlayerData || latest.Address != target.Address)
                    throw new IOException("Elden Ring rune target changed before the write.");
            });
            var result = CheckedCapture(expected);
            var confirmed = RuneTarget(result);
            if (confirmed.PlayerData != target.PlayerData || confirmed.Runes != value)
                throw new IOException("Elden Ring rune change was not confirmed. Refresh the current rune count before trying again.");
            return result.State with { Runes = confirmed.Runes };
        }
    }

    private static uint FlagMask(string featureId) => featureId switch
    {
        "er_damage_protection" => DamageProtectionMask,
        "er_consumables" => ConsumablesMask,
        _ => throw new ArgumentException("Unknown Elden Ring player flag.", nameof(featureId))
    };

    public EldenRingState SetPlayerFlag(EldenRingState expected, string featureId, bool enabled)
    {
        var mask = FlagMask(featureId);
        lock (gate)
        {
            var capture = enabled ? CheckedCapture(expected) : Capture();
            if (!enabled && !originalFlags.ContainsKey(mask)) return capture.State;
            var owner = capture.PlayerContext;
            if (capture.Player == 0 || owner.Length == 0)
            {
                if (!enabled) return capture.State;
                throw new IOException("Elden Ring player effect identity is unavailable. Wait for the local character to finish loading.");
            }
            var before = UInt32(capture.Player + PlayerFlagsOffset);
            if (enabled && !originalFlags.ContainsKey(mask))
            {
                flagsOwner = owner;
                originalFlags.Add(mask, (before & mask) != 0);
            }
            var set = enabled || originalFlags[mask];
            var after = set ? before | mask : before & ~mask;
            if (after != before)
            {
                WriteValue(capture.Player + PlayerFlagsOffset, BitConverter.GetBytes(after), () =>
                {
                    var fresh = enabled ? CheckedCapture(expected) : Capture();
                    if (fresh.Player != capture.Player || fresh.PlayerContext != owner || flagsOwner != owner
                        || UInt32(fresh.Player + PlayerFlagsOffset) != before)
                        throw new IOException("Elden Ring player flags changed before the write. Refresh and retry this control.");
                });
            }
            var result = Capture();
            if (result.PlayerContext == owner && result.Player == capture.Player
                && ((UInt32(result.Player + PlayerFlagsOffset) & mask) != 0) != set)
                throw new IOException("Elden Ring player flag change was not confirmed.");
            // Absence after a write is not readback confirmation. Keep the original bit until
            // this same current identity is readable again (or Capture confirms replacement).
            if (!enabled && result.PlayerContext == owner && result.Player == capture.Player)
            {
                originalFlags.Remove(mask);
                if (originalFlags.Count == 0) flagsOwner = "";
            }
            if (enabled && (!result.State.Ready || result.State.Context != expected.Context || result.PlayerContext != owner || result.Player != capture.Player
                || (UInt32(result.Player + PlayerFlagsOffset) & mask) == 0))
                throw new IOException("Elden Ring player flag activation was not confirmed.");
            return result.State;
        }
    }

    public void ReleasePlayerFlags()
    {
        lock (gate)
        {
            if (originalFlags.Count == 0) return;
            List<Exception> failures = [];
            foreach (var mask in originalFlags.Keys.ToArray())
            {
                try { SetPlayerFlag(new(false, ""), mask == DamageProtectionMask ? "er_damage_protection" : "er_consumables", false); }
                catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
                { failures.Add(error); }
            }
            if (failures.Count != 0) throw new IOException("Elden Ring player effect cleanup was not confirmed for this offline character.", new AggregateException(failures));
        }
    }

    private void WriteValue(long address, byte[] bytes, Action validate)
    {
        if (memory is NativeMemory native) native.WriteGuarded(address, bytes, validate);
        else { validate(); memory.Write(address, bytes); }
    }
    private CaptureResult CheckedCapture(EldenRingState expected)
    {
        var capture = Capture();
        if (!expected.Ready || !capture.State.Ready || expected.Context != capture.State.Context)
            throw new IOException("Elden Ring character changed before the trainer operation. Refresh before trying again.");
        return capture;
    }
    private void RefillOne(EldenRingState expected, long data, int offset, Func<EldenRingState, int> current, Func<EldenRingState, int> maximum)
    {
        // Recheck the entire offline/local context before EACH individual write; never cache a player pointer across ticks.
        var fresh = CheckedCapture(expected);
        if (fresh.Data != data) throw new IOException("Elden Ring character changed during refill.");
        var value = maximum(fresh.State);
        if (current(fresh.State) == value) return;
        if (memory is NativeMemory native)
        {
            native.WriteGuarded(data + offset, BitConverter.GetBytes(value), () =>
            {
                var last = CheckedCapture(expected);
                if (last.Data != data || maximum(last.State) != value)
                    throw new IOException("Elden Ring character changed during refill.");
            });
        }
        else memory.Write(data + offset, BitConverter.GetBytes(value));
        // Resource values may immediately decrease again in active gameplay. Validate state/ranges rather than demanding equality.
        CheckedCapture(expected);
    }
    private int Int32(long address) => BitConverter.ToInt32(memory.Read(address, 4));
    private uint UInt32(long address) => BitConverter.ToUInt32(memory.Read(address, 4));
    private long Pointer(long address)
    {
        var pointer = BitConverter.ToInt64(memory.Read(address, 8));
        if (pointer != 0 && (pointer < 0x10000 || pointer > 0x00007FFFFFFFFFFF || (pointer & 7) != 0))
            throw new IOException("Elden Ring returned an invalid memory pointer.");
        return pointer;
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            try
            {
                ReleasePlayerFlags();
                if (originalFlags.Count != 0)
                    throw new IOException("Elden Ring player effect cleanup could not be confirmed because the local player was unavailable. No saved player pointer was written.");
            }
            finally { disposed = true; memory.Dispose(); }
        }
    }

    private sealed class NativeMemory : IMemory
    {
        private readonly SafeProcessHandle handle;
        private readonly int pid;
        private readonly DateTime started;
        private readonly string executable;
        private readonly long fileLength;
        private readonly DateTime fileModified;
        private readonly int imageSize;
        private readonly List<Section> sections = [];
        private readonly List<(long Address, byte[] Bytes)> sentinels = [];
        internal long Image { get; }
        private sealed record Section(string Name, long Start, int Size, uint Flags);

        internal NativeMemory(int pid, DateTime started, string executable)
        {
            this.pid = pid; this.started = started.ToUniversalTime(); this.executable = Path.GetFullPath(executable);
            var file = new FileInfo(executable); fileLength = file.Length; fileModified = file.LastWriteTimeUtc;
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != this.started
                || !string.Equals(process.MainModule?.FileName, this.executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Elden Ring process changed.");
            Image = process.MainModule!.BaseAddress.ToInt64();
            imageSize = process.MainModule.ModuleMemorySize;
            handle = OpenProcess(0x410, false, pid);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access offline Elden Ring."); }
            try
            {
                var dos = Read(Image, 64); var offset = BitConverter.ToInt32(dos, 0x3c);
                if (dos[0] != 'M' || dos[1] != 'Z' || offset < 64 || offset > 1024 * 1024) throw new IOException("Invalid Elden Ring image.");
                var pe = Read(Image + offset, 264);
                if (BitConverter.ToUInt32(pe) != 0x4550 || BitConverter.ToUInt16(pe, 4) != 0x8664 || BitConverter.ToUInt16(pe, 24) != 0x20B
                    || BitConverter.ToInt32(pe, 80) != imageSize || imageSize < 0x3D8B000 || imageSize > 1024 * 1024 * 1024)
                    throw new IOException("Unsupported loaded Elden Ring image.");
                var count = BitConverter.ToUInt16(pe, 6); var optional = BitConverter.ToUInt16(pe, 20);
                if (count is < 1 or > 96 || optional < 240) throw new IOException("Invalid Elden Ring section table.");
                var table = Read(Image + offset + 24 + optional, count * 40);
                for (var i = 0; i < count; i++)
                {
                    var start = BitConverter.ToInt32(table, i * 40 + 12); var size = BitConverter.ToInt32(table, i * 40 + 8);
                    if (start < 0 || size < 0 || (long)start + size > imageSize) throw new IOException("Invalid Elden Ring section extent.");
                    sections.Add(new(Encoding.ASCII.GetString(table, i * 40, 8).TrimEnd('\0'), Image + start, size, BitConverter.ToUInt32(table, i * 40 + 36)));
                }
                foreach (var rva in new[] { EldenRingProfile.WorldRva, EldenRingProfile.GameRva, EldenRingProfile.SessionRva, EldenRingProfile.GameDataRva })
                    if (!sections.Any(s => (s.Flags & 0x80000000) != 0 && (s.Flags & 0x20000000) == 0 && Image + rva >= s.Start && Image + rva + 8 <= s.Start + s.Size))
                        throw new IOException("Elden Ring globals are outside verified writable image data.");
                CheckPattern(EldenRingProfile.GamePattern, EldenRingProfile.GameRva);
                CheckPattern(EldenRingProfile.DataPattern, EldenRingProfile.GameDataRva);
                CheckEnvironment();
            }
            catch { handle.Dispose(); throw; }
        }

        private void CheckPattern(string pattern, long expectedRva)
        {
            var tokens = pattern.Split(' '); var bytes = tokens.Select(t => t == "??" ? (byte?)null : Convert.ToByte(t, 16)).ToArray();
            long found = 0; var matches = 0;
            foreach (var section in sections.Where(s => s.Name == ".text" && (s.Flags & 0x20000000) != 0))
            {
                if (section.Size > 128 * 1024 * 1024) throw new IOException("Elden Ring text section is too large.");
                var data = Read(section.Start, section.Size);
                for (var i = 0; i <= data.Length - bytes.Length; i++)
                {
                    var equal = true;
                    for (var j = 0; j < bytes.Length; j++) if (bytes[j].HasValue && bytes[j] != data[i + j]) { equal = false; break; }
                    if (equal) { found = section.Start + i; matches++; }
                }
            }
            if (matches != 1) throw new IOException("Elden Ring compatibility signature is missing or ambiguous.");
            var signature = Read(found, bytes.Length);
            if (found + 7 + BitConverter.ToInt32(signature, 3) != Image + expectedRva)
                throw new IOException("Elden Ring compatibility signature resolves to an unsupported layout.");
            sentinels.Add((found, signature));
        }

        public void CheckEnvironment()
        {
            EnsureNoProtectedProcesses();
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != started
                || !string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Elden Ring process changed.");
            var file = new FileInfo(executable);
            if (file.Length != fileLength || file.LastWriteTimeUtc != fileModified) throw new IOException("Elden Ring executable changed while connected.");
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName ?? "";
                if (name.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("seamless", StringComparison.OrdinalIgnoreCase) || name.Equals("ersc.dll", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("modengine", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Protected or multiplayer-modified Elden Ring is unsupported.");
                // Only the shipped runtime DLLs are accepted from the game's directory. Proxy mod loaders are not supported.
                if (Path.GetDirectoryName(module.FileName)?.Equals(Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase) == true
                    && !new[] { "eldenring.exe", "steam_api64.dll", "oo2core_6_win64.dll", "D3D12.dll", "D3D12Core.dll" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new IOException("An unsupported Elden Ring game-folder module is loaded.");
            }
            foreach (var sentinel in sentinels)
                if (!Read(sentinel.Address, sentinel.Bytes.Length).SequenceEqual(sentinel.Bytes)) throw new IOException("Elden Ring compatibility code changed.");
            VerifyObject(EldenRingProfile.WorldRva, "WorldChrMan");
            VerifyObject(EldenRingProfile.SessionRva, "CSSessionManager");
        }

        private void VerifyObject(long rva, string type)
        {
            var obj = BitConverter.ToInt64(Read(Image + rva, 8));
            if (obj == 0) return;
            var vtable = BitConverter.ToInt64(Read(obj, 8));
            if (!InImage(vtable - 8, 16)) throw new IOException("Elden Ring object identity is invalid.");
            var locator = BitConverter.ToInt64(Read(vtable - 8, 8));
            if (!InImage(vtable, 8) || !InImage(locator, 24)) throw new IOException("Elden Ring object identity is invalid.");
            var rtti = Read(locator, 24);
            var name = Image + BitConverter.ToUInt32(rtti, 12) + 16;
            if (BitConverter.ToUInt32(rtti) != 1 || !InImage(name, 128)) throw new IOException("Elden Ring runtime type is invalid.");
            var typeName = Encoding.ASCII.GetString(Read(name, 128)).Split('\0')[0];
            if (!typeName.Contains(type + "@", StringComparison.Ordinal)) throw new IOException("Elden Ring runtime type does not match this profile.");
        }
        private bool InImage(long address, int length) => address >= Image && address <= Image + imageSize - length;
        public byte[] Read(long address, int length)
        {
            if (address < 0x10000 || length < 1 || address > 0x00007FFFFFFFFFFF - length) throw new IOException("Invalid Elden Ring memory range.");
            var bytes = new byte[length];
            if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)length, out var read) || read != (nuint)length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Elden Ring state.");
            return bytes;
        }
        public void Write(long address, byte[] value) => throw new InvalidOperationException("Elden Ring writes require a fresh character guard.");
        internal void WriteGuarded(long address, byte[] value, Action validateCharacter)
        {
            CheckEnvironment();
            if (value.Length != 4 || VirtualQueryEx(handle, (nint)address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) == 0
                || region.State != 0x1000 || region.Type != 0x20000 || (region.Protect & 0x100) != 0
                || (region.Protect & 0xFF) is not (0x04 or 0x08) || address < region.BaseAddress.ToInt64()
                || (ulong)(address - region.BaseAddress.ToInt64()) + 4 > region.RegionSize)
                throw new IOException("Elden Ring trainer target is not writable local player data.");
            validateCharacter();
            using var writer = OpenProcess(0x438, false, pid);
            if (writer.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot write the offline Elden Ring trainer target.");
            if (!GetProcessTimes(writer, out var creation, out _, out _, out _)
                || DateTime.FromFileTimeUtc(creation) != started)
                throw new IOException("Elden Ring process changed before the trainer write.");
            validateCharacter();
            if (!WriteProcessMemory(writer, (nint)address, value, 4, out var written) || written != 4)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot write the offline Elden Ring trainer target.");
        }
        public void Dispose() => handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion { public nint BaseAddress, AllocationBase; public uint AllocationProtect; public nuint RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address, out MemoryRegion memory, nuint size);
}
