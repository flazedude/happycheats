using System.Text.Json;

namespace HappyCheats.Updater;

public sealed record ApplyRequest(string TargetDirectory, string PackageDirectory, string WorkDirectory,
    int ParentProcessId, long ParentStartTimeUtcTicks);

/// <summary>Replaces the portable application as a directory, retaining the previous release for recovery.</summary>
public static class UpdateInstaller
{
    public static void Validate(ApplyRequest request)
    {
        var target = Full(request.TargetDirectory);
        var work = Full(request.WorkDirectory);
        var package = Full(request.PackageDirectory);
        var parent = Directory.GetParent(target)?.FullName ?? throw new IOException("Cannot update a drive root.");
        var staging = Path.Combine(parent, ".happycheats-updates");
        if (!Same(Directory.GetParent(work)?.FullName, staging) ||
            !Guid.TryParseExact(Path.GetFileName(work), "N", out _) ||
            !Same(package, Path.Combine(work, "HappyCheats")))
            throw new IOException("The update must be staged beside the application in its own update folder.");
        if (!Directory.Exists(target) || !Directory.Exists(package)) throw new IOException("An update directory is missing.");
        RejectLinks(target);
        RejectLinks(work);
        foreach (var required in new[] { "HappyCheats.exe", "HappyCheats.dll", "HappyCheats.runtimeconfig.json",
                     "updater/HappyCheats.Updater.exe", "updater/HappyCheats.Updater.dll", "updater/HappyCheats.Updater.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(package, required))) throw new IOException("The release is missing " + required + ".");
        if (!File.Exists(Path.Combine(target, "HappyCheats.exe"))) throw new IOException("The application to update was not found.");
        foreach (var reserved in new[] { "previous", "failed-release", "transaction.json" })
            if (Path.Exists(Path.Combine(work, reserved))) throw new IOException("This update has already been applied or interrupted. Download it again.");
    }

    public static string Apply(ApplyRequest request, Action<string> launchAndConfirm)
    {
        Validate(request);
        var target = Full(request.TargetDirectory);
        var package = Full(request.PackageDirectory);
        var backup = Path.Combine(request.WorkDirectory, "previous");
        var failed = Path.Combine(request.WorkDirectory, "failed-release");
        // A downloaded release cannot change the endpoint or signing key trusted by this installation.
        var settings = Path.Combine(target, "update-settings.json");
        var stagedSettings = Path.Combine(package, "update-settings.json");
        if (!File.Exists(settings)) throw new IOException("The existing update configuration is missing.");
        File.Copy(settings, stagedSettings, overwrite: true);
        WriteState(request, "prepared", backup);
        var oldMoved = false;
        var newMoved = false;
        var launchConfirmed = false;
        try
        {
            Directory.Move(target, backup);
            oldMoved = true;
            WriteState(request, "previous-saved", backup);
            Directory.Move(package, target);
            newMoved = true;
            WriteState(request, "replaced", backup);
            launchAndConfirm(target);
            launchConfirmed = true;
            WriteState(request, "complete", backup);
            return backup;
        }
        catch (Exception installError)
        {
            // A running, confirmed client already owns the new files. A final journal write failure
            // must never roll its directory out from under it; the previous release is still retained.
            if (launchConfirmed) return backup;
            try
            {
                if (newMoved) Directory.Move(target, failed);
                if (oldMoved) Directory.Move(backup, target);
                WriteState(request, "rolled-back", backup);
            }
            catch (Exception rollbackError)
            {
                throw new IOException($"Update failed and automatic recovery could not finish. Previous release: {backup}. " +
                    $"Application location: {target}. Installation: {installError.Message} Recovery: {rollbackError.Message}", rollbackError);
            }
            throw new IOException("Update failed; the previous release is restored. " + installError.Message, installError);
        }
    }

    private static void WriteState(ApplyRequest request, string state, string backup)
    {
        var path = Path.Combine(request.WorkDirectory, "transaction.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { state, request.TargetDirectory, request.PackageDirectory, backup,
            updatedAt = DateTimeOffset.UtcNow });
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static bool Same(string? left, string? right) => left != null && right != null &&
        string.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase);
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static void RejectLinks(string path)
    {
        // Check ancestors as well as descendants; a junction in either can redirect a directory move.
        for (var cursor = new DirectoryInfo(Full(path)); cursor != null; cursor = cursor.Parent)
            if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update directories are not supported.");
        CheckChildren(new DirectoryInfo(path));
    }

    private static void CheckChildren(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update files are not supported.");
            if (entry is DirectoryInfo child) CheckChildren(child);
        }
    }
}
