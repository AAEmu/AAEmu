using AAEmu.Commons.Utils;
using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Managers;

public class TeamManager(IWorldManager worldManager, IChatManager chatManager, ITeamIdManager teamIdManager, ITickManager tickManager) : Singleton<TeamManager>, ITeamManager
{
    private readonly ConcurrentDictionary<uint, Team> _activeTeams = []; // teamId, Team
    private readonly Dictionary<uint, InvitationTemplate> _activeInvitations = []; // targetId, InvitationTemplate
    private readonly Dictionary<uint, OwnerHandoverOffer> _ownerHandoverOffers = []; // teamId, offer
    private long _lastInvitationLogEventId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(1);

    public Team GetActiveTeamByUnit(uint unitId)
    {
        foreach (var team in _activeTeams.Values)
        {
            foreach (var member in team.Members)
            {
                if (member == null)
                    continue;
                if (member.Character.Id == unitId)
                    return team;
            }
        }

        return null;
    }

    public Team GetTeamByObjId(uint objId)
    {
        foreach (var team in _activeTeams.Values)
        {
            foreach (var member in team.Members)
            {
                if (member == null)
                    continue;
                if (member.Character.ObjId == objId)
                    return team;
            }
        }

        return null;
    }

    public Team GetActiveTeam(uint teamId)
    {
        if (teamId == 0) return null;
        return _activeTeams.TryGetValue(teamId, out var team) ? team : null;
    }

    public bool AreTeamMembers(uint unit1, uint unit2)
    {
        var team = GetActiveTeamByUnit(unit1);
        return team?.IsMember(unit2) ?? false;
    }

    private InvitationTemplate GetActiveInvitation(uint targetId)
    {
        return _activeInvitations.TryGetValue(targetId, out var invitation) ? invitation : null;
    }

    public void InviteAreaToTeam(Character owner, int teamId, bool isParty)
    {
        if (owner == null || teamId < 0)
            return;

        RemoveExpiredInvitations();

        var activeTeam = teamId == 0 ? null : GetActiveTeam((uint)teamId);
        if (teamId != 0 && activeTeam == null)
            return;

        var ownerTeam = GetActiveTeamByUnit(owner.Id);
        if (ownerTeam != activeTeam || (activeTeam != null && activeTeam.IsParty != isParty) ||
            (activeTeam != null && !CanInvite(activeTeam, owner)))
            return;

        var teamRole = activeTeam?.RoleType ?? (isParty ? TeamRoleType.Party : TeamRoleType.Raid);
        var memberCount = activeTeam?.MembersCount() ?? 1;
        var pendingCount = CountPendingInvitations(owner.Id, activeTeam?.Id ?? 0u, teamRole);
        var memberLimit = activeTeam?.MemberLimit ?? GetMemberLimit(teamRole);
        var availableSlots = Math.Max(0, memberLimit - memberCount - pendingCount);
        var invited = 0;

        // Area invitation follows the Zone's authoritative AOI/neighbor set. There is no team-invite
        // radius in game.compact, and using the same visible region set avoids a guessed distance.
        foreach (var character in WorldManager.GetAround<Character>(owner))
        {
            if (invited >= availableSlots)
                break;
            if (character.Id == owner.Id || !character.IsOnline || GetActiveTeamByUnit(character.Id) != null ||
                GetActiveInvitation(character.Id) != null ||
                owner.GetRelationStateTo(character) == RelationState.Hostile)
                continue;

            if (AskToJoin(owner, character.Name, teamId, teamRole, CharacterBlocked.LocalWorldId, character, true))
                invited++;
        }

        owner.SendPacket(new SCTeamAreaInvitedPacket(
            availableSlots - invited, invited > 0));
    }

