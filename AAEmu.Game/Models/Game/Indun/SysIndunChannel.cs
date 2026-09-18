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
/// A copy of a system instance as the manager sees it: the row it would become, and whether a host is
/// serving it right now.
/// </summary>
/// <param name="Channel">The row for that copy.</param>
/// <param name="Hosted">Whether a zone host has that copy loaded.</param>
public readonly record struct SysIndunChannelCopy(SysIndunChannel Channel, bool Hosted);

/// <summary>
/// The dimension a character picked in the channel list, kept until the enter request that follows it.
/// The enter request names the instance but carries no channel, so this is what decides the copy.
/// </summary>
/// <param name="ZoneKey">The instance zone the list was for.</param>
/// <param name="WorldId">The copy the row stood for (<c>WorldInstance.Id</c>).</param>
/// <param name="ChannelId">The channel of that row.</param>
public readonly record struct SysIndunPick(uint ZoneKey, uint WorldId, int ChannelId);

/// <summary>
/// Which channels a system instance's picker lists.
/// </summary>
/// <remarks>
/// <para>
/// The client's own list is bounded: its serializer clamps <c>countSysIndun</c> to 32, so a longer list would
/// be truncated on the wire with the extra rows read as garbage.
/// </para>
/// <para>
/// The instances that use this picker are the ones built to spread players over several copies of the same
/// place — the library floors, where a dimension that fills up is left for the next one. Those copies are
/// persistent and hosted like any other zone, so this class only decides what to <em>offer</em>: a copy is
/// offered when a host has it loaded. Offering a copy nothing serves would put a dimension in front of the
/// player that cannot be entered, which is worse than a shorter list.
/// </para>
/// </remarks>
public static class SysIndunChannelRules
{
    /// <summary>The most rows the client reads; its serializer clamps the count to this.</summary>
    public const int MaxChannels = 32;

    /// <summary>
    /// The rows worth offering: only copies a host is serving, one per channel, in channel order, capped at
    /// what the client reads.
    /// </summary>
    /// <param name="copies">The copies of this instance that exist now, with their host state.</param>
    /// <param name="maxPlayers">The instance's capacity, from <c>indun_zones.max_players</c>.</param>
    public static IReadOnlyList<SysIndunChannel> BuildOfferable(IEnumerable<SysIndunChannelCopy> copies,
        int maxPlayers)
    {
        var rows = new List<SysIndunChannel>();

        foreach (var copy in Offerable(copies))
            rows.Add(copy.Channel with { Restrict = maxPlayers });

        return rows;
    }

    /// <summary>
    /// The copies worth offering: a host is serving them, they name a copy, one per channel, in channel order,
    /// up to what the client reads.
    /// </summary>
    private static IEnumerable<SysIndunChannelCopy> Offerable(IEnumerable<SysIndunChannelCopy> copies)
    {
        var used = new HashSet<int>();
        var taken = 0;

        foreach (var copy in (copies ?? []).OrderBy(copy => copy.Channel.ChannelId))
        {
            if (taken >= MaxChannels)
                yield break;
            if (!copy.Hosted)
                continue;
            // A row the client cannot resolve back to a copy is not a choice.
            if (copy.Channel.InstanceId == 0)
                continue;
            if (!used.Add(copy.Channel.ChannelId))
                continue;

            taken++;
            yield return copy;
        }
    }

    /// <summary>
    /// The copy a pick lands in: the exact copy the client named when it is still there, else the copy on the
    /// channel it named. Nothing when neither is among the copies a host is serving.
    /// </summary>
    /// <param name="copies">The copies of this instance that exist now, with their host state.</param>
    /// <param name="pickedWorldId">The copy the client named, when it named one.</param>
    /// <param name="pickedChannel">The channel the client named, when it named one.</param>
    public static uint? FindCopyForPick(IEnumerable<SysIndunChannelCopy> copies, uint? pickedWorldId,
        int? pickedChannel)
    {
        if (pickedWorldId is { } worldId)
        {
            foreach (var copy in Offerable(copies))
            {
                if (copy.Channel.InstanceId == worldId)
                    return copy.Channel.InstanceId;
            }
        }

        if (pickedChannel is { } channel)
        {
            foreach (var copy in Offerable(copies))
            {
                if (copy.Channel.ChannelId == channel)
                    return copy.Channel.InstanceId;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a remembered pick should decide this entry. Only channel instances honour one, and only when
    /// it names a copy — an ordinary dungeon has its own rejoin and team rules.
    /// </summary>
    public static bool HonourPick(bool selectChannel, uint pickedCopyId) =>
        selectChannel && pickedCopyId != 0;

    /// <summary>
    /// Whether a copy is served. A missing probe means this process has no hosts to ask, so the copy is
    /// treated as hosted — the same default a boat handoff uses — rather than as empty.
    /// </summary>
    public static bool CopyIsHosted(Func<uint, uint, bool> probe, uint zoneKey, uint worldId) =>
        probe?.Invoke(zoneKey, worldId) ?? true;
}
