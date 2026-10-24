using System.Globalization;
using System.Text.RegularExpressions;

namespace NetMan.Core;

public static partial class ProfileOperations
{
    /// <summary>A copy on the same adapter with a new id and a numbered name.</summary>
    public static Profile Duplicate(Profile source, IEnumerable<Profile> existing) =>
        source with
        {
            Id = Guid.NewGuid(),
            Name = UniqueName(source.Name, source.AdapterId, existing),
        };

    /// <summary>A copy on another adapter. Keeps the name unless that adapter already has it.</summary>
    public static Profile CopyTo(Profile source, Guid adapterId, string adapterName, IEnumerable<Profile> existing)
    {
        if (adapterId == Guid.Empty)
        {
            throw new ArgumentException("Adapter id is required.", nameof(adapterId));
        }

        return source with
        {
            Id = Guid.NewGuid(),
            AdapterId = adapterId,
            AdapterName = adapterName,
            Name = UniqueName(source.Name, adapterId, existing),
        };
    }

    public static string UniqueName(string name, Guid adapterId, IEnumerable<Profile> existing)
    {
        var taken = existing
            .Where(p => p.AdapterId == adapterId)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(name))
        {
            return name;
        }

        var match = NumberedSuffix().Match(name);
        var baseName = match.Success ? match.Groups[1].Value : name;
        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName} ({n})");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex(@"^(.*) \((\d+)\)$")]
    private static partial Regex NumberedSuffix();
}
