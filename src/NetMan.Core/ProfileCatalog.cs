namespace NetMan.Core;

/// <summary>An immutable, ordered set of profiles. Every change returns a new catalog.</summary>
public sealed class ProfileCatalog(IEnumerable<Profile> profiles)
{
    public static readonly ProfileCatalog Empty = new([]);

    public IReadOnlyList<Profile> All { get; } = profiles.ToList().AsReadOnly();

    public IReadOnlyList<Profile> ForAdapter(Guid adapterId) => All.Where(p => p.AdapterId == adapterId).ToList().AsReadOnly();

    public IReadOnlyList<Profile> Orphans(IEnumerable<Guid> presentAdapterIds)
    {
        var present = presentAdapterIds.ToHashSet();
        return All.Where(p => !present.Contains(p.AdapterId)).ToList().AsReadOnly();
    }

    public Profile? Find(Guid id) => All.FirstOrDefault(p => p.Id == id);

    public ProfileCatalog Upsert(Profile profile)
    {
        var index = IndexOf(profile.Id);
        return index < 0
            ? new ProfileCatalog(All.Append(profile))
            : new ProfileCatalog(All.Select((p, i) => i == index ? profile : p));
    }

    public ProfileCatalog Remove(Guid id)
    {
        Require(id);
        return new ProfileCatalog(All.Where(p => p.Id != id));
    }

    public (ProfileCatalog Catalog, Profile Copy) Duplicate(Guid id)
    {
        var index = Require(id);
        var copy = ProfileOperations.Duplicate(All[index], All);
        return (new ProfileCatalog(All.Take(index + 1).Append(copy).Concat(All.Skip(index + 1))), copy);
    }

    public (ProfileCatalog Catalog, Profile Copy) CopyTo(Guid id, AdapterInfo target)
    {
        var index = Require(id);
        var copy = ProfileOperations.CopyTo(All[index], target.Id, target.Name, All);
        return (new ProfileCatalog(All.Append(copy)), copy);
    }

    public bool NameTaken(string name, Guid adapterId, Guid? exceptId) =>
        All.Any(p => p.AdapterId == adapterId
            && p.Id != exceptId
            && string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps each profile's stored adapter name current, so a later "Not found" group still shows a useful name.</summary>
    public ProfileCatalog SyncAdapterNames(IEnumerable<AdapterInfo> adapters)
    {
        var names = adapters.ToDictionary(a => a.Id, a => a.Name);
        return new ProfileCatalog(All.Select(p =>
            names.TryGetValue(p.AdapterId, out var name) && name != p.AdapterName ? p with { AdapterName = name } : p));
    }

    public bool Differs(ProfileCatalog other) => !All.SequenceEqual(other.All);

    private int IndexOf(Guid id)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int Require(Guid id)
    {
        var index = IndexOf(id);
        return index >= 0 ? index : throw new KeyNotFoundException($"No profile with id {id}.");
    }
}
