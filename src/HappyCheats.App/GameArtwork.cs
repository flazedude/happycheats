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
            "phantom-blade-zero" => "phantom-blade-zero",
            "red-dead-redemption-2" => "red-dead-redemption-2",
            "gta-v-enhanced" => "gta-v-enhanced",
            "witcher-3" => "witcher-3",
            _ => ""
        };

        return assetName.Length == 0 ? new Uri("pack://application:,,,/HappyCheats;component/Assets/Branding/HappyCheats.png", UriKind.Absolute)
            : new Uri($"pack://application:,,,/HappyCheats;component/Assets/Games/{assetName}-{kind}.png", UriKind.Absolute);
    }
}
