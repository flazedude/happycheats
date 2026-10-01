namespace HappyCheats.App;

/// <summary>Bundled Steam artwork; no network or local Steam cache is needed at runtime.</summary>
public static class GameArtwork
{
    public static Uri IconUri(string moduleId) => ResourceUri(moduleId, "icon");

    public static Uri LogoUri(string moduleId) => ResourceUri(moduleId, "logo");

    private static Uri ResourceUri(string moduleId, string kind)
    {
        var assetName = moduleId switch
        {
            "chinese-parents" => "chinese-parents",
            "project-hospital" => "project-hospital",
            "hotel-architect" => "hotel-architect",
            "civilization-vi" => "civilization-vi",
            "civilization-vii" => "civilization-vii",
            "elden-ring" => "elden-ring",
            "onimusha" => "onimusha",
            "laundry" => "laundry",
            "astroneer" => "astroneer",
            "stardew" => "stardew",
            "dawnwalker" => "dawnwalker",
            "crimson-desert" => "crimson-desert",
            "black-myth-wukong" => "black-myth-wukong",
            "red-dead-redemption-2" => "red-dead-redemption-2",
            "gta-v-enhanced" => "gta-v-enhanced",
            "witcher-3" => "witcher-3",
            _ => throw new ArgumentOutOfRangeException(nameof(moduleId), moduleId, "No bundled artwork for this game.")
        };

        return new Uri($"pack://application:,,,/HappyCheats;component/Assets/Games/{assetName}-{kind}.png", UriKind.Absolute);
    }
}
