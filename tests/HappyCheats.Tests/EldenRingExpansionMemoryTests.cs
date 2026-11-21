using HappyCheats.Core.EldenRing;

namespace HappyCheats.Tests;

internal static class EldenRingExpansionMemoryTests
{
    private static int passed, failed;
    internal static int RunAll()
    {
        passed = failed = 0;
        Run("rune edits change only the verified current rune count", () =>
        {
            using var f = new Fixture();
            var result = f.Runtime.SetRunes(f.Runtime.Read(), 123456);
            Assert(result.Runes == 123456);
            Assert(f.Memory.Writes.Count == 1 && f.Memory.Writes[0].Address == Fixture.PlayerData + 0x6C);
            Assert(f.Memory.Int(Fixture.PlayerData + 0x70) == 654321 && f.Memory.Int(Fixture.PlayerData + 0x68) == 50);
            Assert(f.Runtime.SetRunes(result, 123456).Runes == 123456 && f.Memory.Writes.Count == 1);
            Assert(f.Runtime.SetRunes(result, 0).Runes == 0);
            Assert(f.Runtime.SetRunes(result, EldenRingMemory.MaximumRunes).Runes == EldenRingMemory.MaximumRunes);
        });
        Run("rune bounds reject before any memory read or write", () =>
        {
            using var f = new Fixture();
            var reads = f.Memory.Reads;
            Fails(() => f.Runtime.SetRunes(new(true, ""), -1));
            Fails(() => f.Runtime.SetRunes(new(true, ""), 1000000000));
            Assert(f.Memory.Reads == reads && f.Memory.Writes.Count == 0);
        });
        Run("invalid rune targets remain specific to rune controls", () =>
        {
            Action<Fixture>[] corruptions = [
                f => f.Memory.Ptr(Fixture.Image + EldenRingProfile.GameDataRva, 0),
                f => f.Memory.Ptr(Fixture.Manager + 8, 0),
                f => f.Memory.Ptr(Fixture.Player + 0x580, Fixture.PlayerData + 8),
                f => f.Memory.Int(Fixture.PlayerData + 0xD0, 1),
                f => f.Memory.Put(Fixture.PlayerData + 0x8F0, [0]),
                f => f.Memory.Put(Fixture.PlayerData + 0x8F0, [2]),
                f => f.Memory.Int(Fixture.PlayerData + 0x6C, -1),
                f => f.Memory.Int(Fixture.PlayerData + 0x6C, 1000000000)
            ];
            foreach (var corrupt in corruptions)
            {
                using var f = new Fixture();
                corrupt(f);
                var status = f.Runtime.Read();
                Assert(status.Ready && status.Runes == null);
                Fails(() => f.Runtime.SetRunes(status, 100));
                Assert(f.Memory.Writes.Count == 0);
                Assert(f.Runtime.Refill(status, true, false, false).Health == 100);
            }
        });
        Run("missing optional flag memory does not block old resource controls", () =>
        {
            using var f = new Fixture();
            f.Memory.Remove(Fixture.Player + 0x538);
            var state = f.Runtime.Read();
            Assert(state.Ready && !state.DamageProtected && !state.UnlimitedConsumables);
            Fails(() => f.Runtime.SetPlayerFlag(state, "er_damage_protection", true));
            Assert(f.Memory.Writes.Count == 0);
            Assert(f.Runtime.Refill(state, false, true, true).FP == 80);
        });
        Run("rune writes require fresh living-character identity and offline environment", () =>
        {
            Action<Fixture>[] changes = [
                f => f.Memory.Int(Fixture.Data + 0x138, 0),
                f => f.Memory.Ptr(Fixture.World + 0x1E508, 0),
                f => f.ChangeResourceOwner(),
                f => f.Memory.Put(Fixture.Game + 0xBC8, [1]),
                f => f.Memory.EnvironmentError = true
            ];
            foreach (var change in changes)
            {
                using var f = new Fixture();
                var before = f.Runtime.Read();
                change(f);
                Fails(() => f.Runtime.SetRunes(before, 123));
                Assert(f.Memory.Writes.Count == 0);
            }
        });
        Run("rune data replacement during operation prevents the write", () =>
        {
            using var f = new Fixture();
            var state = f.Runtime.Read();
            var checks = 0;
            f.Memory.OnCheck = () => { if (++checks == 2) f.Memory.Ptr(Fixture.Player + 0x580, Fixture.PlayerData + 8); };
            Fails(() => f.Runtime.SetRunes(state, 123));
            Assert(f.Memory.Writes.Count == 0);
        });
        Run("rune write failure never replays the action", () =>
        {
            using var f = new Fixture();
            f.Memory.WriteError = true;
            Fails(() => f.Runtime.SetRunes(f.Runtime.Read(), 123));
            f.Memory.WriteError = false;
            Assert(f.Runtime.Read().Runes == 1000 && f.Memory.Writes.Count == 0);
        });
        Run("player flags use only the two local-player bits and preserve unrelated bits", () =>
        {
            using var f = new Fixture();
            const uint original = 0xA0000020;
            f.Memory.UInt(Fixture.Player + 0x538, original);
            var state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            Assert(state.DamageProtected && !state.UnlimitedConsumables);
            state = f.Runtime.SetPlayerFlag(state, "er_consumables", true);
            Assert(state.DamageProtected && state.UnlimitedConsumables);
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == (original | 8u | 0x10000u));
            Assert(f.Memory.Writes.All(w => w.Address == Fixture.Player + 0x538 && w.Bytes.Length == 4));
            var writes = f.Memory.Writes.Count;
            f.Runtime.SetPlayerFlag(state, "er_damage_protection", true);
            Assert(f.Memory.Writes.Count == writes);
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == original);
        });
        Run("flag off preserves an originally enabled bit and external unrelated changes", () =>
        {
            using var f = new Fixture();
            f.Memory.UInt(Fixture.Player + 0x538, 8u);
            var state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            state = f.Runtime.SetPlayerFlag(state, "er_consumables", true);
            f.Memory.UInt(Fixture.Player + 0x538, 8u | 0x10000u | 0x80000000u);
            f.Runtime.SetPlayerFlag(state, "er_consumables", false);
            f.Runtime.SetPlayerFlag(state, "er_damage_protection", false);
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == (8u | 0x80000000u));
        });
        Run("off never clears an unowned game flag", () =>
        {
            using var f = new Fixture();
            f.Memory.UInt(Fixture.Player + 0x538, 8u | 0x10000u);
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", false);
            f.Runtime.SetPlayerFlag(new(false, "stale"), "er_consumables", false);
            Assert(f.Memory.Writes.Count == 0 && f.Memory.UInt(Fixture.Player + 0x538) == (8u | 0x10000u));
        });
        Run("owned flags can be removed from a dead same-owner player", () =>
        {
            using var f = new Fixture();
            var state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Memory.Int(Fixture.Data + 0x138, 0);
            state = f.Runtime.SetPlayerFlag(state, "er_damage_protection", false);
            Assert(!state.Ready && !state.DamageProtected && f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("temporary player absence retains originals without reading or writing a saved target", () =>
        {
            using var f = new Fixture();
            var state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_consumables", true);
            f.Memory.Ptr(Fixture.World + 0x1E508, 0);
            var writes = f.Memory.Writes.Count;
            f.Memory.ReadAddresses.Clear();
            Assert(!f.Runtime.Read().Ready);
            state = f.Runtime.SetPlayerFlag(state, "er_consumables", false);
            f.Runtime.ReleasePlayerFlags();
            Assert(!state.Ready && f.Memory.Writes.Count == writes);
            Assert(!f.Memory.ReadAddresses.Any(address => address >= Fixture.Player && address < Fixture.Player + 0x600));
            f.Memory.Ptr(Fixture.World + 0x1E508, Fixture.Player);
            state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_consumables", true);
            state = f.Runtime.SetPlayerFlag(state, "er_consumables", false);
            Assert(!state.UnlimitedConsumables && f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("manager replacement preserves original bits on the same player", () =>
        {
            using var f = new Fixture();
            var before = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            const long nextGame = 0xD00000, nextSession = 0xE00000, nextWorld = 0xF00000;
            f.Memory.Ptr(Fixture.Image + EldenRingProfile.GameRva, nextGame);
            f.Memory.Put(nextGame + 0xBC8, [0, 0, 0]);
            f.Memory.Ptr(Fixture.Image + EldenRingProfile.SessionRva, nextSession);
            f.Memory.Int(nextSession + 0xC, 0); f.Memory.Int(nextSession + 0x10, 0);
            f.Memory.Ptr(nextSession + 0x60, Fixture.Steam);
            f.Memory.Ptr(nextSession + 0x78, 0); f.Memory.Ptr(nextSession + 0x80, 0);
            f.Memory.Ptr(Fixture.Image + EldenRingProfile.WorldRva, nextWorld);
            f.Memory.Ptr(nextWorld + 0x1E508, Fixture.Player); f.Memory.Ptr(nextWorld + 0x10EF8, Fixture.Entries);
            var after = f.Runtime.Read();
            Assert(after.Ready && after.Context != before.Context && after.DamageProtected);
            after = f.Runtime.SetPlayerFlag(after, "er_damage_protection", true);
            after = f.Runtime.SetPlayerFlag(after, "er_damage_protection", false);
            Assert(!after.DamageProtected && f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("absence during off readback preserves cleanup ownership until confirmation", () =>
        {
            using var f = new Fixture();
            var state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Memory.OnWrite = () =>
            {
                // A concurrent game update restored the bit before the player vanished.
                f.Memory.UInt(Fixture.Player + 0x538, 8u);
                f.Memory.Ptr(Fixture.World + 0x1E508, 0);
                f.Memory.ReadAddresses.Clear();
            };
            state = f.Runtime.SetPlayerFlag(state, "er_damage_protection", false);
            Assert(!state.Ready);
            var writes = f.Memory.Writes.Count;
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.Writes.Count == writes);
            Assert(!f.Memory.ReadAddresses.Any(address => address >= Fixture.Player && address < Fixture.Player + 0x600));
            f.Memory.OnWrite = null;
            f.Memory.Ptr(Fixture.World + 0x1E508, Fixture.Player);
            state = f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            state = f.Runtime.SetPlayerFlag(state, "er_damage_protection", false);
            Assert(!state.DamageProtected && f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("same address with different PlayerGameData never receives former cleanup writes", () =>
        {
            using var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Memory.Ptr(Fixture.Player + 0x580, 0xD00000);
            var writes = f.Memory.Writes.Count;
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.Writes.Count == writes);
        });
        Run("missing flag identity stays specific to player effect controls", () =>
        {
            using var f = new Fixture();
            f.Memory.Remove(Fixture.Player + 0x580);
            var state = f.Runtime.Read();
            Assert(state.Ready);
            try
            {
                f.Runtime.SetPlayerFlag(state, "er_damage_protection", true);
                throw new Exception("Expected player effect identity failure.");
            }
            catch (IOException error) { Assert(error.Message.Contains("identity", StringComparison.OrdinalIgnoreCase)); }
            Assert(f.Memory.Writes.Count == 0);
            Assert(f.Runtime.Refill(state, true, false, false).Health == 100);
        });
        Run("resource-data replacement still cleans the same verified player flags", () =>
        {
            using var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.ChangeResourceOwner();
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("missing resource modules still permit cleanup of the verified player", () =>
        {
            using var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Memory.Ptr(Fixture.Player + 0x190, 0);
            Assert(!f.Runtime.Read().Ready);
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0);
        });
        Run("replacement PlayerIns never receives old cleanup writes", () =>
        {
            using var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            const long replacement = 0xD00000;
            f.Memory.Ptr(Fixture.World + 0x1E508, replacement);
            f.Memory.Ptr(Fixture.Entries, replacement);
            f.Memory.Ptr(replacement, Fixture.Image + EldenRingProfile.PlayerVtableRva);
            f.Memory.Ptr(replacement + 0x190, Fixture.Modules);
            f.Memory.Ptr(replacement + 0x580, 0xE00000);
            f.Memory.Ptr(Fixture.Data + 8, replacement);
            f.Memory.UInt(replacement + 0x538, 0x10000u);
            var writes = f.Memory.Writes.Count;
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.Writes.Count == writes && f.Memory.UInt(replacement + 0x538) == 0x10000u);
        });
        Run("actual cleanup write failures are reported and retain retry ownership", () =>
        {
            using var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Memory.WriteError = true;
            Fails(() => f.Runtime.ReleasePlayerFlags());
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 8u);
            f.Memory.WriteError = false;
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0u);
        });
        Run("failed activation retains bounded ownership for later cleanup", () =>
        {
            using var f = new Fixture();
            f.Memory.OnWrite = () => throw new IOException("Fixture failure after committed write.");
            Fails(() => f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_consumables", true));
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0x10000u);
            f.Memory.OnWrite = null;
            f.Runtime.ReleasePlayerFlags();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0u);
        });
        Run("concurrent unrelated flag changes reject a precomputed word", () =>
        {
            using var f = new Fixture();
            var before = f.Runtime.Read();
            var checks = 0;
            f.Memory.OnCheck = () => { if (++checks == 2) f.Memory.UInt(Fixture.Player + 0x538, 0x40u); };
            Fails(() => f.Runtime.SetPlayerFlag(before, "er_damage_protection", true));
            Assert(f.Memory.Writes.Count == 0 && f.Memory.UInt(Fixture.Player + 0x538) == 0x40u);
            f.Memory.OnCheck = null;
        });
        Run("online transition before flag write prevents activation", () =>
        {
            using var f = new Fixture();
            var before = f.Runtime.Read();
            var checks = 0;
            f.Memory.OnCheck = () => { if (++checks == 2) f.Memory.Put(Fixture.Game + 0xBC8, [1]); };
            Fails(() => f.Runtime.SetPlayerFlag(before, "er_damage_protection", true));
            Assert(f.Memory.Writes.Count == 0);
            f.Memory.OnCheck = null;
            f.Memory.Put(Fixture.Game + 0xBC8, [0]);
        });
        Run("dispose restores only owned flags and releases memory", () =>
        {
            var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_damage_protection", true);
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_consumables", true);
            f.Runtime.Dispose();
            Assert(f.Memory.UInt(Fixture.Player + 0x538) == 0 && f.Memory.Disposals == 1);
            f.Runtime.Dispose();
            Assert(f.Memory.Disposals == 1);
        });
        Run("final disposal reports unavailable cleanup and still closes memory once", () =>
        {
            var f = new Fixture();
            f.Runtime.SetPlayerFlag(f.Runtime.Read(), "er_consumables", true);
            f.Memory.Ptr(Fixture.World + 0x1E508, 0);
            f.Memory.ReadAddresses.Clear();
            try
            {
                f.Runtime.Dispose();
                throw new Exception("Expected unconfirmed cleanup report.");
            }
            catch (IOException error) { Assert(error.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)); }
            Assert(f.Memory.Disposals == 1 && f.Memory.Writes.Count == 1);
            Assert(!f.Memory.ReadAddresses.Any(address => address >= Fixture.Player && address < Fixture.Player + 0x600));
            f.Runtime.Dispose();
            Assert(f.Memory.Disposals == 1);
        });
        Run("unknown flag ids reject before touching memory", () =>
        {
            using var f = new Fixture();
            Fails(() => f.Runtime.SetPlayerFlag(new(true, ""), "all_no_damage", true));
            Assert(f.Memory.Reads == 0 && f.Memory.Writes.Count == 0);
        });
        Console.WriteLine($"Elden Ring expansion memory tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS Elden Ring expansion " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL Elden Ring expansion " + name + ": " + error); }
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Fails(Action action) { try { action(); } catch { return; } throw new Exception("Expected operation failure."); }

    private sealed class Fixture : IDisposable
    {
        internal const long Image = 0x140000000, Game = 0x100000, Session = 0x200000, Steam = 0x300000,
            World = 0x400000, Player = 0x500000, Entries = 0x600000, Modules = 0x700000, Data = 0x800000,
            Manager = 0xA00000, PlayerData = 0xB00000;
        internal readonly FakeMemory Memory = new();
        internal EldenRingMemory Runtime { get; }
        internal Fixture()
        {
            Memory.Ptr(Image + EldenRingProfile.GameRva, Game);
            Memory.Ptr(Image + EldenRingProfile.SessionRva, Session);
            Memory.Ptr(Image + EldenRingProfile.WorldRva, World);
            Memory.Put(Game + 0xBC8, [0, 0, 0]);
            Memory.Int(Session + 0xC, 0); Memory.Int(Session + 0x10, 0);
            Memory.Ptr(Session + 0x78, 0); Memory.Ptr(Session + 0x80, 0);
            Memory.Ptr(Session + 0x60, Steam); Memory.Int(Steam + 0x734, 0);
            Memory.Ptr(World + 0x1E508, Player); Memory.Ptr(World + 0x10EF8, Entries);
            Memory.Ptr(Entries, Player); Memory.Ptr(Player, Image + EldenRingProfile.PlayerVtableRva);
            Memory.Ptr(Player + 0x190, Modules); Memory.Ptr(Modules, Data);
            Resources(Data);
            Memory.UInt(Player + 0x538, 0);
            Memory.Ptr(Image + EldenRingProfile.GameDataRva, Manager);
            Memory.Ptr(Manager + 8, PlayerData); Memory.Ptr(Player + 0x580, PlayerData);
            Memory.Int(PlayerData + 0x6C, 1000); Memory.Int(PlayerData + 0x70, 654321);
            Memory.Int(PlayerData + 0x68, 50); Memory.Int(PlayerData + 0xD0, 0);
            Memory.Put(PlayerData + 0x8F0, [1]);
            Runtime = new(Memory, Image);
        }
        private void Resources(long target)
        {
            Memory.Ptr(target + 8, Player); Memory.Put(target + 0x138, new byte[0x24]);
            Memory.Int(target + 0x138, 50); Memory.Int(target + 0x13C, 100);
            Memory.Int(target + 0x148, 20); Memory.Int(target + 0x14C, 80);
            Memory.Int(target + 0x154, 30); Memory.Int(target + 0x158, 90);
        }
        internal void ChangeResourceOwner() { Memory.Ptr(Modules, 0xC00000); Resources(0xC00000); }
        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FakeMemory : EldenRingMemory.IMemory
    {
        private readonly Dictionary<long, byte> bytes = [];
        internal readonly List<(long Address, byte[] Bytes)> Writes = [];
        internal readonly List<long> ReadAddresses = [];
        internal int Reads, Disposals;
        internal bool EnvironmentError, WriteError;
        internal Action? OnCheck, OnWrite;
        internal void Put(long address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + i] = value[i]; }
        internal void Remove(long address) => bytes.Remove(address);
        internal void Int(long address, int value) => Put(address, BitConverter.GetBytes(value));
        internal int Int(long address) => BitConverter.ToInt32(Read(address, 4));
        internal void UInt(long address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal uint UInt(long address) => BitConverter.ToUInt32(Read(address, 4));
        internal void Ptr(long address, long value) => Put(address, BitConverter.GetBytes(value));
        public byte[] Read(long address, int length)
        {
            Reads++;
            ReadAddresses.Add(address);
            var result = new byte[length];
            for (var i = 0; i < length; i++)
                if (!bytes.TryGetValue(address + i, out result[i])) throw new IOException("Fixture read outside initialized memory.");
            return result;
        }
        public void Write(long address, byte[] value)
        {
            if (WriteError) throw new IOException("Fixture write failed.");
            Writes.Add((address, value.ToArray())); Put(address, value); OnWrite?.Invoke();
        }
        public void CheckEnvironment() { OnCheck?.Invoke(); if (EnvironmentError) throw new IOException("Fixture environment failure."); }
        public void Dispose() => Disposals++;
    }
}
