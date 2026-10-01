namespace LaundryTrainer.Setup.Core;

public sealed record GameInspection(
    string Directory,
    bool Exists,
    bool IsCompatible,
    bool IsRunning,
    IReadOnlyList<string> Problems,
    string? NativeSha256,
    string? MetadataSha256)
{
    public bool CanInstall => Exists && IsCompatible && !IsRunning && Problems.Count == 0;
    public bool TrainerInstalled => File.Exists(Path.Combine(Directory, "Mods", "LaundryTrainer.dll"));
}

public sealed record SteamInstallation(
    string SteamDirectory,
    string? SteamExecutable,
    string LibraryDirectory,
    string ManifestPath,
    string GameDirectory,
    string? StateFlags,
    GameInspection Game);

public sealed record SteamDiscoveryResult(
    IReadOnlyList<SteamInstallation> Installations,
    IReadOnlyList<string> Problems);

public sealed record InstallResult(
    string GameDirectory,
    string TrainerPath,
    string? BackupPath,
    bool LoaderInstalled,
    bool TrainerUpdated);

internal sealed record CompatibilityProfile(string NativeSha256, string MetadataSha256)
{
    internal static CompatibilityProfile Current { get; } = new(
        SupportedBuild.NativeSha256, SupportedBuild.MetadataSha256);
}
