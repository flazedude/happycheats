using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using HappyCheats.Core.CivilizationVI;

internal static class Program
{
    private static int failures;
    private static async Task<int> Main()
    {
        await Run("fragmented frames, unsolicited output, exact context and refreshed IDs", async () => {
            await using var fixture = new Fixture(async stream => {
                await Handshake(stream);
                await Expect(stream, 4, "LSQ:");
                await Send(stream, 4, "L\0" + "8\0GameCore_Tuner\0" + "15\0InGame", true);
                var command = await Read(stream);
                Assert(command.Tag == 3 && command.Payload.StartsWith("CMD:15:") && command.Payload.Contains("print('reply|good')"));
                await Send(stream, 3, "");
                await Send(stream, 1, "O\0GameCore_Tuner: reply|wrong-context");
                await Send(stream, 1, "O\0InGame: stale|old");
                await Send(stream, 1, "O\0InGame: reply|good", true);
                await Done(stream, command.Payload);
                await Expect(stream, 4, "LSQ:");
                await Send(stream, 4, "8\0GameCore_Tuner\0" + "16\0InGame");
                var second = await Read(stream);
                Assert(second.Payload.StartsWith("CMD:16:"));
                await Send(stream, 1, "O\0InGame: next|confirmed");
                await Done(stream, second.Payload);
            });
            await using var client = new FireTunerClient(fixture.Port);
            await client.ConnectAsync();
            Assert(client.IsConnected && client.Contexts[2] == "InGame" && client.AppIdentity.Contains("Civilization VI"));
            Assert(await client.ExecuteAsync("InGame", "print('reply|good')", "reply|") == "reply|good");
            Assert(await client.ExecuteAsync("InGame", "print('next|confirmed')", "next|") == "next|confirmed");
            await fixture.Completion;
        });
        await Run("Lua errors fail even after a matching print", async () => {
            await using var fixture = new Fixture(async stream => {
                await ReadyCommand(stream, async command => {
                    await Send(stream, 1, "O\0InGame: reply|partial");
                    var marker = Regex.Match(command, "__HC_ERROR_[a-f0-9]{32}:").Value;
                    await Send(stream, 1, "O\0InGame: " + marker + "test failure");
                    await Done(stream, command);
                });
            });
            await using var client = new FireTunerClient(fixture.Port);
            await Fails(() => client.ExecuteAsync("InGame", "error('test failure')", "reply|"), "test failure");
            Assert(!client.IsConnected);
            await fixture.Completion;
        });
        await Run("APP and LSQ errors fail before accepting handshake replies", async () => {
            foreach (var failIdentity in new[] { true, false }) {
                await using var fixture = new Fixture(async stream => {
                    await Expect(stream, 4, "APP:");
                    if (!failIdentity) {
                        await Send(stream, 4, "APP:Civilization VI");
                        await Expect(stream, 4, "LSQ:");
                    }
                    await Send(stream, 4, "ERR:handshake test failure");
                    var next = new byte[1];
                    Assert(await stream.ReadAsync(next) == 0);
                });
                await using var client = new FireTunerClient(fixture.Port);
                await Fails(() => client.ConnectAsync(), "handshake failed");
                Assert(!client.IsConnected && client.AppIdentity == "" && client.Contexts.Count == 0);
                await fixture.Completion;
            }
        });
        await Run("interleaved output and command ACKs cannot replace context refresh replies", async () => {
            await using var fixture = new Fixture(async stream => {
                await Handshake(stream);
                await Expect(stream, 4, "LSQ:");
                await Send(stream, 3, "");
                await Send(stream, 4, "O\0InGame: stale output");
                await Send(stream, 1, "O\0GameCore_Tuner: stale output");
                await Send(stream, 4, "5\0GameCore_Tuner\0" + "9\0InGame");
                var command = await Read(stream);
                Assert(command.Tag == 3 && command.Payload.StartsWith("CMD:9:"));
                await Send(stream, 1, "O\0InGame: reply|refreshed");
                await Done(stream, command.Payload);
            });
            await using var client = new FireTunerClient(fixture.Port);
            Assert(await client.ExecuteAsync("InGame", "print('reply|refreshed')", "reply|") == "reply|refreshed");
            await fixture.Completion;
        });
        await Run("missing and duplicate acknowledgements are rejected", async () => {
            foreach (var duplicate in new[] { false, true }) {
                await using var fixture = new Fixture(stream => ReadyCommand(stream, async command => {
                    if (duplicate) {
                        await Send(stream, 1, "O\0InGame: reply|one");
                        await Send(stream, 1, "O\0InGame: reply|two");
                    }
                    await Done(stream, command);
                }));
                await using var client = new FireTunerClient(fixture.Port);
                await Fails(() => client.ExecuteAsync("InGame", "print('other')", "reply|"));
                Assert(!client.IsConnected);
                await fixture.Completion;
            }
        });
        await Run("timeout closes socket without replaying a mutation", async () => {
            var commands = 0;
            await using var fixture = new Fixture(stream => ReadyCommand(stream, async command => {
                Interlocked.Increment(ref commands);
                var next = new byte[1];
                Assert(await stream.ReadAsync(next) == 0);
            }));
            await using var client = new FireTunerClient(fixture.Port, TimeSpan.FromMilliseconds(500));
            await Fails(() => client.ExecuteAsync("InGame", "Mutation()", "reply|"), "timed out");
            Assert(!client.IsConnected && commands == 1);
            await fixture.Completion;
        });
        await Run("oversized and unterminated frames are rejected before interpreting content", async () => {
            foreach (var oversized in new[] { false, true }) {
                await using var fixture = new Fixture(async stream => {
                    await Expect(stream, 4, "APP:");
                    var header = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(header, oversized ? 2_000_000u : 3u);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 4);
                    await stream.WriteAsync(header);
                    if (!oversized) await stream.WriteAsync("bad"u8.ToArray());
                });
                await using var client = new FireTunerClient(fixture.Port);
                await Fails(() => client.ConnectAsync());
                Assert(!client.IsConnected);
                await fixture.Completion;
            }
        });
        await Run("missing or ambiguous context prevents command submission", async () => {
            foreach (var contexts in new[] { "1\0GameCore_Tuner", "2\0InGame\0" + "3\0InGame" }) {
                await using var fixture = new Fixture(async stream => {
                    await Handshake(stream);
                    await Expect(stream, 4, "LSQ:");
                    await Send(stream, 4, contexts);
                    var next = new byte[1];
                    Assert(await stream.ReadAsync(next) == 0);
                });
                await using var client = new FireTunerClient(fixture.Port);
                await Fails(() => client.ExecuteAsync("InGame", "Mutation()", "reply|"), "unavailable or ambiguous");
                await fixture.Completion;
            }
        });
        await Run("cancelled partial frame discards connection", async () => {
            await using var fixture = new Fixture(async stream => {
                await Expect(stream, 4, "APP:");
                await stream.WriteAsync(new byte[] { 10, 0, 0 });
                var next = new byte[1];
                Assert(await stream.ReadAsync(next) == 0);
            });
            await using var client = new FireTunerClient(fixture.Port);
            using var cancellation = new CancellationTokenSource(250);
            try { await client.ConnectAsync(cancellation.Token); throw new Exception("Expected cancellation."); }
            catch (OperationCanceledException) { }
            Assert(!client.IsConnected);
            await fixture.Completion;
        });
        Console.WriteLine($"FireTuner transport tests: {failures} failures.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task ReadyCommand(NetworkStream stream, Func<string, Task> response)
    {
        await Handshake(stream);
        await Expect(stream, 4, "LSQ:");
        await Send(stream, 4, "1\0GameCore_Tuner\0" + "2\0InGame");
        var command = await Read(stream);
        Assert(command.Tag == 3 && command.Payload.StartsWith("CMD:2:"));
        await response(command.Payload);
    }
    private static async Task Handshake(NetworkStream stream)
    {
        await Expect(stream, 4, "APP:");
        await Send(stream, 1, "O\0InGame: unsolicited logging");
        await Send(stream, 4, "APP:Civilization VI", true);
        await Expect(stream, 4, "LSQ:");
        await Send(stream, 4, "1\0GameCore_Tuner\0" + "2\0InGame");
    }
    private static Task Done(NetworkStream stream, string command) => Send(stream, 1,
        "O\0InGame: " + Regex.Match(command, "__HC_DONE_[a-f0-9]{32}").Value);
    private static async Task Expect(NetworkStream stream, int tag, string payload)
    {
        var frame = await Read(stream);
        Assert(frame.Tag == tag && frame.Payload == payload);
    }
    private static async Task<(int Tag, string Payload)> Read(NetworkStream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, deadline.Token);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        Assert(size is > 0 and < 1_048_576);
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, deadline.Token);
        Assert(bytes[^1] == 0);
        return (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)), Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1));
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
    private static async Task Run(string name, Func<Task> test)
    {
        try { await test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.WriteLine("FAIL " + name + ": " + exception); }
    }
    private static async Task Fails(Func<Task> action, string? expectedMessage = null)
    {
        try { await action(); }
        catch (IOException exception) { if (expectedMessage != null) Assert(exception.Message.Contains(expectedMessage)); return; }
        throw new Exception("Expected an IOException.");
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
