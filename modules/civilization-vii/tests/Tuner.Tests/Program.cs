using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyCheats.Core.CivilizationVII;

internal static class Program
{
    private static int passed, failed;
    private static async Task<int> Main()
    {
        await Run("connect sends no command before ownership check; VII has no VI handshake", async () => {
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var checkedConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observedSilence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var fixture = new Fixture(async stream => {
                connected.SetResult();
                await checkedConnection.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert(!stream.DataAvailable);
                observedSilence.SetResult();
                var command = await Read(stream);
                Assert(command.Tag == 3 && command.Payload.StartsWith("CMD:65535:"));
                await Send(stream, 3, Success(command.Payload) + "ready");
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            await connected.Task;
            Assert(client.IsConnected && client.LocalPort > 0);
            if (OperatingSystem.IsWindows()) Civ7EndpointOwnership.RequireConnectionOwner(Environment.ProcessId, client.LocalPort, fixture.Port);
            checkedConnection.SetResult();
            await observedSilence.Task;
            Assert(await client.ExecuteAsync("'ready'") == "ready");
            await fixture.Completion;
        });
        await Run("fragmented UTF-8 raw results and fresh markers ignore logs stale output and wrong tags", async () => {
            const string script = "globalThis.fixture = '中文';\n({text: \"quoted \\\" value\", amount: 2n})";
            await using var fixture = new Fixture(async stream => {
                var first = await Read(stream);
                Assert(first.Tag == 3 && first.Payload.StartsWith("CMD:65535:"));
                var encoded = Regex.Match(first.Payload, @"\(0,eval\)\((?<value>""(?:\\.|[^""\\])*"")\);").Groups["value"].Value;
                Assert(JsonSerializer.Deserialize<string>(encoded) == script);
                Assert(first.Payload.Contains("typeof v==='bigint'?v.toString():v"));
                await Send(stream, 3, "unsolicited log");
                await Send(stream, 1, "O\0InGame: unsolicited native logging");
                await Send(stream, 1, Success(first.Payload) + "wrong tag");
                await Send(stream, 3, "__HC_C7_00000000000000000000000000000000_OK:stale");
                await Send(stream, 3, Success(first.Payload) + "中文 result", fragment: true);
                var second = await Read(stream);
                Assert(Success(second.Payload) != Success(first.Payload));
                await Send(stream, 3, Success(first.Payload) + "stale prior result");
                await Send(stream, 3, Success(second.Payload) + "second");
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            Assert(await client.ExecuteAsync(script) == "中文 result");
            Assert(await client.ExecuteAsync("2") == "second");
            await fixture.Completion;
        });
        await Run("JavaScript errors fail and discard the socket", async () => {
            await using var fixture = new Fixture(async stream => {
                var command = await Read(stream);
                await Send(stream, 3, Error(command.Payload) + "TypeError: fixture failure");
                await ExpectClosed(stream);
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            await Fails(() => client.ExecuteAsync("throw new TypeError('fixture failure')"), "fixture failure");
            Assert(!client.IsConnected);
            await fixture.Completion;
        });
        await Run("concurrent commands serialize without cross-attributing responses", async () => {
            var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var fixture = new Fixture(async stream => {
                var first = await Read(stream);
                firstReceived.SetResult();
                await secondQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(50);
                Assert(!stream.DataAvailable);
                await Send(stream, 3, Success(first.Payload) + "first");
                var second = await Read(stream);
                Assert(Success(first.Payload) != Success(second.Payload));
                await Send(stream, 3, Success(second.Payload) + "second");
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            var first = client.ExecuteAsync("'first'");
            await firstReceived.Task;
            var second = client.ExecuteAsync("'second'");
            secondQueued.SetResult();
            Assert(await first == "first" && await second == "second");
            await fixture.Completion;
        });
        await Run("timeout never replays a mutation or silently reconnects", async () => {
            var commands = 0;
            await using var fixture = new Fixture(async stream => {
                _ = await Read(stream);
                Interlocked.Increment(ref commands);
                await ExpectClosed(stream);
            });
            await using var client = new Civ7TunerClient(fixture.Port, TimeSpan.FromMilliseconds(400));
            await client.ConnectAsync();
            await Fails(() => client.ExecuteAsync("MutateOnce()"), "outcome is unconfirmed");
            Assert(!client.IsConnected && commands == 1);
            await Fails(() => client.ExecuteAsync("MutateOnce()"), "Connect and authenticate");
            Assert(commands == 1);
            await fixture.Completion;
        });
        await Run("closed peer cannot confirm a mutation", async () => {
            await using var fixture = new Fixture(async stream => { _ = await Read(stream); });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            await Fails(() => client.ExecuteAsync("MutateOnce()"));
            Assert(!client.IsConnected);
            await fixture.Completion;
        });
        await Run("zero oversized unterminated embedded-null and invalid UTF-8 frames are rejected", async () => {
            foreach (var invalid in new[] { "zero", "large", "unterminated", "embedded-null", "utf8" }) {
                await using var fixture = new Fixture(async stream => {
                    var command = await Read(stream);
                    var bytes = invalid switch {
                        "unterminated" => new byte[] { 97, 98 },
                        "embedded-null" => Encoding.UTF8.GetBytes(Success(command.Payload) + "a\0b\0"),
                        "utf8" => new byte[] { 0xFF, 0 },
                        _ => Array.Empty<byte>()
                    };
                    var header = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(header, invalid == "large" ? 2_000_000u : (uint)bytes.Length);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 3);
                    await stream.WriteAsync(header);
                    if (bytes.Length > 0) await stream.WriteAsync(bytes);
                    await ExpectClosed(stream);
                });
                await using var client = new Civ7TunerClient(fixture.Port);
                await client.ConnectAsync();
                await Fails(() => client.ExecuteAsync("'fixture'"));
                Assert(!client.IsConnected);
                await fixture.Completion;
            }
        });
        await Run("cancelled partial frame discards socket", async () => {
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var fixture = new Fixture(async stream => {
                _ = await Read(stream);
                await stream.WriteAsync(new byte[] { 10, 0, 0 });
                received.SetResult();
                await ExpectClosed(stream);
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            using var cancellation = new CancellationTokenSource();
            var command = client.ExecuteAsync("'fixture'", cancellation.Token);
            await received.Task;
            cancellation.Cancel();
            try { await command; throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert(!client.IsConnected);
            await fixture.Completion;
        });
        await Run("cancellation while queued does not reset another command's connection", async () => {
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var fixture = new Fixture(async stream => {
                var first = await Read(stream);
                received.SetResult();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert(!stream.DataAvailable);
                await Send(stream, 3, Success(first.Payload) + "completed");
            });
            await using var client = new Civ7TunerClient(fixture.Port);
            await client.ConnectAsync();
            var first = client.ExecuteAsync("'first'");
            await received.Task;
            using var cancellation = new CancellationTokenSource();
            var queued = client.ExecuteAsync("'never sent'", cancellation.Token);
            cancellation.Cancel();
            try { await queued; throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
            Assert(client.IsConnected);
            cancelled.SetResult();
            Assert(await first == "completed");
            await fixture.Completion;
        });
        await Run("unsolicited message count and aggregate bytes are bounded", async () => {
            foreach (var oversizedBytes in new[] { false, true }) {
                await using var fixture = new Fixture(async stream => {
                    _ = await Read(stream);
                    var payload = oversizedBytes ? new string('x', 900_000) : "unsolicited";
                    for (var index = 0; index < (oversizedBytes ? 5 : 512); index++) await Send(stream, 3, payload);
                    await ExpectClosed(stream);
                });
                await using var client = new Civ7TunerClient(fixture.Port);
                await client.ConnectAsync();
                await Fails(() => client.ExecuteAsync("'fixture'"), oversizedBytes ? "too much" : "too many");
                Assert(!client.IsConnected);
                await fixture.Completion;
            }
        });
        await Run("invalid commands and invalid connection settings fail locally", async () => {
            foreach (var port in new[] { 0, 65536 }) Throws<ArgumentOutOfRangeException>(() => new Civ7TunerClient(port));
            foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(31) }) Throws<ArgumentOutOfRangeException>(() => new Civ7TunerClient(timeout: timeout));
            await using var client = new Civ7TunerClient();
            foreach (var code in new[] { "", "  ", "x\0y", new string('x', 131_073) }) Throws<ArgumentException>(() => client.ExecuteAsync(code));
            await Fails(() => client.ExecuteAsync("'fixture'"), "Connect and authenticate");
        });
        await Run("endpoint owner parsing excludes remote or unrelated connections", () => {
            var table = new byte[4 + 24 * 3];
            BinaryPrimitives.WriteUInt32LittleEndian(table, 3);
            WriteConnection(table.AsSpan(4, 24), 127, 4318, 1234, 500);
            WriteConnection(table.AsSpan(28, 24), 192, 4318, 1234, 600);
            WriteConnection(table.AsSpan(52, 24), 127, 4318, 1235, 700);
            Assert(Civ7EndpointOwnership.ParseOwners(table, 2, 4318, 1234).SequenceEqual(new[] { 500 }));
            Throws<IOException>(() => Civ7EndpointOwnership.ParseOwners(table.AsSpan(0, 8), 2, 4318));
            return Task.CompletedTask;
        });
        Console.WriteLine($"Civilization VII Tuner tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void WriteConnection(Span<byte> row, byte remotePrefix, int port, int clientPort, int owner)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(row, 5);
        row[4] = 127; row[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(row.Slice(8, 2), (ushort)port);
        row[12] = remotePrefix; row[15] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(row.Slice(16, 2), (ushort)clientPort);
        BinaryPrimitives.WriteInt32LittleEndian(row.Slice(20, 4), owner);
    }
    private static string Success(string command) => Marker(command, "OK");
    private static string Error(string command) => Marker(command, "ERROR");
    private static string Marker(string command, string kind)
    {
        var marker = Regex.Match(command, "__HC_C7_[a-f0-9]{32}_" + kind + ":").Value;
        Assert(marker.Length > 0);
        return marker;
    }
    private static async Task<(int Tag, string Payload)> Read(NetworkStream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, deadline.Token);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        Assert(size is > 0 and <= 1_048_576);
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, deadline.Token);
        Assert(bytes[^1] == 0);
        return (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)), new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - 1));
    }
    private static async Task Send(NetworkStream stream, int tag, string payload, bool fragment = false)
    {
        var data = Encoding.UTF8.GetBytes(payload + '\0');
        var frame = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), tag);
        data.CopyTo(frame, 8);
        if (!fragment) { await stream.WriteAsync(frame); return; }
        for (var offset = 0; offset < frame.Length; offset += 3)
            await stream.WriteAsync(frame.AsMemory(offset, Math.Min(3, frame.Length - offset)));
    }
    private static async Task ExpectClosed(NetworkStream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert(await stream.ReadAsync(new byte[1], deadline.Token) == 0);
    }
    private static async Task Run(string name, Func<Task> test)
    {
        try { await test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL " + name + ": " + exception); }
    }
    private static async Task Fails(Func<Task> action, string? expectedMessage = null)
    {
        try { await action(); }
        catch (IOException exception) { if (expectedMessage != null) Assert(exception.Message.Contains(expectedMessage)); return; }
        throw new Exception("Expected an IOException.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public int Port { get; }
        public Task Completion { get; }
        public Fixture(Func<NetworkStream, Task> handler)
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Completion = Task.Run(async () => {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                client.NoDelay = true;
                await handler(client.GetStream());
            });
        }
        public async ValueTask DisposeAsync() { listener.Stop(); await Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
}
