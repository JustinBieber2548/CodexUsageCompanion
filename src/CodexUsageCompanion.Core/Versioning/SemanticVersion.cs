using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexUsageCompanion.Core.Versioning;

public sealed record SemanticVersion :
    IComparable<SemanticVersion>,
    IComparable
{
    private static readonly Regex Pattern = new(
        @"^v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SemanticVersion(
        int major,
        int minor,
        int patch,
        IReadOnlyList<string>? prerelease = null,
        string? buildMetadata = null)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Version numbers cannot be negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease ?? [];
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Prerelease { get; }

    public string? BuildMetadata { get; }

    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var parsed))
        {
            throw new FormatException($"'{value}' is not a valid semantic version.");
        }

        return parsed;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.')
            : [];
        version = new SemanticVersion(
            major,
            minor,
            patch,
            prerelease,
            match.Groups["build"].Success ? match.Groups["build"].Value : null);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numeric = Major.CompareTo(other.Major);
        if (numeric == 0)
        {
            numeric = Minor.CompareTo(other.Minor);
        }

        if (numeric == 0)
        {
            numeric = Patch.CompareTo(other.Patch);
        }

        if (numeric != 0)
        {
            return numeric;
        }

        if (Prerelease.Count == 0 && other.Prerelease.Count == 0)
        {
            return 0;
        }

        if (Prerelease.Count == 0)
        {
            return 1;
        }

        if (other.Prerelease.Count == 0)
        {
            return -1;
        }

        var length = Math.Max(Prerelease.Count, other.Prerelease.Count);
        for (var index = 0; index < length; index++)
        {
            if (index >= Prerelease.Count)
            {
                return -1;
            }

            if (index >= other.Prerelease.Count)
            {
                return 1;
            }

            var comparison = CompareIdentifier(Prerelease[index], other.Prerelease[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    int IComparable.CompareTo(object? obj)
    {
        return obj is SemanticVersion other
            ? CompareTo(other)
            : throw new ArgumentException("Object must be a SemanticVersion.", nameof(obj));
    }

    public override string ToString()
    {
        var value = $"{Major}.{Minor}.{Patch}";
        if (Prerelease.Count > 0)
        {
            value += $"-{string.Join('.', Prerelease)}";
        }

        if (!string.IsNullOrWhiteSpace(BuildMetadata))
        {
            value += $"+{BuildMetadata}";
        }

        return value;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightNumeric = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftNumeric && rightNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }
}