    public bool AskToJoin(
        Character owner,
        string targetName,
        int teamId,
        TeamRoleType teamRole,
        sbyte worldId,
        Character targetObj = null,
        bool isArea = false)
    {
        if (owner == null || teamId < 0 || teamRole is not (TeamRoleType.Party or TeamRoleType.Raid))
            return false;

        RemoveExpiredInvitations();

        // 0xFF is the native local-world sentinel. An explicit local shard id is also accepted;
        // remote shards require inter-world routing that this World process does not own.
        if (worldId != CharacterBlocked.LocalWorldId && unchecked((byte)worldId) != AppConfiguration.Instance.Id)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteeOffline);
            return false;
        }

        var target = targetObj ?? worldManager.GetCharacter(targetName);
        if (target == null || !target.IsOnline)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteeOffline);
            return false;
        }

        if (target.Id == owner.Id)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteeMember);
            return false;
        }

        // Only hostile players cannot be invited (friendly and neutral are allowed, supports custom nations)
        if (owner.GetRelationStateTo(target) == RelationState.Hostile)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteRefused);
            return false;
        }

        var activeTeam = teamId == 0 ? null : GetActiveTeam((uint)teamId);
        var ownerTeam = GetActiveTeamByUnit(owner.Id);
        if ((teamId != 0 && activeTeam == null) || ownerTeam != activeTeam)
            return false;

        if (activeTeam != null && (activeTeam.RoleType != teamRole || !CanInvite(activeTeam, owner)))
            return false;

        if (GetActiveTeamByUnit(target.Id) != null)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteeInTeam);
            return false;
        }

        if (GetActiveInvitation(target.Id) != null)
        {
            owner.SendPacket(new SCRejectedTeamPacket(target.Name, teamRole == TeamRoleType.Party));
            return false;
        }

        var effectiveTeamId = activeTeam?.Id ?? 0u;
        var memberCount = activeTeam?.MembersCount() ?? 1;
        var pendingCount = CountPendingInvitations(owner.Id, effectiveTeamId, teamRole);
        var memberLimit = activeTeam?.MemberLimit ?? GetMemberLimit(teamRole);
        if (memberCount + pendingCount >= memberLimit)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamFull);
            return false;
        }

        var logEventId = isArea ? 0L : Interlocked.Increment(ref _lastInvitationLogEventId);
        _activeInvitations.Add(target.Id, new InvitationTemplate
        {
            Owner = owner,
            Target = target,
            IsArea = isArea,
            TeamRole = teamRole,
            LogEventId = logEventId,
            Time = DateTime.UtcNow,
            TeamId = effectiveTeamId
        });

        if (isArea)
            target.SendPacket(new SCAskToJoinTeamAreaPacket((int)effectiveTeamId, owner.Id, owner.Name, teamRole));
        else
            target.SendPacket(new SCAskToJoinTeamPacket((int)effectiveTeamId, owner.Id, owner.Name, teamRole, logEventId));
        return true;
    }

    public void ReplyToJoinTeam(
        Character target,
        int teamId,
        bool isParty,
        ulong ownerId,
        bool isReject,
        string charName,
        bool isArea,
        TeamRoleType teamRole,
        long logEventId)
    {
        if (target == null)
            return;

        var activeInvitation = GetActiveInvitation(target.Id);
        if (activeInvitation == null)
            return;

        var replyMatchesInvitation = teamId >= 0 && (uint)teamId == activeInvitation.TeamId &&
                                     ownerId == activeInvitation.Owner.Id &&
                                     string.Equals(charName, target.Name, StringComparison.Ordinal) &&
                                     isArea == activeInvitation.IsArea &&
                                     teamRole == activeInvitation.TeamRole &&
                                     isParty == (activeInvitation.TeamRole == TeamRoleType.Party) &&
                                     logEventId == activeInvitation.LogEventId;
        if (!replyMatchesInvitation)
        {
            _activeInvitations.Remove(target.Id);
            return;
        }

        if (isReject || activeInvitation.Time + InvitationLifetime < DateTime.UtcNow)
        {
            activeInvitation.Owner.SendPacket(new SCRejectedTeamPacket(
                activeInvitation.Target.Name, activeInvitation.TeamRole == TeamRoleType.Party));
            _activeInvitations.Remove(target.Id);
            return;
        }

        if (GetActiveTeamByUnit(target.Id) != null)
        {
            target.SendErrorMessage(ErrorMessageType.TeamInviteeInTeam);
            _activeInvitations.Remove(target.Id);
            return;
        }

        var activeTeam = GetActiveTeamByUnit(activeInvitation.Owner.Id);
        if (activeTeam == null)
        {
            if (activeInvitation.TeamId == 0)
            {
                CreateNewTeam(activeInvitation);
            }
            else
            {
                _activeInvitations.Remove(target.Id);
                return;
            }
        }
        else
        {
            if ((activeInvitation.TeamId != 0 && activeInvitation.TeamId != activeTeam.Id) ||
                activeTeam.RoleType != activeInvitation.TeamRole ||
                !CanInvite(activeTeam, activeInvitation.Owner))
            {
                _activeInvitations.Remove(target.Id);
                return;
            }

            if (activeTeam.MembersCount() >= activeTeam.MemberLimit)
            {
                target.SendErrorMessage(ErrorMessageType.TeamFull);
                _activeInvitations.Remove(activeInvitation.Target.Id);
                return;
            }

            var (newTeamMember, party) = activeTeam.AddMember(target);
            if (newTeamMember != null)
            {
                target.SendPacket(new SCJoinedTeamPacket(activeTeam));
                if (activeTeam.OfficerId != 0)
                    target.SendPacket(new SCTeamOfficerChangedPacket((int)activeTeam.Id, activeTeam.OfficerId));
                target.InParty = true;
                target.SendPacket(new SCTeamPingPosPacket(true, activeTeam.PingPosition, 0));
                activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newTeamMember, party), target.Id);
                if (!activeTeam.IsParty)
                    chatManager.GetRaidChat(activeTeam).JoinChannel(target);
                chatManager.GetPartyChat(activeTeam, target).JoinChannel(target);
                target.Events?.OnTeamJoin(
                    activeInvitation,
                    new OnTeamJoinArgs { Team = activeTeam, Player = target });
            }
        }

        _activeInvitations.Remove(activeInvitation.Target.Id);
    }

    private static bool CanInvite(Team team, Character character)
    {
        return team.IsMember(character.Id) &&
               (team.OwnerId == character.Id || !team.IsParty && team.IsOfficer(character.Id));
    }

    private int CountPendingInvitations(uint ownerId, uint teamId, TeamRoleType teamRole)
    {
        return _activeInvitations.Values.Count(invitation =>
            invitation.Owner.Id == ownerId && invitation.TeamId == teamId && invitation.TeamRole == teamRole);
    }

    private void RemoveExpiredInvitations()
    {
        var now = DateTime.UtcNow;
        var expired = _activeInvitations
            .Where(pair => pair.Value.Time + InvitationLifetime < now)
            .ToArray();
        foreach (var (targetId, invitation) in expired)
        {
            _activeInvitations.Remove(targetId);
            if (invitation.Owner.IsOnline)
                invitation.Owner.SendPacket(new SCRejectedTeamPacket(
                    invitation.Target.Name, invitation.TeamRole == TeamRoleType.Party));
        }
    }

    private static int GetMemberLimit(TeamRoleType teamRole)
    {
        return teamRole == TeamRoleType.Party ? Team.PartyMemberLimit : Team.RaidMemberLimit;
    }

    public void MoveTeamMember(
        Character owner,
        int teamId,
        ulong memberId,
        ulong otherMemberId,
        sbyte memberIndex,
        sbyte otherIndex,
        bool ghostSwap)
    {
        if (owner == null || teamId <= 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam == null || activeTeam.OwnerId != owner.Id)
            return;

        if (ghostSwap && (memberId == 0) == (otherMemberId == 0))
            return;

        var slotCount = activeTeam.MemberLimit;
        if (memberIndex < 0 || memberIndex >= slotCount || otherIndex < 0 || otherIndex >= slotCount || memberIndex == otherIndex)
            return;

        var t1 = activeTeam.Members[memberIndex]?.Character;
        var t2 = activeTeam.Members[otherIndex]?.Character;
        var currentMemberId = (ulong)(t1?.Id ?? 0u);
        var currentOtherMemberId = (ulong)(t2?.Id ?? 0u);
        if (currentMemberId != memberId || currentOtherMemberId != otherMemberId || memberId == 0 && otherMemberId == 0)
            return;

        if (t1 != null)
            chatManager.GetPartyChat(activeTeam, t1).LeaveChannel(t1);
        if (t2 != null)
            chatManager.GetPartyChat(activeTeam, t2).LeaveChannel(t2);

        if (activeTeam.MoveMember(memberId, otherMemberId, memberIndex, otherIndex))
        {
            activeTeam.BroadcastPacket(new SCTeamMemberMovedPacket(teamId, memberId, otherMemberId, memberIndex, otherIndex, ghostSwap));
            if (t1 != null)
                chatManager.GetPartyChat(activeTeam, t1).JoinChannel(t1);
            if (t2 != null)
                chatManager.GetPartyChat(activeTeam, t2).JoinChannel(t2);
        }
    }

    public Character GetNextEligibleLooter(uint teamId, Unit owner)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null) return null;

        //Round Robin vs FFA
        //if(activeTeam.LootingRule==)
        foreach (var member in activeTeam.Members)
        {
            if (member?.Character == null)
                continue;
            if (member.HasGoneRoundRobin)
                continue;
            //Need to check if player is in range, and skip if not.
            var distance = member.Character.Transform.World.Position - owner.Transform.World.Position;
            if (distance.Length() >= 200)
                continue;

            member.HasGoneRoundRobin = true;
            return member.Character;
        }

        // Reset round robin and get the first eligible member
        Character returnMember = null;
        foreach (var member in activeTeam.Members)
        {
            if (member?.Character == null)
                continue;

            member.HasGoneRoundRobin = returnMember == null;
            if (returnMember == null)
                returnMember = member.Character;
        }

        return returnMember;
    }

    public void CreateNewTeam(InvitationTemplate activeInvitation)
    {
        if (GetActiveTeamByUnit(activeInvitation.Owner.Id) != null)
        {
            activeInvitation.Target.SendErrorMessage(ErrorMessageType.TeamInvitorMoved);
            return;
        }

        if (GetActiveTeamByUnit(activeInvitation.Target.Id) != null)
        {
            activeInvitation.Owner.SendErrorMessage(ErrorMessageType.TeamInviteeInTeam);
            return;
        }

        var teamId = teamIdManager.GetNextId();
        var newTeam = new Team
        {
            Id = teamId,
            OwnerId = activeInvitation.Owner.Id,
            IsParty = activeInvitation.TeamRole == TeamRoleType.Party
        };
        if (newTeam.AddMember(activeInvitation.Owner).Item1 == null ||
            newTeam.AddMember(activeInvitation.Target).Item1 == null)
        {
            teamIdManager.ReleaseId(teamId);
            return;
        }

        if (!_activeTeams.TryAdd(newTeam.Id, newTeam))
        {
            teamIdManager.ReleaseId(teamId);
            return;
        }

        activeInvitation.Owner.SendPacket(new SCJoinedTeamPacket(newTeam));
        activeInvitation.Owner.InParty = true;
        activeInvitation.Target.SendPacket(new SCJoinedTeamPacket(newTeam));
        activeInvitation.Target.InParty = true;
        newTeam.BroadcastPacket(new SCTeamPingPosPacket(true, activeInvitation.Owner.LocalPingPosition, 0));
        if (!newTeam.IsParty)
        {
            chatManager.GetRaidChat(newTeam).JoinChannel(activeInvitation.Owner);
            chatManager.GetRaidChat(newTeam).JoinChannel(activeInvitation.Target);
        }
        chatManager.GetPartyChat(newTeam, activeInvitation.Owner).JoinChannel(activeInvitation.Owner);
        chatManager.GetPartyChat(newTeam, activeInvitation.Target).JoinChannel(activeInvitation.Target);
        // Trigger events
        activeInvitation.Owner.Events?.OnTeamJoin(activeInvitation, new OnTeamJoinArgs { Team = newTeam, Player = activeInvitation.Owner });
        activeInvitation.Target.Events?.OnTeamJoin(activeInvitation, new OnTeamJoinArgs { Team = newTeam, Player = activeInvitation.Target });
    }

    public void CreateSoloTeam(Character character, bool asParty)
    {
        if (GetActiveTeamByUnit(character.Id) != null)
        {
            character.SendErrorMessage(ErrorMessageType.TeamInviteeInTeam);
            return;
        }

        var teamId = teamIdManager.GetNextId();
        var newTeam = new Team
        {
            Id = teamId,
            OwnerId = character.Id,
            IsParty = asParty
        };
        if (newTeam.AddMember(character).Item1 == null)
        {
            teamIdManager.ReleaseId(teamId);
            return;
        }

        if (!_activeTeams.TryAdd(newTeam.Id, newTeam))
        {
            teamIdManager.ReleaseId(teamId);
            return;
        }

        character.SendPacket(new SCJoinedTeamPacket(newTeam));
        character.InParty = true;
        newTeam.BroadcastPacket(new SCTeamPingPosPacket(true, character.LocalPingPosition, 0));

        if (!newTeam.IsParty)
            chatManager.GetRaidChat(newTeam).JoinChannel(character);
        chatManager.GetPartyChat(newTeam, character).JoinChannel(character);
        // Trigger events
        character.Events?.OnTeamJoin(character, new OnTeamJoinArgs { Team = newTeam, Player = character });
    }

    public void AskRiskyTeam(Character requester, int teamId, ulong targetId, RiskyAction riskyAction)
    {
        if (requester == null || teamId <= 0 || !Enum.IsDefined(riskyAction))
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        var error = ValidateRiskyAction(activeTeam, requester, targetId, riskyAction);

        // the client emits CSLeaveTeam/CSKickTeamMember/CSDismissTeam immediately; those packets
        // are independently validated again by the Zone.
        requester.SendPacket(new SCTeamAckRiskyActionPacket(
            teamId, targetId, riskyAction, TeamRiskyWarningFlags.None, error));
    }

    public void LeaveTeam(Character requester, int teamId)
    {
        if (requester == null || teamId <= 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (ValidateRiskyAction(activeTeam, requester, requester.Id, RiskyAction.Leave) !=
            ErrorMessageType.NoErrorMessage)
            return;

        RemoveTeamMember(activeTeam, requester, false);
    }

    public void KickTeamMember(Character requester, int teamId, ulong targetId)
    {
        if (requester == null || teamId <= 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (ValidateRiskyAction(activeTeam, requester, targetId, RiskyAction.Kick) !=
            ErrorMessageType.NoErrorMessage)
            return;

        var target = activeTeam.Members[activeTeam.GetIndex((uint)targetId)].Character;
        RemoveTeamMember(activeTeam, target, true);
    }

    public void DismissTeam(Character requester, int teamId)
    {
        if (requester == null || teamId <= 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (ValidateRiskyAction(activeTeam, requester, requester.Id, RiskyAction.Dismiss) !=
            ErrorMessageType.NoErrorMessage)
            return;

        DisbandTeam(activeTeam);
        chatManager.CleanUpChannels();
    }

    private static ErrorMessageType ValidateRiskyAction(
        Team activeTeam,
        Character requester,
        ulong targetId,
        RiskyAction riskyAction)
    {
        if (activeTeam == null || !activeTeam.IsMember(requester.Id))
            return ErrorMessageType.TeamNoSuchMember;

        return riskyAction switch
        {
            RiskyAction.Leave when targetId != requester.Id => ErrorMessageType.TeamYourself,
            RiskyAction.Leave => ErrorMessageType.NoErrorMessage,
            RiskyAction.Kick when activeTeam.OwnerId != requester.Id => ErrorMessageType.TeamNoRights,
            RiskyAction.Kick when targetId == requester.Id => ErrorMessageType.TeamYourself,
            RiskyAction.Kick when targetId > uint.MaxValue || !activeTeam.IsMember((uint)targetId) =>
                ErrorMessageType.TeamNoSuchMember,
            RiskyAction.Kick => ErrorMessageType.NoErrorMessage,
            RiskyAction.Dismiss when activeTeam.OwnerId != requester.Id => ErrorMessageType.TeamNoRights,
            RiskyAction.Dismiss when targetId != requester.Id => ErrorMessageType.TeamYourself,
            RiskyAction.Dismiss => ErrorMessageType.NoErrorMessage,
            _ => ErrorMessageType.Invalid
        };
    }

    private void RemoveTeamMember(Team activeTeam, Character target, bool kicked)
    {
        _ownerHandoverOffers.Remove(activeTeam.Id);
        var wasOwner = activeTeam.OwnerId == target.Id;
        if (!activeTeam.RemoveMember(target.Id))
            return;

        if (!activeTeam.IsParty)
            chatManager.GetRaidChat(activeTeam).LeaveChannel(target);
        chatManager.GetPartyChat(activeTeam, target).LeaveChannel(target);

        target.InParty = false;
        if (target.IsOnline)
            target.SendPacket(new SCLeavedTeamPacket((int)activeTeam.Id, kicked, false));
        activeTeam.BroadcastPacket(new SCTeamMemberLeavedPacket((int)activeTeam.Id, target.Id, kicked));
        target.Events?.OnTeamLeave(
            target,
            new OnTeamLeaveArgs { Id = activeTeam.Id, Team = activeTeam, Player = target });

        var shouldDisband = (activeTeam.IsParty && activeTeam.MembersCount() <= 1) ||
                            activeTeam.MembersOnlineCount() <= 0;
        if (!shouldDisband && wasOwner)
        {
            var newOwner = activeTeam.GetNewOwner();
            if (newOwner == 0)
                shouldDisband = true;
            else
            {
                activeTeam.OwnerId = newOwner;
                if (activeTeam.OfficerId == newOwner)
                    activeTeam.OfficerId = 0;
                activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(checked((int)activeTeam.Id), newOwner));
            }
        }

        if (shouldDisband)
            DisbandTeam(activeTeam);
        chatManager.CleanUpChannels();
    }

    private void DisbandTeam(Team activeTeam)
    {
        _ownerHandoverOffers.Remove(activeTeam.Id);
        activeTeam.BroadcastPacket(new SCTeamDismissedPacket((int)activeTeam.Id));
        foreach (var member in activeTeam.Members)
        {
            var character = member?.Character;
            if (character == null)
                continue;

            if (!activeTeam.IsParty)
                chatManager.GetRaidChat(activeTeam).LeaveChannel(character);
            chatManager.GetPartyChat(activeTeam, character).LeaveChannel(character);

            character.InParty = false;
            if (character.IsOnline)
                character.SendPacket(new SCLeavedTeamPacket((int)activeTeam.Id, false, true));
            character.Events?.OnTeamLeave(
                character,
                new OnTeamLeaveArgs { Id = activeTeam.Id, Team = activeTeam, Player = character });
        }

        if (_activeTeams.TryRemove(activeTeam.Id, out _))
            teamIdManager.ReleaseId(activeTeam.Id);
    }

    public void MakeTeamOwner(Character unit, int teamId, ulong memberId)
    {
        if (unit == null || teamId <= 0 || memberId > uint.MaxValue || memberId == unit.Id)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam?.OwnerId != unit.Id || !activeTeam.IsMember((uint)memberId))
            return;

        _ownerHandoverOffers.Remove(activeTeam.Id);
        activeTeam.OwnerId = (uint)memberId;
        if (activeTeam.OfficerId == memberId)
            activeTeam.OfficerId = 0;
        activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(teamId, memberId));
    }

    public bool BeginOwnerHandover(int teamId, ulong candidateId, TeamOwnerHandoverDetails details)
    {
        if (teamId <= 0 || candidateId > uint.MaxValue ||
            details.Reason is < TeamOwnerHandoverReason.HigherHeroGrade or > TeamOwnerHandoverReason.GearScore)
            return false;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam == null || activeTeam.IsParty || activeTeam.OwnerId == candidateId ||
            !activeTeam.IsMember((uint)candidateId) || _ownerHandoverOffers.ContainsKey(activeTeam.Id))
            return false;

        var owner = GetTeamCharacter(activeTeam, activeTeam.OwnerId);
        var candidate = GetTeamCharacter(activeTeam, (uint)candidateId);
        if (owner is not { IsOnline: true } || candidate is not { IsOnline: true })
            return false;

        var offer = new OwnerHandoverOffer(activeTeam.OwnerId, (uint)candidateId, details);
        _ownerHandoverOffers.Add(activeTeam.Id, offer);
        owner.SendPacket(new SCTeamAskHandOverOwnerPacket(teamId, candidateId, details));
        return true;
    }

    public void RespondToOwnerHandover(
        Character responder,
        int teamId,
        ulong ownerId,
        ulong candidateId,
        sbyte reason,
        bool accept,
        bool ownerResponse)
    {
        if (responder == null || teamId <= 0 || ownerId > uint.MaxValue || candidateId > uint.MaxValue ||
            !_ownerHandoverOffers.TryGetValue((uint)teamId, out var offer))
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        var offerStillValid = activeTeam != null && activeTeam.OwnerId == offer.OwnerId &&
                              activeTeam.IsMember(offer.OwnerId) && activeTeam.IsMember(offer.CandidateId);
        if (!offerStillValid)
        {
            _ownerHandoverOffers.Remove((uint)teamId);
            return;
        }

        if (ownerId != offer.OwnerId || candidateId != offer.CandidateId ||
            reason != (sbyte)offer.Details.Reason)
            return;

        var owner = GetTeamCharacter(activeTeam, offer.OwnerId);
        var candidate = GetTeamCharacter(activeTeam, offer.CandidateId);
        if (ownerResponse)
        {
            if (offer.Stage != OwnerHandoverStage.AwaitingOwner || responder.Id != offer.OwnerId)
                return;

            if (!accept || candidate is not { IsOnline: true })
            {
                _ownerHandoverOffers.Remove(activeTeam.Id);
                return;
            }

            offer.Stage = OwnerHandoverStage.AwaitingCandidate;
            candidate.SendPacket(new SCTeamAskAcceptOwnerOfferPacket(teamId, candidateId, offer.Details));
            return;
        }

        if (offer.Stage != OwnerHandoverStage.AwaitingCandidate || responder.Id != offer.CandidateId)
            return;

        _ownerHandoverOffers.Remove(activeTeam.Id);
        var result = new SCTeamHandOverOwnerOfferResultPacket(teamId, candidateId, accept);
        if (owner is { IsOnline: true })
            owner.SendPacket(result);
        if (candidate is { IsOnline: true })
            candidate.SendPacket(result);

        if (!accept)
            return;

        activeTeam.OwnerId = offer.CandidateId;
        if (activeTeam.OfficerId == offer.CandidateId)
            activeTeam.OfficerId = 0;
        activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(teamId, candidateId));
    }

    private static Character GetTeamCharacter(Team team, uint characterId)
    {
        var index = team.GetIndex(characterId);
        return index < 0 ? null : team.Members[index]?.Character;
    }

    public void MakeTeamOfficer(Character unit, int teamId, ulong memberId)
    {
        if (unit == null || teamId <= 0 || memberId > uint.MaxValue || memberId == unit.Id)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam == null || activeTeam.IsParty || activeTeam.OwnerId != unit.Id)
            return;

        if (!activeTeam.IsMember((uint)memberId))
            return;

        activeTeam.OfficerId = memberId;
        activeTeam.BroadcastPacket(new SCTeamOfficerChangedPacket(teamId, memberId));
    }

    public void ConvertToRaid(Character owner, int teamId)
    {
        if (owner == null || teamId <= 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam == null || !activeTeam.IsParty || activeTeam.OwnerId != owner.Id)
            return;

        activeTeam.IsParty = false;
        activeTeam.BroadcastPacket(new SCTeamBecameRaidTeamPacket(teamId));
        foreach (var m in activeTeam.Members)
            if (m?.Character != null)
                chatManager.GetRaidChat(activeTeam).JoinChannel(m.Character);

        // Dungeons retain the Team object and compare its stable Id, so in-place role conversion
        // preserves the owning team and every member's existing instance access.
    }

    public void SetTeamMemberRole(Character unit, uint teamId, uint memberId, MemberRole role)
    {
        if (!Enum.IsDefined(typeof(MemberRole), role)) role = MemberRole.Undecided;
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || unit.Id != memberId) return;

        if (activeTeam.ChangeRole(memberId, role))
        {
            activeTeam.BroadcastPacket(new SCTeamMemberRoleChangedPacket(activeTeam.Id, memberId, role));
        }
    }

    public void SetOverHeadMarker(Character unit, uint teamId, OverHeadMark index, byte type, uint targetId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || !activeTeam.IsParty && activeTeam.OwnerId != unit.Id && !activeTeam.IsOfficer(unit.Id)) return;

        if (Enum.IsDefined(typeof(OverHeadMark), index) && index != OverHeadMark.ResetAll && type <= 2)
        {
            activeTeam.MarksList[(int)index].Item1 = type;
            activeTeam.MarksList[(int)index].Item2 = type != 0 ? targetId : 0u;
        }
        else
        {
            activeTeam.ResetMarks();
            index = OverHeadMark.ResetAll;
            type = 100;
            targetId = 0;
        }

        activeTeam.BroadcastPacket(new SCOverHeadMarkerSetPacket(teamId, index, type == 2, targetId));
    }

    public void ChangeLootingRule(
        Character owner,
        int teamId,
        LootingRuleChangeFlags flags,
        LootingRuleMethod lootingRuleMethod,
        sbyte minimumGrade,
        ulong lootMaster,
        bool rollForBindOnPickup)
    {
        if (owner == null || teamId <= 0 || flags == LootingRuleChangeFlags.None ||
            (flags & ~LootingRuleChangeFlags.All) != 0)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        if (activeTeam?.OwnerId != owner.Id)
            return;

        if (flags.HasFlag(LootingRuleChangeFlags.Method) &&
            lootingRuleMethod is < LootingRuleMethod.FreeForAll or > LootingRuleMethod.LootMaster)
            return;
        if (flags.HasFlag(LootingRuleChangeFlags.MinimumGrade) &&
            minimumGrade is < (sbyte)ItemGrade.Crude or > (sbyte)ItemGrade.Eternal)
            return;
        if (flags.HasFlag(LootingRuleChangeFlags.LootMaster) &&
            (lootMaster == 0 || lootMaster > uint.MaxValue || !activeTeam.IsMember((uint)lootMaster)))
            return;

        var finalMethod = flags.HasFlag(LootingRuleChangeFlags.Method)
            ? lootingRuleMethod
            : activeTeam.LootingRule.LootMethod;
        var finalLootMaster = flags.HasFlag(LootingRuleChangeFlags.LootMaster)
            ? lootMaster
            : activeTeam.LootingRule.LootMaster;
        if (finalMethod == LootingRuleMethod.LootMaster &&
            (finalLootMaster == 0 || finalLootMaster > uint.MaxValue || !activeTeam.IsMember((uint)finalLootMaster)))
            return;

        if (flags.HasFlag(LootingRuleChangeFlags.RollForBindOnPickup))
            activeTeam.LootingRule.RollForBindOnPickup = rollForBindOnPickup;
        if (flags.HasFlag(LootingRuleChangeFlags.MinimumGrade))
            activeTeam.LootingRule.MinimumGrade = minimumGrade;
        if (flags.HasFlag(LootingRuleChangeFlags.Method))
            activeTeam.LootingRule.LootMethod = lootingRuleMethod;
        activeTeam.LootingRule.LootMaster = finalMethod == LootingRuleMethod.LootMaster ? finalLootMaster : 0;

        activeTeam.BroadcastPacket(new SCTeamLootingRuleChangedPacket(teamId, activeTeam.LootingRule, flags));
    }

    public void ChangeDiceBidRule(Character unit, int teamId, ulong memberId, DiceBidRuleKind rule, bool byIdleState)
    {
        if (unit == null || teamId <= 0 || memberId != unit.Id)
            return;

        if (rule is < DiceBidRuleKind.Default or > DiceBidRuleKind.AutoGiveUp)
            return;

        // The native idle transition only sends Default when activity resumes and AutoGiveUp when idle starts.
        if (byIdleState && rule == DiceBidRuleKind.AutoAccept)
            return;

        var activeTeam = GetActiveTeam((uint)teamId);
        var memberIndex = activeTeam?.GetIndex(unit.Id) ?? -1;
        if (memberIndex < 0)
            return;

        var member = activeTeam.Members[memberIndex];
        member.DiceBidRule = rule;
        member.DiceBidRuleChangedByIdleState = byIdleState;
        activeTeam.BroadcastPacket(new SCDiceBidRuleChangedPacket(teamId, memberId, rule));
    }

    public void SetPingPos(Character unit, uint teamId, bool hasPing, WorldSpawnPosition position, uint insId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || (activeTeam.OwnerId != unit.Id && !activeTeam.IsOfficer(unit.Id)))
            return;

        activeTeam.PingPosition = position;
        activeTeam.BroadcastPacket(new SCTeamPingPosPacket(hasPing, position, insId));
    }

    public void SetOffline(Character unit)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        var memberInfo = activeTeam?.ChangeStatus(unit);
        if (memberInfo == null) return;

        if (_ownerHandoverOffers.TryGetValue(activeTeam.Id, out var offer) &&
            (offer.OwnerId == unit.Id || offer.CandidateId == unit.Id))
            _ownerHandoverOffers.Remove(activeTeam.Id);

        if (activeTeam.OwnerId == unit.Id)
        {
            var newOwner = activeTeam.GetNewOwner();
            if (newOwner != 0)
            {
                activeTeam.OwnerId = newOwner;
                if (activeTeam.OfficerId == newOwner)
                    activeTeam.OfficerId = 0;
                activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(checked((int)activeTeam.Id), newOwner), unit.Id);
            }
        }

        activeTeam.BroadcastPacket(new SCTeamMemberDisconnectedPacket(activeTeam.Id, unit.Id, memberInfo));
    }

    public void MemberRemoveFromTeam(Character unit, Character source, RiskyAction leaveType)
    {
        if (unit == null || source == null || leaveType is not (RiskyAction.Leave or RiskyAction.Kick))
            return;

        var activeTeam = GetActiveTeamByUnit(unit.Id);
        var sourceTeam = GetActiveTeamByUnit(source.Id);
        if (activeTeam == null || sourceTeam != activeTeam ||
            (unit.Id != source.Id && source.Id != activeTeam.OwnerId))
            return;

        RemoveTeamMember(activeTeam, unit, leaveType == RiskyAction.Kick);
    }

    private void SendRemoteMemberUpdates(TimeSpan _)
    {
        foreach (var activeTeam in _activeTeams.Values)
        {
            var members = activeTeam.Members
                .Where(member => member?.Character != null)
                .ToArray();
            if (members.Length <= 1)
                continue;

            foreach (var recipient in members)
            {
                if (!recipient.Character.IsOnline)
                    continue;

                var remoteMembers = members
                    .Where(member => member.Character.Id != recipient.Character.Id)
                    .ToArray();
                recipient.Character.SendPacket(new SCTeamRemoteMembersExPacket(
                    checked((int)activeTeam.Id), remoteMembers));
            }
        }
    }

    public void UpdateAtLogin(Character unit)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        if (activeTeam == null) return;

        var newInfo = activeTeam.ChangeStatus(unit);
        unit.SendPacket(new SCJoinedTeamPacket(activeTeam));
        if (activeTeam.OfficerId != 0)
            unit.SendPacket(new SCTeamOfficerChangedPacket((int)activeTeam.Id, activeTeam.OfficerId));
        unit.InParty = true;
        activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newInfo, Team.GetParty(activeTeam.GetIndex(unit.Id))));
        //activeTeam.BroadcastPacket(new SCRefreshTeamMemberPacket(activeTeam.Id, unit.Id, unit.ObjId));
        if (!activeTeam.IsParty)
            chatManager.GetRaidChat(activeTeam).JoinChannel(unit);
        chatManager.GetPartyChat(activeTeam, unit).JoinChannel(unit);
    }

    public void Load()
    {
        LootingRule.ValidateDefaults(AppConfiguration.Instance.World);
        tickManager.OnTick.Subscribe(
            SendRemoteMemberUpdates,
            TimeSpan.FromMilliseconds(AppConfiguration.Instance.World.TeamRemoteMemberUpdateIntervalMilliseconds));
    }
}

public class InvitationTemplate
{
    public uint TeamId { get; set; }
    public Character Owner { get; set; }
    public Character Target { get; set; }
    public DateTime Time { get; set; }
    public bool IsArea { get; set; }
    public TeamRoleType TeamRole { get; set; }
    public long LogEventId { get; set; }
}

internal enum OwnerHandoverStage
{
    AwaitingOwner,
    AwaitingCandidate
}

internal sealed class OwnerHandoverOffer(
    uint ownerId,
    uint candidateId,
    TeamOwnerHandoverDetails details)
{
    public uint OwnerId { get; } = ownerId;
    public uint CandidateId { get; } = candidateId;
    public TeamOwnerHandoverDetails Details { get; } = details;
    public OwnerHandoverStage Stage { get; set; } = OwnerHandoverStage.AwaitingOwner;
}
