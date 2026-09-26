using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Team;

using Microsoft.Extensions.DependencyInjection;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// World-authoritative state for raid-team joint requests and the consent phase of team summon.
/// Teams stay independent objects; the joint roster only describes the client-visible ordering.
/// All world access goes through <see cref="ITeamJointContext"/>, so every flow below is
/// deterministic and testable without a running World.
/// </summary>
public sealed class TeamJointManager(ITeamJointContext context, TimeProvider timeProvider = null)
    : Singleton<TeamJointManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ITeamJointContext _context = context;
    private readonly ConcurrentDictionary<uint, JointSession> _sessions = new();
    private readonly ConcurrentDictionary<uint, PendingJoint> _pendingJoints = new();
    private readonly ConcurrentDictionary<uint, PendingSummon> _pendingSummons = new();
    private readonly ConcurrentDictionary<uint, PendingBreak> _pendingBreaks = new();

    /// <summary>Pending joint requests, keyed by the requesting team.</summary>
    internal int PendingJointCount => _pendingJoints.Count;

    /// <summary>Active joint sessions.</summary>
    internal int SessionCount => _sessions.Count;

    /// <summary>Pending summon rounds, keyed by recipient.</summary>
    internal int PendingSummonCount => _pendingSummons.Count;

    /// <summary>Pending break asks.</summary>
    internal int PendingBreakCount => _pendingBreaks.Count;

    /// <summary>The joint state currently published on a team, or null when it is not federated.</summary>
    public TeamJointRoster? GetSession(uint teamId) =>
        _sessions.Values.FirstOrDefault(session => session.Contains(teamId))?.Roster;

    public static bool TryGet(out TeamJointManager manager)
    {
        manager = SingletonContainer.ServiceProvider?.GetService<TeamJointManager>();
        return manager != null;
    }

    public void RequestJointInfo(uint requesterId, ulong type, sbyte mode, string targetName, sbyte worldId)
    {
        PurgeExpired();

        if (!TeamJointModes.IsKnownWireMode(mode) || !TeamJointModes.IsRequestMode(mode))
        {
            Logger.Warn("Team joint request from {0} used unknown mode {1}.", requesterId, mode);
            _context.SendError(requesterId, ErrorMessageType.TeamNoRights);
            return;
        }

        // The target context menu arrives with an empty name and this packet carries no target
        // unit id, so the server resolves the requester's current selection instead of refusing.
        // Modes 1 and 3 carry the name.
        var resolvedTargetName = string.IsNullOrWhiteSpace(targetName)
            ? mode == TeamJointModes.MenuTargetRequest
                ? _context.FindSelectedTarget(requesterId)?.Name
                : null
            : targetName;

        if (string.IsNullOrWhiteSpace(resolvedTargetName))
        {
            Logger.Warn("Team joint request from {0} carried no resolvable target name (mode {1}).",
                requesterId, mode);
            _context.SendError(requesterId, ErrorMessageType.TeamInviteeOffline);
            return;
        }

        if (!_context.IsLocalWorld(worldId))
        {
            _context.SendError(requesterId, ErrorMessageType.TeamInviteeOffline);
            return;
        }

        var sourceTeam = _context.FindTeamByMember(requesterId);
        var targetCharacter = _context.FindCharacterByName(resolvedTargetName);
        var targetTeam = targetCharacter == null ? null : _context.FindTeamByMember(targetCharacter.Id);

        if (sourceTeam == null || targetTeam == null || targetCharacter == null ||
            !sourceTeam.CanManage(requesterId) || !targetTeam.IsRaid || sourceTeam.Id == targetTeam.Id)
        {
            _context.SendError(requesterId, ErrorMessageType.TeamNoRights);
            return;
        }

        if (_sessions.Values.Any(session => session.Contains(sourceTeam.Id) || session.Contains(targetTeam.Id)))
        {
            _context.SendError(requesterId, ErrorMessageType.TeamInviteeInTeam);
            return;
        }

        // A team may sit on EITHER side of an outstanding ask. Comparing only source-to-source and
        // target-to-target let C ask A while A was already asking B, which is how one team ended up
        // party to two pending joints at once.
        if (_pendingJoints.Values.Any(pending =>
                pending.SourceTeamId == sourceTeam.Id || pending.TargetTeamId == targetTeam.Id ||
                pending.SourceTeamId == targetTeam.Id || pending.TargetTeamId == sourceTeam.Id))
        {
            _context.SendError(requesterId, ErrorMessageType.TeamLoading);
            return;
        }

        if (!TeamJointRules.Fits(sourceTeam.MemberCount, targetTeam.MemberCount, Team.RaidMemberLimit))
        {
            _context.SendError(requesterId, ErrorMessageType.TeamFull);
            return;
        }

        var sourceOwner = OnlineTeamOwner(sourceTeam);
        var targetOwner = OnlineTeamOwner(targetTeam);
        if (sourceOwner == null || targetOwner == null)
        {
            _context.SendError(requesterId, ErrorMessageType.TeamInvitorOffline);
            return;
        }

        _pendingJoints[sourceTeam.Id] = new PendingJoint(
            sourceTeam.Id,
            targetTeam.Id,
            requesterId,
            type,
            _timeProvider.GetUtcNow() + RequestLifetime);

        _context.Send(requesterId, new SCTeamJointInfoPacket(TeamJointModes.ContextRequest, new TeamJointInfo(
            unchecked((long)type),
            targetOwner.Name,
            targetTeam.Id,
            targetTeam.MemberCount,
            0,
            false)));
    }

    public void RespondToJoint(uint responderId, ulong type, bool myTeamLeader, bool accept, bool timeout)
    {
        PurgeExpired();
        var team = _context.FindTeamByMember(responderId);
        if (team == null)
            return;

        // The dictionary is keyed by the requesting team, so the answering side is found by role.
        PendingJoint pending = null;
        if (_pendingJoints.TryGetValue(team.Id, out var keyed) &&
            (keyed.SourceTeamId == team.Id || keyed.TargetTeamId == team.Id))
            pending = keyed;
        pending ??= _pendingJoints.Values.FirstOrDefault(value => value.TargetTeamId == team.Id);
        if (pending == null)
            return;

        if (pending.Type != type)
        {
            _pendingJoints.TryRemove(pending.SourceTeamId, out _);
            _context.SendError(responderId, ErrorMessageType.TeamLoading);
            return;
        }

        var isSource = pending.SourceTeamId == team.Id;
        var otherTeam = _context.FindTeam(isSource ? pending.TargetTeamId : pending.SourceTeamId);
        if (otherTeam == null || !team.CanManage(responderId))
        {
            _context.SendError(responderId, ErrorMessageType.TeamNoRights);
            return;
        }

        if (isSource)
        {
            if (!accept || timeout)
            {
                _pendingJoints.TryRemove(pending.SourceTeamId, out _);
                NotifyJointRejected(pending, timeout);
                return;
            }

            if (!_pendingJoints.TryUpdate(pending.SourceTeamId, pending with { LeaderChoice = myTeamLeader }, pending))
                return;

            var sourceOwner = OnlineTeamOwner(team);
            var targetOwner = OnlineTeamOwner(otherTeam);
            if (sourceOwner == null || targetOwner == null)
            {
                _pendingJoints.TryRemove(pending.SourceTeamId, out _);
                return;
            }

            _context.Send(targetOwner.Id, new SCTeamJointInfoPacket(TeamJointModes.ResponsePrompt,
                new TeamJointInfo(
                    unchecked((long)type),
                    sourceOwner.Name,
                    team.Id,
                    team.MemberCount,
                    0,
                    myTeamLeader)));
            return;
        }

        if (!accept || timeout)
        {
            _pendingJoints.TryRemove(pending.SourceTeamId, out _);
            NotifyJointRejected(pending, timeout);
            return;
        }

        // The client does not choose a leader: it echoes back the flag the server placed in the
        // response dialog. DLG_TASK_RESOPONSE_RAID_JOINT (x2ui/components/dialog/handle_task.lua)
        // passes the same infoTable["leader"] to JointOk (:2910) and to JointCancel (:2913/:2916),
        // and the decline/timeout is carried by JointCancel's separate boolean. So a genuine
        // accept always echoes whatever we sent, and requiring the answer to equal the stored
        // value compared a REQUEST-side meaning (leader => requester is the officer,
        // handle_task.lua:2829/:2836) against a RESPONSE-side one (leader => responder is the
        // owner, handle_task.lua:2883/:2890; joint_view.lua:462-463 agrees). Both values are
        // valid answers; the echoed one selects the leading team in CommitJoint.
        // The echoed flag is deliberately NOT used to pick the leader. Whether the native
        // X2Team:JointOk inverts it before sending is not recoverable here: the Lua hands
        // infoTable["leader"] straight through (handle_task.lua:2910) and the binding is opaque, so
        // the polarity is a standing disagreement. Accepting either value would let a crafted answer
        // choose the owner, so the server's own stored LeaderChoice decides instead. That is correct
        // whichever way the client resolves it, and the echo is logged for diagnosis only.
        Logger.Debug("Team joint answer from {0} echoed leader={1}; using the stored choice {2}.",
            responderId, myTeamLeader, pending.LeaderChoice);
        CommitJoint(pending);
    }

    public void RespondToJointBreak(uint responderId, bool ask, bool accept)
    {
        var team = _context.FindTeamByMember(responderId);
        if (team == null)
            return;
        var session = _sessions.Values.FirstOrDefault(value => value.Contains(team.Id));
        if (session == null)
            return;

        if (ask)
        {
            if (!team.CanBreak(responderId))
            {
                _context.SendError(responderId, ErrorMessageType.TeamNoRights);
                return;
            }

            var otherOwner = OnlineTeamOwner(_context.FindTeam(session.GetOtherTeamId(team.Id)));
            if (otherOwner == null)
            {
                // Recording an ask nobody received leaves the session stuck: every later break ask
                // returns silently and only a disband clears it.
                Logger.Warn("Team joint break ask from {0} dropped: the other owner is offline.", responderId);
                return;
            }

            if (!_pendingBreaks.TryAdd(session.JointId, new PendingBreak(team.Id, responderId)))
                return;
            _context.Send(otherOwner.Id, new SCTeamJointBreakPacket(true, false));
            return;
        }

        if (!_pendingBreaks.TryGetValue(session.JointId, out var pending) || pending.RequesterTeamId == team.Id)
            return;
        if (!team.CanManage(responderId))
        {
            _context.SendError(responderId, ErrorMessageType.TeamNoRights);
            return;
        }

        _pendingBreaks.TryRemove(session.JointId, out _);
        if (accept)
        {
            Dissolve(session.JointId);
            return;
        }

        var requesterOwner = OnlineTeamOwner(_context.FindTeam(pending.RequesterTeamId));
        if (requesterOwner != null)
            _context.Send(requesterOwner.Id, new SCTeamJointBreakPacket(false, false));
    }

    public IReadOnlyList<uint> RequestSummons(uint summonerId)
    {
        PurgeExpired();
        var team = _context.FindTeamByMember(summonerId);
        var summoner = _context.FindCharacterById(summonerId);
        if (team == null || summoner == null || !team.IsRaid || team.OwnerId != summonerId || !summoner.IsOnline)
        {
            if (summonerId != 0)
                _context.SendError(summonerId, ErrorMessageType.TeamNoRights);
            return [];
        }

        var targets = team.OnlineMemberIds
            .Where(id => id != summonerId)
            .Select(id => _context.FindCharacterById(id))
            .Where(character => character is { IsOnline: true })
            .Where(character => !_pendingSummons.ContainsKey(character.Id))
            .ToArray();
        if (targets.Length == 0)
            return [];

        foreach (var target in targets)
        {
            _pendingSummons[target.Id] = new PendingSummon(
                target.Id,
                summonerId,
                summoner.Name,
                _timeProvider.GetUtcNow() + RequestLifetime);
            _context.Send(target.Id, new SCTeamSummonSuggestPacket(
                summoner.Name,
                team.Id,
                summoner.ZoneId,
                summoner.X,
                summoner.Y,
                summoner.Z));
        }

        var ids = targets.Select(target => target.Id).ToArray();
        _context.Send(summonerId, new SCTeamSummonGetPacket(ids));
        return ids;
    }

    public bool ReplyToSummon(uint recipientId, bool accepted, string summonerName)
    {
        PurgeExpired();
        if (!_pendingSummons.TryGetValue(recipientId, out var pending) ||
            !string.Equals(pending.SummonerName, summonerName, StringComparison.Ordinal))
            return false;

        if (!accepted)
            return _pendingSummons.TryRemove(new KeyValuePair<uint, PendingSummon>(recipientId, pending));

        // Consume the round before acting on it. A duplicated accept finds nothing left to consume
        // and is refused, so one round can never emit the consent packet twice.
        if (!_pendingSummons.TryRemove(new KeyValuePair<uint, PendingSummon>(recipientId, pending)))
            return false;

        var summoner = _context.FindCharacterById(pending.SummonerId);
        var recipient = _context.FindCharacterById(recipientId);
        if (summoner is not { IsOnline: true } || recipient is not { IsOnline: true } || recipient.IsInBattle)
            return false;

        _context.Send(recipientId, new SCTeamSummonPacket());
        return true;
    }

    /// <summary>
    /// Releases everything the character owns. A joint session is only dissolved when the team the
    /// character owned goes away with it; a member of a still-running raid going offline must not
    /// tear the federation down. TeamManager disbands that team separately, which arrives here
    /// through <see cref="OnTeamDisbanded"/>.
    /// </summary>
    public void OnCharacterLogout(uint characterId)
    {
        PurgeExpired();
        if (characterId == 0)
            return;

        // Only the round's own character owns it. A raid member who is not the requesting owner or
        // officer, and not the leader who raised a break, must not cancel work someone else started.
        RemovePendingSummonsOf(characterId);
        RemovePendingJointsRaisedBy(characterId);
        RemovePendingBreaksRaisedBy(characterId);

        var team = _context.FindTeamByMember(characterId);
        if (team == null)
            return;

        if (team.OwnerId == characterId && !team.HasOnlineMembersExcept(characterId))
            ReleaseForTeam(team.Id, team.Id);
    }

    public void OnTeamDisbanded(uint teamId)
    {
        if (teamId == 0)
            return;
        ReleaseForTeam(teamId, teamId);
    }

    private void ReleaseForTeam(uint teamId, uint skipTeamId)
    {
        // The whole team is going away, so every request that names it goes with it regardless of
        // which character raised it.
        RemovePendingJointsForTeam(teamId);
        DropSummonsForTeam(teamId);

        foreach (var session in _sessions.Values.Where(value => value.Contains(teamId)).ToArray())
            Dissolve(session.JointId, skipTeamId);
    }

    private void RemovePendingSummonsOf(uint characterId)
    {
        foreach (var entry in _pendingSummons)
        {
            if (entry.Value.SummonerId == characterId || entry.Value.RecipientId == characterId)
                _pendingSummons.TryRemove(entry.Key, out _);
        }
    }

    private void RemovePendingJointsRaisedBy(uint characterId)
    {
        foreach (var entry in _pendingJoints)
        {
            if (entry.Value.RequesterCharacterId == characterId)
                _pendingJoints.TryRemove(entry.Key, out _);
        }
    }

    private void RemovePendingBreaksRaisedBy(uint characterId)
    {
        foreach (var entry in _pendingBreaks)
        {
            if (entry.Value.RequesterCharacterId == characterId)
                _pendingBreaks.TryRemove(entry.Key, out _);
        }
    }

    private void RemovePendingJointsForTeam(uint teamId)
    {
        foreach (var entry in _pendingJoints)
        {
            if (entry.Value.SourceTeamId == teamId || entry.Value.TargetTeamId == teamId)
                _pendingJoints.TryRemove(entry.Key, out _);
        }
    }

    private void DropSummonsForTeam(uint teamId)
    {
        var team = _context.FindTeam(teamId);
        if (team == null)
            return;
        foreach (var memberId in team.OnlineMemberIds)
            _pendingSummons.TryRemove(memberId, out _);
    }

    private void CommitJoint(PendingJoint pending)
    {
        var sourceTeam = _context.FindTeam(pending.SourceTeamId);
        var targetTeam = _context.FindTeam(pending.TargetTeamId);
        if (sourceTeam == null || targetTeam == null || !sourceTeam.IsRaid || !targetTeam.IsRaid ||
            !TeamJointRules.Fits(sourceTeam.MemberCount, targetTeam.MemberCount, Team.RaidMemberLimit))
        {
            _pendingJoints.TryRemove(pending.SourceTeamId, out _);
            return;
        }

        // Re-check at commit. Between the ask and the answer either team can have joined another
        // joint through a different ask, and a stale pending entry would then overwrite its JointId.
        if (_sessions.Values.Any(existing => existing.Contains(sourceTeam.Id) || existing.Contains(targetTeam.Id)))
        {
            _pendingJoints.TryRemove(pending.SourceTeamId, out _);
            _context.SendError(OnlineTeamOwner(sourceTeam)?.Id ?? 0, ErrorMessageType.TeamInviteeInTeam);
            return;
        }

        // The echoed leader flag is read in the RESPONSE dialog's polarity, where leader == true
        // means the responder is the OWNER: the response dialog's "my side" row uses the gold
        // crown and raid_joint_owner (handle_task.lua:2883/:2890), and joint_view.lua:462-463
        // renders the same way for the viewing player's own team. The responder is the target
        // side, so true makes the target team lead. The opposite mapping would read the same bit
        // in the request dialog's polarity, where true means the requester is the OFFICER
        // (handle_task.lua:2829/:2836).
        var leaderTeamId = pending.LeaderChoice ? pending.TargetTeamId : pending.SourceTeamId;
        var followerTeamId = leaderTeamId == pending.SourceTeamId ? pending.TargetTeamId : pending.SourceTeamId;
        var leaderCount = leaderTeamId == pending.SourceTeamId ? sourceTeam.MemberCount : targetTeam.MemberCount;
        var followerCount = followerTeamId == pending.SourceTeamId ? sourceTeam.MemberCount : targetTeam.MemberCount;

        var roster = new TeamJointRoster(pending.SourceTeamId, leaderTeamId);
        if (!roster.TryAdd(leaderTeamId, leaderCount, Team.RaidMemberLimit) ||
            !roster.TryAdd(followerTeamId, followerCount, Team.RaidMemberLimit))
            return;

        _pendingJoints.TryRemove(pending.SourceTeamId, out _);
        _sessions[roster.JointId] = new JointSession(roster);
        PublishJoint(roster, pending.Type);
    }

    private void PublishJoint(TeamJointRoster roster, ulong type)
    {
        foreach (var entry in roster.Entries)
        {
            var order = checked((int)roster.GetOrder(entry.TeamId));
            _context.ApplyJoint(entry.TeamId, roster.JointId, entry.IsLeader, order);
            foreach (var memberId in OnlineMemberIdsOf(entry.TeamId))
            {
                _context.SendTeamHeader(entry.TeamId, memberId);
                _context.Send(memberId, new SCTeamJointPacket(
                    roster.GetOtherTeamId(entry.TeamId),
                    roster.LeaderTeamId,
                    unchecked((long)type),
                    SCTeamJointPacket.PacketModeUnresolved,
                    order));
            }
        }
    }

    private void Dissolve(uint jointId, uint skipTeamId = 0)
    {
        if (!_sessions.TryRemove(jointId, out var session))
            return;
        _pendingBreaks.TryRemove(jointId, out _);

        foreach (var entry in session.Roster.Entries)
        {
            _context.ClearJoint(entry.TeamId);
            if (entry.TeamId == skipTeamId)
                continue;
            foreach (var memberId in OnlineMemberIdsOf(entry.TeamId))
            {
                _context.SendTeamHeader(entry.TeamId, memberId);
                _context.Send(memberId, new SCTeamJointBreakPacket(false, true));
            }
        }
    }

    private void NotifyJointRejected(PendingJoint pending, bool timeout)
    {
        var sourceOwner = OnlineTeamOwner(_context.FindTeam(pending.SourceTeamId));
        var targetOwner = OnlineTeamOwner(_context.FindTeam(pending.TargetTeamId));
        var packet = new SCTeamJointPacket(
            pending.TargetTeamId,
            pending.SourceTeamId,
            unchecked((long)pending.Type),
            SCTeamJointPacket.PacketModeUnresolved,
            0);
        if (sourceOwner != null)
            _context.Send(sourceOwner.Id, packet);
        if (!timeout && targetOwner != null)
            _context.Send(targetOwner.Id, packet);
    }

    private void PurgeExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in _pendingJoints.Where(entry => entry.Value.ExpiresAt <= now))
            _pendingJoints.TryRemove(entry.Key, out _);
        foreach (var entry in _pendingSummons.Where(entry => entry.Value.ExpiresAt <= now))
            _pendingSummons.TryRemove(entry.Key, out _);
    }

    private TeamJointCharacterSnapshot? OnlineTeamOwner(TeamJointTeamSnapshot? team)
    {
        if (team == null)
            return null;
        var owner = _context.FindCharacterById(team.OwnerId);
        return owner is { IsOnline: true } ? owner : null;
    }

    private uint[] OnlineMemberIdsOf(uint teamId)
    {
        var team = _context.FindTeam(teamId);
        if (team == null)
            return [];
        return team.OnlineMemberIds
            .Where(id => _context.FindCharacterById(id) is { IsOnline: true })
            .ToArray();
    }

    private sealed record PendingJoint(
        uint SourceTeamId,
        uint TargetTeamId,
        uint RequesterCharacterId,
        ulong Type,
        DateTimeOffset ExpiresAt,
        bool LeaderChoice = false);

    private sealed record PendingBreak(uint RequesterTeamId, uint RequesterCharacterId);

    private sealed record PendingSummon(
        uint RecipientId,
        uint SummonerId,
        string SummonerName,
        DateTimeOffset ExpiresAt);

    private sealed class JointSession(TeamJointRoster roster)
    {
        public TeamJointRoster Roster { get; } = roster;
        public uint JointId => Roster.JointId;
        public bool Contains(uint teamId) => Roster.Contains(teamId);
        public uint GetOtherTeamId(uint teamId) => Roster.GetOtherTeamId(teamId);
    }
}
