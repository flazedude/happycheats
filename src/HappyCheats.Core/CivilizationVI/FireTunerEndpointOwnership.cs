using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HappyCheats.Core.CivilizationVI;

/// <summary>Identifies the local tuner listener without inspecting or injecting process memory.</summary>
internal static class FireTunerEndpointOwnership
{
    public static void RequireOwner(int processId, int port = 4318)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Civilization VI tuner ownership requires Windows.");
        if (processId <= 0 || port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(processId));
        var owners = ReadOwners(2, port, null).Concat(ReadOwners(23, port, null)).Distinct().ToArray();
        if (owners.Length != 1 || owners[0] != processId)
            throw new IOException("Civilization VI does not own the local Tuner port (4318). Enable Tuner, close other Tuner clients, and restart Civilization VI yourself before reconnecting.");
    }

    public static void RequireConnectionOwner(int processId, int clientPort, int port = 4318)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Civilization VI tuner ownership requires Windows.");
        if (processId <= 0 || port is < 1 or > 65535 || clientPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(processId));
        // FireTunerClient explicitly opens an IPv4 loopback socket. An unavailable
        // IPv6 stack must not veto an authenticated IPv4 connection.
        var owners = ReadOwners(2, port, clientPort).Distinct().ToArray();
        if (owners.Length != 1 || owners[0] != processId)
            throw new IOException("The connected Tuner socket does not belong to this Civilization VI process. No trainer code was sent.");
    }

    private static IEnumerable<int> ReadOwners(int addressFamily, int port, int? clientPort)
    {
        const uint insufficientBuffer = 122;
        var tableClass = clientPort.HasValue ? 5 : 3; // OWNER_PID_ALL or OWNER_PID_LISTENER.
        var length = 0;
        var result = GetExtendedTcpTable(null, ref length, false, addressFamily, tableClass, 0);
        if (result is not (0 or insufficientBuffer)) throw new IOException("Cannot inspect the local Civilization VI Tuner port (Windows error " + result + ").");
        for (var attempt = 0; attempt < 3; attempt++) {
            if (length is < 4 or > 16_777_216) throw new IOException("Invalid Windows TCP listener table size.");
            var buffer = new byte[length];
            result = GetExtendedTcpTable(buffer, ref length, false, addressFamily, tableClass, 0);
            if (result == insufficientBuffer) continue;
            if (result != 0) throw new IOException("Cannot inspect the local Civilization VI Tuner port (Windows error " + result + ").");
            return ParseOwners(buffer.AsSpan(0, length), addressFamily, port, clientPort);
        }
        throw new IOException("The local Tuner listener changed during inspection. Retry after Civilization VI finishes loading.");
    }

    internal static int[] ParseOwners(ReadOnlySpan<byte> table, int addressFamily, int port, int? clientPort = null)
    {
        var size = addressFamily switch { 2 => 24, 23 => 56, _ => throw new ArgumentOutOfRangeException(nameof(addressFamily)) };
        if (table.Length < 4) throw new IOException("Incomplete Windows TCP listener table.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table[..4]);
        if (count > (table.Length - 4) / size) throw new IOException("Truncated Windows TCP listener table.");
        var owners = new List<int>();
        for (var index = 0; index < count; index++) {
            var row = table.Slice(4 + index * size, size);
            var localPortOffset = addressFamily == 2 ? 8 : 20;
            if (BinaryPrimitives.ReadUInt16BigEndian(row.Slice(localPortOffset, 2)) != port) continue;
            var address = addressFamily == 2 ? row.Slice(4, 4) : row[..16];
            if (clientPort.HasValue) {
                var stateOffset = addressFamily == 2 ? 0 : 48;
                var remotePortOffset = addressFamily == 2 ? 16 : 44;
                var remoteAddress = addressFamily == 2 ? row.Slice(12, 4) : row.Slice(24, 16);
                if (BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(stateOffset, 4)) != 5
                    || BinaryPrimitives.ReadUInt16BigEndian(row.Slice(remotePortOffset, 2)) != clientPort.Value
                    || !IsLoopback(remoteAddress)) continue;
            }
            var wildcard = true;
            foreach (var octet in address) if (octet != 0) { wildcard = false; break; }
            if (!(wildcard && !clientPort.HasValue) && !IsLoopback(address)) continue;
            var ownerOffset = addressFamily == 2 ? 20 : 52;
            owners.Add(BinaryPrimitives.ReadInt32LittleEndian(row.Slice(ownerOffset, 4)));
        }
        return owners.ToArray();
    }

    private static bool IsLoopback(ReadOnlySpan<byte> address)
    {
        if (address.Length == 4) return address.SequenceEqual(new byte[] { 127, 0, 0, 1 });
        return address[..15].IndexOfAnyExcept((byte)0) == -1 && address[15] == 1
            || address[..10].IndexOfAnyExcept((byte)0) == -1 && address[10] == 255 && address[11] == 255
                && address[12..].SequenceEqual(new byte[] { 127, 0, 0, 1 });
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable([Out] byte[]? table, ref int length,
        [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);
}
