namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>
/// One zone group's running siege score: how much of the guard tower's magic power each side has taken
/// (or, for the defender, how much it has kept) during the siege in progress.
/// </summary>
/// <remarks>
/// The three counters are the server's copy of what SCSiegeScorePointPacket shows the client, so every
/// change is broadcast as the whole state rather than as a delta - the client writes the fields it is
/// sent (it does not add to them), which is why a delta would show a total of one forever.
/// <para>
/// Immutable: <see cref="With"/> returns the next state instead of editing this one, so a snapshot taken
/// before a write is still the snapshot the caller holds.
/// </para>
/// </remarks>
public sealed record SiegeScoreState
{
    public static SiegeScoreState Empty(ushort zoneGroupId) => new() { ZoneGroupId = zoneGroupId };

    /// <summary>The zone group whose siege this is - the key every siege packet carries.</summary>
    public ushort ZoneGroupId { get; init; }

    /// <summary>Guard-tower magic power destroyed by the raider side.</summary>
    public uint OutlawPoint { get; init; }

    /// <summary>Guard-tower magic power the defending side has kept.</summary>
    public uint DefensePoint { get; init; }

    /// <summary>Guard-tower magic power purified by the attacking side.</summary>
    public uint OffensePoint { get; init; }

    public uint For(SiegeScoreSide side) => side switch
    {
        SiegeScoreSide.Defense => DefensePoint,
        SiegeScoreSide.Offense => OffensePoint,
        SiegeScoreSide.Outlaw => OutlawPoint,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown siege score side."),
    };

    public SiegeScoreState With(SiegeScoreSide side, uint point) => side switch
    {
        SiegeScoreSide.Defense => this with { DefensePoint = point },
        SiegeScoreSide.Offense => this with { OffensePoint = point },
        SiegeScoreSide.Outlaw => this with { OutlawPoint = point },
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown siege score side."),
    };

    /// <summary>
    /// The next state with <paramref name="amount"/> added to one side, saturating at <see cref="uint.MaxValue"/>
    /// rather than wrapping: a counter that wrapped would report a side as having lost ground.
    /// </summary>
    public SiegeScoreState Plus(SiegeScoreSide side, uint amount)
    {
        var current = For(side);
        var sum = (ulong)current + amount;
        return With(side, sum > uint.MaxValue ? uint.MaxValue : (uint)sum);
    }
}
