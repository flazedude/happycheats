using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using HappyCheats.App;
using HappyCheats.Core;
using HappyCheats.Core.Updates;

namespace HappyCheats.App.Tests;

internal static class BackgroundUpdateUiChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static object Run(string root)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Background update UI: " + message);
        }
        var originalLanguage = Localization.Language;
        var originalTheme = ThemeManager.IsLight ? "light" : "dark";
        var testRoot = Path.Combine(Path.GetTempPath(), "HappyCheats-background-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var settings = Path.Combine(testRoot, "settings.json");
        var main = new MainWindow(captureMode: true, dataRoot: root, settingsPath: settings)
        {
            ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        using var key = RSA.Create(2048);
        var package = Package();
        var manifest = new ReleaseManifest
        {
            Version = "9.0.0", PublishedAt = DateTimeOffset.UtcNow,
            PackageUrl = "https://updates.invalid/fixture.zip", PackageSize = package.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(package)),
            ChangelogEnglish = "Inert UI fixture", ChangelogChinese = "界面测试示例"
        };
        var feed = ReleaseSigning.SerializeEnvelope(ReleaseSigning.Sign(manifest, key.ExportPkcs8PrivateKeyPem()));
        var configuration = new UpdateConfiguration
        {
            FeedUrl = "https://updates.invalid/latest.json", PublicKeyPem = key.ExportSubjectPublicKeyInfoPem()
        };
        var releaseDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FixtureHandler(feed, package, releaseDownload.Task);
        using var http = new HttpClient(handler);
        BackgroundUpdateSession? session = null;
        Task? download = null;
        try
        {
            main.Show();
            main.InitializeAsync(preview: true).GetAwaiter().GetResult();
            main.SetLanguage("en", persist: false);
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(main.Dispatcher));
                session = new BackgroundUpdateSession(new UpdateClient(http, configuration), "1.0.0", Path.Combine(testRoot, "staging"));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            Set(main, "_updateConfiguration", configuration);
            Set(main, "_isolatedUpdates", false);
            Invoke(main, "AttachUpdateSession", session);
            var readyCount = 0;
            session.ReadyToInstall += (_, _) => readyCount++;

            download = session.CheckAndDownloadAsync();
            Pump(main, () => session.BytesReceived > 0 && session.State == BackgroundUpdateState.Downloading);
            Check(Get(main, "_updateWindow") == null && readyCount == 0,
                "background transfer opened a progress window before the package was ready");
            Check(handler.FeedRequests == 1 && handler.PackageRequests == 1,
                "automatic discovery and download did not use exactly one feed/package request");

            var first = Open(main);
            var received = session.BytesReceived;
            Check(first.ProgressVisibility == Visibility.Visible && first.ProgressPercent > 0,
                "the opened progress window did not show the existing download");
            CheckOwner(main, Check);
            first.Close();
            Check(Get(main, "_updateWindow") == null && session.State == BackgroundUpdateState.Downloading && !download.IsCompleted,
                "closing the progress window cancelled or replaced the active transfer");
            var reopened = Open(main);
            Check(!ReferenceEquals(first, reopened) && session.BytesReceived >= received && reopened.ProgressPercent > 0,
                "reopening lost the shared transfer or its progress");
            Check(handler.FeedRequests == 1 && handler.PackageRequests == 1,
                "reopening started another network operation");
            CloseButton(reopened);
            releaseDownload.SetResult();
            Pump(main, () => download.IsCompleted);
            download.GetAwaiter().GetResult();
            var prompt = Get(main, "_updateWindow") as UpdateWindow;
            Check(session.State == BackgroundUpdateState.Staged && session.Staged != null && readyCount == 1,
                "verified staging did not produce exactly one ready notification");
            Check(prompt != null && !ReferenceEquals(prompt, reopened) && !prompt.ShowActivated,
                "the ready update did not automatically show a non-activating prompt");
            Drain(main);
            Check(prompt!.CloseLabel == "Later" && prompt.PrimaryLabel == "Restart & install"
                && prompt.PrimaryVisibility == Visibility.Visible, "the ready prompt lacks restart and Later actions");
            CheckOwner(main, Check);
            var staged = session.Staged!;
            Check(Directory.Exists(staged.PackageDirectory), "the ready package was not retained");
            CloseButton(prompt);
            Check(Get(main, "_updateWindow") == null && ReferenceEquals(staged, session.Staged)
                && Directory.Exists(staged.PackageDirectory), "Later discarded the downloaded package");
            session.CheckAndDownloadAsync().GetAwaiter().GetResult();
            Drain(main);
            Check(readyCount == 1 && Get(main, "_updateWindow") == null && handler.PackageRequests == 1,
                "a later background check repeated the prompt or downloaded the staged package again");
            var later = Open(main);
            Check(later.PrimaryLabel == "Restart & install" && ReferenceEquals(staged, session.Staged),
                "reopening after Later lost the ready installation");
            later.WindowState = WindowState.Minimized;
            Invoke(main, "ShowUpdates", false);
            Drain(main);
            Check(later.WindowState != WindowState.Minimized && !later.ShowActivated
                && ReferenceEquals(later, Get(main, "_updateWindow")) && ReferenceEquals(staged, session.Staged),
                $"a ready notification did not restore the existing minimized window without activating it (state={later.WindowState}, activate={later.ShowActivated}, sameWindow={ReferenceEquals(later, Get(main, "_updateWindow"))}, sameStage={ReferenceEquals(staged, session.Staged)})");

            // Replace the catalog with an inert module before invoking the real install preparation.
            // The running-game guard must stop before any helper process can start.
            var module = new RunningGameModule();
            main.Games.Clear();
            main.Games.Add(new GameItem(module) { Installation = module.Installation, Status = TrainerStatus.Offline("Inert fixture") });
            main.SelectGame(module.Definition.Id);
            var install = (Task)Invoke(main, "InstallUpdateAsync")!;
            Pump(main, () => install.IsCompleted);
            install.GetAwaiter().GetResult();
            Check(session.State == BackgroundUpdateState.Failed && session.Error?.Message.Contains("Running fixture", StringComparison.Ordinal) == true,
                "installation did not explain which running game prevented restart");
            Check(module.DiscoveryCalls == 1 && ReferenceEquals(staged, session.Staged)
                && !Directory.Exists(Path.Combine(staged.WorkDirectory, "runner")),
                "running-game refusal discarded the package or started the updater helper");
            Check(!(bool)Get(main, "_installingUpdate")! && !(bool)Get(main, "_updateHandoff")!,
                "failed installation retained its busy or handoff state");
            CheckOwner(main, Check);
            Check(later.CanClose && later.ErrorDetails.Contains("Running fixture", StringComparison.Ordinal),
                "the progress window did not recover after failed install preparation");
            CloseButton(later);
            Check(!File.Exists(settings), "tests wrote user settings");
            return new
            {
                Screen = "background-updates", Assertions = assertions, InMemorySignedFixture = true,
                DownloadWithoutWindow = true, CloseAndReopenRetainsProgress = true, ReadyPromptOnce = true,
                LaterRetainsPackage = true, RunningGameBlocksInstall = true, CheatWindowRemainsEnabled = true,
                NoExternalNetworkOrGameActions = true
            };
        }
        finally
        {
            session?.Dispose();
            releaseDownload.TrySetResult();
            if (download != null) Pump(main, () => download.IsCompleted);
            foreach (var window in main.OwnedWindows.OfType<UpdateWindow>().ToArray()) window.Close();
            main.Close();
            Localization.Language = originalLanguage;
            ThemeManager.Apply(originalTheme);
            var resolved = Path.GetFullPath(testRoot);
            if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("HappyCheats-background-ui-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe UI fixture cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static void CheckOwner(MainWindow main, Action<bool, string> check) => check(main.CanManage
        && ((ListBox)main.FindName("Library")).IsEnabled && IsWindowEnabled(new WindowInteropHelper(main).Handle),
        "the cheat window or library became disabled during background update activity");
    private static UpdateWindow Open(MainWindow main)
    {
        Invoke(main, "ShowUpdates", true);
        Drain(main);
        return (UpdateWindow)Get(main, "_updateWindow")!;
    }
    private static void CloseButton(UpdateWindow window)
        => typeof(UpdateWindow).GetMethod("CloseClicked", Private)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
    private static object? Get(MainWindow main, string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(main);
    private static void Set(MainWindow main, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(main, value);
    private static object? Invoke(MainWindow main, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, args);
    private static void Drain(MainWindow main) => main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Pump(MainWindow main, Func<bool> done)
    {
        if (done()) { Drain(main); return; }
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, main.Dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (done() || elapsed.Elapsed > TimeSpan.FromSeconds(15)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!done()) throw new TimeoutException("Background update UI fixture did not finish in time.");
        Drain(main);
    }

    private static byte[] Package()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var path in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json",
            "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json", "payload/fixture/package.json" })
        {
            using var entry = archive.CreateEntry("HappyCheats/" + path).Open();
            entry.Write(Encoding.UTF8.GetBytes("Inert fixture " + path));
        }
        return output.ToArray();
    }

    private sealed class FixtureHandler(byte[] feed, byte[] package, Task releaseDownload) : HttpMessageHandler
    {
        public int FeedRequests { get; private set; }
        public int PackageRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            HttpContent content;
            if (request.RequestUri!.AbsolutePath == "/latest.json") { FeedRequests++; content = new ByteArrayContent(feed); }
            else if (request.RequestUri.AbsolutePath == "/fixture.zip") { PackageRequests++; content = new StreamContent(new PausedStream(package, releaseDownload)); }
            else throw new InvalidOperationException("Unexpected fixture URL.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content });
        }
    }
    private sealed class PausedStream(byte[] bytes, Task releaseDownload) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position > 0) await releaseDownload.WaitAsync(token);
            return await base.ReadAsync(buffer[..Math.Min(64, buffer.Length)], token);
        }
    }
    private sealed class RunningGameModule : IGameModule
    {
        public GameDefinition Definition { get; } = new("running-update-fixture", "Running fixture", "Fixture", "0", "UF", "Inert update fixture", Array.Empty<FeatureDefinition>());
        public IReadOnlyList<string> GameExecutables => ["Fixture.exe"];
        public GameInstallation Installation { get; } = new("C:/HappyCheats-Inert-Update-Fixture", "fixture", true, true, true, true, "Inert running game");
        public int DiscoveryCalls { get; private set; }
        public Task<GameInstallation> DiscoverAsync(string? directory = null) { DiscoveryCalls++; return Task.FromResult(Installation); }
        public Task<TrainerStatus> ReadStatusAsync(GameInstallation installation) => throw new InvalidOperationException("Unexpected fixture status action.");
        public Task SendAsync(GameInstallation installation, TrainerStatus status, string featureId, bool? enabled = null) => throw new InvalidOperationException("Unexpected fixture command.");
        public Task InstallAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Unexpected fixture installation.");
        public Task RemoveAsync(GameInstallation installation, IProgress<string>? progress = null) => throw new InvalidOperationException("Unexpected fixture removal.");
        public Task LaunchAsync(GameInstallation installation) => throw new InvalidOperationException("Unexpected fixture launch.");
    }
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
}
