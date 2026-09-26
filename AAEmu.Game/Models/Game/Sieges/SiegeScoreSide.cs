namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>
/// The three counters one zone group's siege score carries.
/// </summary>
/// <remarks>
/// The wire (SCSiegeScorePointPacket) writes them as outlawPoint, defensePoint, offensePoint, so the
/// ordinal here is deliberately not the wire order: <see cref="SiegeScoreState.For"/> and the packet
/// are the only places that map a side onto a field.
/// <para>
/// A side is a role in one siege, not a faction. The defending alliance holds the ground, the attacking
/// alliance purifies the guard tower's magic power, and the raider alliance destroys it - the three
/// conditions the shipped siege guide text (<c>ui_texts.siege_score_guide</c>) gives for a siege.
/// </para>
/// </remarks>
public enum SiegeScoreSide
{
    /// <summary>The alliance holding the dominion: it wins by preventing the tower's magic power from being purified or destroyed.</summary>
    Defense = 0,

    /// <summary>The attacking alliance: it wins by purifying the guard tower's magic power with its own faction's power.</summary>
    Offense = 1,

    /// <summary>The raider alliance: it wins by destroying the guard tower's magic power.</summary>
    Outlaw = 2,
}
