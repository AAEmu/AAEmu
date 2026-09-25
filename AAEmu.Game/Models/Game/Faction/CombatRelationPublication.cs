namespace AAEmu.Game.Models.Game.Faction;

public enum CombatRelationPublicationKind
{
    FullState,
    Delta,
}

/// <summary>
/// An explicit, transport-only publication for one CvF or FvF relation family.
/// FullState replaces the canonical state; Delta applies inserts and zero-valued tombstones.
/// The publication version is an ordering guard in the relay and is not a wire field.
/// </summary>
public sealed class CombatRelationPublication
{
    public ulong Version { get; }
    public CombatRelationPublicationKind Kind { get; }
    public IReadOnlyList<CombatRelationEntry> Entries { get; }

    public CombatRelationPublication(
        ulong version,
        CombatRelationPublicationKind kind,
        IEnumerable<CombatRelationEntry> entries)
    {
        if (version == 0)
            throw new ArgumentOutOfRangeException(nameof(version), "A publication version must be positive.");
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Unknown combat-relation publication kind.");

        ArgumentNullException.ThrowIfNull(entries);
        Version = version;
        Kind = kind;
        Entries = Array.AsReadOnly(entries.ToArray());
    }
}
