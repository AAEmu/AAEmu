namespace AAEmu.Game.Models.Game.Squad;

/// <summary>Pure squad lifecycle rules (unit-testable, no networking).</summary>
public static class SquadRules
{
    public const int PageSize = 3;

    /// <summary>
    /// Recruit methods that put a team on the Recruit/Search board. Both recruit methods are
    /// listed; only Quick Enter stays hidden, since it never waits for anyone to join.
    /// </summary>
    public static bool IsListedOpenType(SquadOpenType openType) =>
        openType is SquadOpenType.Public or SquadOpenType.Private or SquadOpenType.MustPublic;

    /// <summary>
    /// Whether a player browsing the board may put themselves forward. Private Recruit teams are
    /// listed but fill only by the leader's invitation or by matchmaking backfill.
    /// </summary>
    public static bool AcceptsBoardApplications(SquadOpenType openType) =>
        openType is SquadOpenType.Public or SquadOpenType.MustPublic;

    /// <summary>
    /// Whether the team is recruiting and so wants matchmaking to keep looking for members. Quick
    /// Enter plays with whoever is already on the team, which is why it reaches the instance
    /// quickly: there is nobody left to wait for.
    /// </summary>
    public static bool WaitsForOtherPlayers(SquadOpenType openType) =>
        openType != SquadOpenType.DirectMatching;

    public static bool CanCreate(bool alreadyInSquad) => !alreadyInSquad;

    public static bool CanJoinPublic(Squad squad, uint characterId, byte level, int characterGearScore)
    {
        if (squad == null || !AcceptsBoardApplications(squad.OpenType))
            return false;
        if (squad.GetMember(characterId) != null)
            return false;
        if (squad.IsFull)
            return false;
        if (squad.LimitLevel > 0 && level < squad.LimitLevel)
            return false;
        if (squad.LimitGearScore > 0 && characterGearScore < squad.LimitGearScore)
            return false;
        return true;
    }

    /// <summary>
    /// Ready is only asked of members who are still connected. An offline seat still occupies
    /// the roster (<see cref="Squad.IsFull"/>) until the leader expels it, but it must not
    /// freeze Register / matching.
    /// </summary>
    public static bool AllOnlineReady(IEnumerable<SquadMember> members)
    {
        if (members == null)
            return false;

        var online = 0;
        foreach (var member in members)
        {
            if (member.Offline)
                continue;
            if (!member.Ready)
                return false;
            online++;
        }

        return online > 0;
    }

    public static bool ShouldBeginEnterOnApply(Squad squad) =>
        squad is { EnterCommitted: false, AllReady: true };

    /// <summary>
    /// Registering always hands the team to matchmaking, whatever the recruit method. Quick Enter
    /// skips the wait for other players to join, not the instance's warmup: the team still sits
    /// registered until matchmaking offers it an instance, and entry happens on that offer.
    /// </summary>
    public static bool ShouldQueueMatchingOnApply(Squad squad) =>
        squad is { EnterCommitted: false, AllReady: true };

    /// <summary>
    /// After the team leaves the instance, matching may be applied again. Enter left these flags
    /// set, which blocked Register and left the Instance UI on Leave Recruit/Search.
    /// </summary>
    public static void ResetAfterInstanceLeave(Squad squad)
    {
        if (squad == null)
            return;
        squad.EnterCommitted = false;
        squad.MatchingApplied = false;
        squad.Joining = false;
        foreach (var member in squad.Members)
            member.Ready = false;
    }

    /// <summary>
    /// Quick Enter has no board presence after a run — leaving the instance should wipe the squad
    /// so Recruit is available again. Listed recruit methods keep the team for another Register.
    /// </summary>
    public static bool ShouldDisbandAfterInstanceLeave(SquadOpenType openType) =>
        openType == SquadOpenType.DirectMatching;

    public static IReadOnlyList<Squad> FilterListed(IEnumerable<Squad> all, uint catalogId) =>
        all.Where(s => s.CatalogId == catalogId && IsListedOpenType(s.OpenType)).ToList();

    public static (IReadOnlyList<Squad> page, int total) Page(IReadOnlyList<Squad> listed, int pageIndex,
        int pageSize = PageSize)
    {
        if (pageIndex < 0)
            pageIndex = 0;
        if (pageSize <= 0)
            pageSize = PageSize;
        var total = listed.Count;
        var slice = listed.Skip(pageIndex * pageSize).Take(pageSize).ToList();
        return (slice, total);
    }

    public static byte ListButtonType(bool isMySquad) =>
        isMySquad ? (byte)2 : (byte)1; // CANCEL_RECRUIT : APPLY

    /// <summary>
    /// Invitation IDs are sequential and client-supplied on refuse/join. Only the intended target
    /// may consume a pending invite; otherwise another player could cancel someone else's invite.
    /// </summary>
    public static bool CallerOwnsInvite(uint inviteTargetId, uint callerCharacterId) =>
        inviteTargetId != 0 && inviteTargetId == callerCharacterId;
}
