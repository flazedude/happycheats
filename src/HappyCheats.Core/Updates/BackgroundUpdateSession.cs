using System.ComponentModel;

namespace HappyCheats.Core.Updates;

public enum BackgroundUpdateState { Ready, Checking, Latest, Downloading, Staged, Installing, Failed }

/// <summary>Owns an update independently of any progress window, for one client lifetime.</summary>
public sealed class BackgroundUpdateSession : INotifyPropertyChanged, IDisposable
{
    private readonly UpdateClient client;
    private readonly string currentVersion;
    private readonly string stagingRoot;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private Task? operation;
    private bool disposed, preserveStaging;

    public BackgroundUpdateSession(UpdateClient client, string currentVersion, string stagingRoot)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        _ = ReleaseVersion.Parse(currentVersion);
        this.currentVersion = currentVersion;
        this.stagingRoot = Path.GetFullPath(stagingRoot);
    }

    public BackgroundUpdateState State { get; private set; } = BackgroundUpdateState.Ready;
    public ReleaseManifest? Release { get; private set; }
    public StagedUpdate? Staged { get; private set; }
    public Exception? Error { get; private set; }
    public long BytesReceived { get; private set; }
    public long TotalBytes { get; private set; }
    public bool IsWorking => State is BackgroundUpdateState.Checking or BackgroundUpdateState.Downloading or BackgroundUpdateState.Installing;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ReadyToInstall;

    public Task CheckAndDownloadAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (disposed || Staged != null) return Task.CompletedTask;
            if (operation is { IsCompleted: false }) return operation;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
        }
        // Keep state notifications and the ready prompt on the constructing UI context.
        if (context != null && !ReferenceEquals(SynchronizationContext.Current, context))
            context.Post(_ => { _ = RunAsync(completion); }, null);
        else _ = RunAsync(completion);
        return completion.Task;
    }

    private async Task RunAsync(TaskCompletionSource completion)
    {
        StagedUpdate? downloaded = null;
        try
        {
            lock (gate)
            {
                if (disposed) return;
                Error = null;
                BytesReceived = TotalBytes = 0;
                State = BackgroundUpdateState.Checking;
                Notify();
            }
            using (var check = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                check.CancelAfter(client.CheckTimeout);
                var release = await client.CheckAsync(currentVersion, check.Token);
                lock (gate)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    Release = release;
                    State = release == null ? BackgroundUpdateState.Latest : BackgroundUpdateState.Downloading;
                    TotalBytes = release?.PackageSize ?? 0;
                    Notify();
                    if (release == null) return;
                }
            }
            using var download = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            download.CancelAfter(TimeSpan.FromMinutes(30));
            var progress = new SessionProgress(value => ReportProgress(value));
            downloaded = await client.DownloadAndStageAsync(Release!, stagingRoot, progress, download.Token);
            lock (gate)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                Staged = downloaded;
                downloaded = null;
                BytesReceived = TotalBytes = Release!.PackageSize;
                State = BackgroundUpdateState.Staged;
                Notify();
                if (!disposed) ReadyToInstall?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception error)
        {
            lock (gate)
            {
                if (!disposed)
                {
                    Error = error is OperationCanceledException
                        ? new TimeoutException("The update took too long to finish. Check your connection and retry.", error)
                        : error;
                    State = BackgroundUpdateState.Failed;
                    Notify();
                }
            }
        }
        finally
        {
            if (downloaded != null) DeleteOwnedStage(downloaded);
            lock (gate) { if (disposed) lifetime.Dispose(); }
            completion.TrySetResult();
        }
    }

    private void ReportProgress(UpdateProgress value)
    {
        void Apply()
        {
            lock (gate)
            {
                if (disposed || State != BackgroundUpdateState.Downloading) return;
                BytesReceived = value.BytesReceived;
                TotalBytes = value.TotalBytes;
                Notify();
            }
        }
        if (context != null && !ReferenceEquals(SynchronizationContext.Current, context)) context.Post(_ => Apply(), null);
        else Apply();
    }

    public StagedUpdate BeginInstall()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Staged == null || IsWorking) throw new InvalidOperationException("The update is not ready to install.");
            Error = null;
            State = BackgroundUpdateState.Installing;
            var staged = Staged;
            Notify();
            return staged;
        }
    }

    public void InstallFailed(Exception error, bool discardStaged)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (gate)
        {
            if (disposed) return;
            if (discardStaged && Staged != null)
            {
                DeleteOwnedStage(Staged);
                Staged = null;
            }
            Error = error;
            State = BackgroundUpdateState.Failed;
            Notify();
        }
    }

    public void PreserveForHandoff()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Staged == null || State != BackgroundUpdateState.Installing)
                throw new InvalidOperationException("The update has not begun installation.");
            preserveStaging = true;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            if (!preserveStaging && Staged != null) DeleteOwnedStage(Staged);
            if (!preserveStaging) Staged = null;
            if (operation == null || operation.IsCompleted) lifetime.Dispose();
        }
    }

    private void DeleteOwnedStage(StagedUpdate staged)
    {
        try
        {
            var work = Path.GetFullPath(staged.WorkDirectory);
            if (!string.Equals(Path.GetDirectoryName(work), Path.TrimEndingDirectorySeparator(stagingRoot), StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(work), "N", out _) || !Directory.Exists(work)
                || (File.GetAttributes(work) & FileAttributes.ReparsePoint) != 0) return;
            Directory.Delete(work, recursive: true);
        }
        catch (IOException) { /* Locked staging can be removed after the client exits. */ }
        catch (UnauthorizedAccessException) { }
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    private sealed class SessionProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => report(value); }
}
