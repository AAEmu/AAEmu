namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// One channel of a system instance, as the client's channel picker lists it.
/// </summary>
/// <param name="ChannelId">
/// The channel index. The client labels the row from it ("Dimension - 1" for index 0).
/// </param>
/// <param name="InstanceId">
/// The world copy this row stands for (<c>WorldInstance.Id</c>). It is what the client hands back when the row
/// is picked, so it has to identify the copy — never 0, which the client fails on.
/// </param>
/// <param name="Current">Players inside that copy.</param>
/// <param name="Restrict">How many it holds; the client draws its badge from <c>Current / Restrict</c>.</param>
public readonly record struct SysIndunChannel(int ChannelId, uint InstanceId, int Current, int Restrict);

/// <summary>
/// Which channels a system instance's picker lists, and which ones have to exist for it to be a choice.
/// </summary>
/// <remarks>
/// <para>
/// The client's own list is bounded: its serializer clamps <c>countSysIndun</c> to 32, so a longer list would
/// be truncated on the wire with the extra rows read as garbage.
/// </para>
/// <para>
/// The instances that use this picker are the ones built to spread players over several copies of the same
/// place — the library floors, where a dimension that fills up is left for the next one. A list with a single
/// row is therefore not the feature: <see cref="ChannelsToCreate"/> says which copies have to exist before the
/// list is worth sending, and the caller creates them.
/// </para>
/// </remarks>
public static class SysIndunChannelRules
{
    /// <summary>The most rows the client reads; its serializer clamps the count to this.</summary>
    public const int MaxChannels = 32;

    /// <summary>How many dimensions to have running, so the player has one to move to when another fills.</summary>
    public const int MinimumOfferedChannels = 2;

    /// <summary>
    /// The rows for the copies that exist: one per channel, in channel order, capped at what the client reads.
    /// </summary>
    /// <param name="existing">The copies of this instance that exist now.</param>
    /// <param name="maxPlayers">The instance's capacity, from <c>indun_zones.max_players</c>.</param>
    public static IReadOnlyList<SysIndunChannel> Build(IEnumerable<SysIndunChannel> existing, int maxPlayers)
    {
        var rows = new List<SysIndunChannel>();
        var used = new HashSet<int>();

        foreach (var channel in (existing ?? []).OrderBy(channel => channel.ChannelId))
        {
            if (rows.Count >= MaxChannels)
                break;
            if (!used.Add(channel.ChannelId))
                continue;

            rows.Add(channel with { Restrict = maxPlayers });
        }

        return rows;
    }

    /// <summary>
    /// The channels that have to be brought up before the list can offer a choice: the lowest ones no copy is
    /// using, until there are <see cref="MinimumOfferedChannels"/> of them.
    /// </summary>
    public static IReadOnlyList<int> ChannelsToCreate(IEnumerable<int> existingChannels)
    {
        var used = new HashSet<int>(existingChannels ?? []);
        var missing = new List<int>();

        for (var channel = 0; used.Count + missing.Count < MinimumOfferedChannels && missing.Count < MaxChannels;
             channel++)
        {
            if (used.Contains(channel))
                continue;

            missing.Add(channel);
        }

        return missing;
    }
}
