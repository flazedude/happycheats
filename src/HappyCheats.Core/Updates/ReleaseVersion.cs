using System.Globalization;

namespace HappyCheats.Core.Updates;

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion Parse(string value) => TryParse(value, out var version)
        ? version : throw new InvalidDataException("Release version must be three numeric components, for example 1.2.3.");

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
        var parts = value.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || (parts[i].Length > 1 && parts[i][0] == '0') ||
                parts[i].Any(c => c is < '0' or > '9') ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        version = new(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
        return comparison == 0 ? Patch.CompareTo(other.Patch) : comparison;
    }

    public override string ToString() => FormattableString.Invariant($"{Major}.{Minor}.{Patch}");
}
