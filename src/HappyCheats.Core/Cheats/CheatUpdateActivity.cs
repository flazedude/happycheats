namespace HappyCheats.Core.Cheats;

public enum CheatUpdateStage
{
    CheckingSource, SourceVerified, SourceUnavailable, SelectingCatalog,
    CheckingCache, ReusingCache, RepairingCache, Connecting, Downloading,
    VerifyingPackage, Extracting, VerifyingModule, SavingPackage
}

public sealed record CheatUpdateActivity(CheatUpdateStage Stage, string? Source = null);
