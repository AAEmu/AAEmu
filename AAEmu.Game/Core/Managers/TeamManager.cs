using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Managers;

public class TeamManager : Singleton<TeamManager>
{
    /*
     * TODO:
     *
     * RE-DO LEAVE / KICK / DISMISS
     */

    private Dictionary<uint, Team> _activeTeams; // teamId, Team
    private Dictionary<uint, InvitationTemplate> _activeInvitations; // targetId, InvitationTemplate

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

    public void InviteAreaToTeam(Character owner, uint teamId, bool isParty)
    {
        var characters = WorldManager.GetAround<Character>(owner, 100.0f); // CHECK IF 100m
        if (characters.Count <= 0) return;

        foreach (var character in characters)
        {
            if (!character.InParty)
                AskToJoin(owner, "", teamId, false, character);
        }
    }

    public void AskToJoin(Character owner, string targetName, uint teamId, bool isParty, Character targetObj = null)
    {
        var target = targetObj ?? WorldManager.Instance.GetCharacter(targetName);
        if (target == null) return;
        // TODO - CONFIG INVITE DISABLED

        var activeTeam = GetActiveTeam(teamId);
        var activeInvitation = GetActiveInvitation(target.Id);
        if (activeInvitation != null)
        {
            owner.SendPacket(new SCRejectedTeamPacket(targetName, isParty));
            return;
        }

        var isAllowed = false;
        if (activeTeam != null)
        {
            var isOwner = activeTeam.OwnerId == owner.Id;
            if (isOwner) isAllowed = true;

            if (!activeTeam.IsParty && !isAllowed)
            {
                var isMarked = activeTeam.IsMarked(owner.Id);
                if (isMarked) isAllowed = true;
            }

            if (!isAllowed)
            {
                // TODO - ERROR NOT ALLOWED TO INVITE
                return;
            }
        }

        _activeInvitations.TryAdd(target.Id, new InvitationTemplate
        {
            Owner = owner,
            Target = target,
            IsParty = activeTeam?.IsParty ?? isParty,
            Time = DateTime.UtcNow,
            TeamId = activeTeam?.Id ?? 0u,
        });
        target.SendPacket(new SCAskToJoinTeamPacket(activeTeam?.Id ?? 0u, owner.Id, owner.Name, isParty));
    }

    public void ReplyToJoinTeam(Character target, uint teamId, bool isParty, uint ownerId, bool isReject, string charName, bool isArea)
    {
        var activeInvitation = GetActiveInvitation(target.Id);
        if (activeInvitation == null)
        {
            // TODO - ERROR NO INVITATION
            return;
        }

        if (isReject || activeInvitation.Time.AddSeconds(60) < DateTime.UtcNow) // 60 seconds for timeout
        {
            activeInvitation.Owner.SendPacket(new SCRejectedTeamPacket(activeInvitation.Target.Name, activeInvitation.IsParty));
            _activeInvitations.Remove(target.Id);
            return;
        }

        if (isArea)
        {
            // TODO
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
                // TODO - ERROR TEAM DO NOT EXISTS ANYMORE
                return;
            }
        }
        else
        {
            if (activeTeam.MembersCount() >= (activeTeam.IsParty ? 5 : 50)) // TODO - NEED TESTS
            {
                // ERROR TEAM IS FULL
                target.SendErrorMessage(ErrorMessageType.TeamFull);
                _activeInvitations.Remove(activeInvitation.Target.Id);
                return;
            }

            var (newTeamMember, party) = activeTeam.AddMember(target);
            if (newTeamMember != null)
            {
                target.SendPacket(new SCJoinedTeamPacket(activeTeam));
                target.InParty = true;
                target.SendPacket(new SCTeamPingPosPacket(activeTeam.PingPosition));
                activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newTeamMember, party), target.Id);
            }
        }

        _activeInvitations.Remove(activeInvitation.Target.Id);
    }

    public void MoveTeamMember(Character owner, uint teamId, uint targetId, uint target2Id, byte fromIndex, byte toIndex)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || activeTeam.OwnerId != owner.Id) return;

        var t1 = WorldManager.Instance.GetCharacterById(targetId);
        var t2 = WorldManager.Instance.GetCharacterById(target2Id);
        if (t1 != null)
            ChatManager.Instance.GetPartyChat(activeTeam, t1).LeaveChannel(t1);
        if (t2 != null)
            ChatManager.Instance.GetPartyChat(activeTeam, t2).LeaveChannel(t2);

        if (activeTeam.MoveMember(targetId, target2Id, fromIndex, toIndex))
        {
            activeTeam.BroadcastPacket(new SCTeamMemberMovedPacket(teamId, targetId, target2Id, fromIndex, toIndex));
            if (t1 != null)
                ChatManager.Instance.GetPartyChat(activeTeam, t1).JoinChannel(t1);
            if (t2 != null)
                ChatManager.Instance.GetPartyChat(activeTeam, t2).JoinChannel(t2);
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
        if (GetActiveTeamByUnit(activeInvitation.Owner.Id) != null || GetActiveTeamByUnit(activeInvitation.Target.Id) != null)
        {
            // TODO - ERROR MESSAGE ALREADY HAVE TEAM
            return;
        }

        var newTeam = new Team
        {
            Id = TeamIdManager.Instance.GetNextId(),
            OwnerId = activeInvitation.Owner.Id,
            IsParty = activeInvitation.IsParty,
            LootingRule = new LootingRule()
        };
        if (newTeam.AddMember(activeInvitation.Owner).Item1 == null || newTeam.AddMember(activeInvitation.Target).Item1 == null) return;

        _activeTeams.Add(newTeam.Id, newTeam);

        activeInvitation.Owner.SendPacket(new SCJoinedTeamPacket(newTeam));
        activeInvitation.Owner.InParty = true;
        activeInvitation.Target.SendPacket(new SCJoinedTeamPacket(newTeam));
        activeInvitation.Target.InParty = true;
        newTeam.BroadcastPacket(new SCTeamPingPosPacket(activeInvitation.Owner.LocalPingPosition));
        if (!newTeam.IsParty)
        {
            ChatManager.Instance.GetRaidChat(newTeam).JoinChannel(activeInvitation.Owner);
            ChatManager.Instance.GetRaidChat(newTeam).JoinChannel(activeInvitation.Target);
        }
        ChatManager.Instance.GetPartyChat(newTeam, activeInvitation.Owner).JoinChannel(activeInvitation.Owner);
        ChatManager.Instance.GetPartyChat(newTeam, activeInvitation.Target).JoinChannel(activeInvitation.Target);
    }

    public void CreateSoloTeam(Character character, bool asParty)
    {
        if (GetActiveTeamByUnit(character.Id) != null)
        {
            // TODO - ERROR MESSAGE ALREADY HAVE TEAM
            return;
        }

        var newTeam = new Team
        {
            Id = TeamIdManager.Instance.GetNextId(),
            OwnerId = character.Id,
            IsParty = true,
            LootingRule = new LootingRule()
        };
        if (newTeam.AddMember(character).Item1 == null) return;

        _activeTeams.Add(newTeam.Id, newTeam);

        character.SendPacket(new SCJoinedTeamPacket(newTeam));
        character.InParty = asParty;
        newTeam.BroadcastPacket(new SCTeamPingPosPacket(character.LocalPingPosition));

        if (!newTeam.IsParty)
            ChatManager.Instance.GetRaidChat(newTeam).JoinChannel(character);
        ChatManager.Instance.GetPartyChat(newTeam, character).JoinChannel(character);
    }

    public void AskRiskyTeam(Character unit, uint teamId, uint targetId, RiskyAction riskyAction)
    {
        // Get Team data
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null) return;
        var isAutoDisband = false;

        // Check if action is allowed; Kick only by raid leader ; Leave only by self
        if (riskyAction == RiskyAction.Kick && activeTeam.OwnerId != unit.Id ||
            riskyAction == RiskyAction.Leave && unit.Id != targetId) return;

        // Remove from ChatManager channels
        if (!activeTeam.IsParty)
            ChatManager.Instance.GetRaidChat(activeTeam).LeaveChannel(unit);
        ChatManager.Instance.GetPartyChat(activeTeam, unit).LeaveChannel(unit);

        if ((riskyAction == RiskyAction.Leave || riskyAction == RiskyAction.Kick) && activeTeam.RemoveMember(targetId))
        {
            // Check if person leaving is the leader, if so, find a new leader
            if (targetId == activeTeam.OwnerId)
            {
                var newOwner = activeTeam.GetNewOwner();
                if (newOwner != 0)
                {
                    activeTeam.OwnerId = newOwner;
                    activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(teamId, newOwner));
                }
                else
                {
                    // couldn't find a new leader, only party will auto-disband, raids will keep the one remaining person in it
                    if (activeTeam.IsParty)
                        isAutoDisband = true;
                }
            }

            // Send Leave info the team
            activeTeam.BroadcastPacket(new SCTeamMemberLeavedPacket(teamId, targetId, riskyAction == RiskyAction.Kick));
            // Find the target, and and send it's leave info
            var target = WorldManager.Instance.GetCharacterById(targetId);
            if (target != null)
            {
                target.InParty = false;
                target.SendPacket(new SCLeavedTeamPacket(teamId, riskyAction == RiskyAction.Kick, false));
            }
        }

        // Disband if only one member left in a Party (not raid)
        if ((activeTeam.IsParty) && (activeTeam.MembersCount() <= 1))
            isAutoDisband = true;

        // If everybody is offline, also disband regardless of raid or party status
        if (activeTeam.MembersOnlineCount() <= 0)
            isAutoDisband = true;

        // TODO - NEED TO FIND WHY NEED THIS
        activeTeam.BroadcastPacket(new SCTeamAckRiskyActionPacket(teamId, targetId, riskyAction, 0, 0));

        if (isAutoDisband || riskyAction == RiskyAction.Dismiss)
        {
            activeTeam.BroadcastPacket(new SCTeamDismissedPacket(teamId));
            foreach (var member in activeTeam.Members)
            {
                if (member?.Character != null)
                {
                    if (!activeTeam.IsParty)
                        ChatManager.Instance.GetRaidChat(activeTeam).LeaveChannel(member.Character);
                    ChatManager.Instance.GetPartyChat(activeTeam, member.Character).LeaveChannel(member.Character);

                    if (member.Character.IsOnline)
                    {
                        member.Character.SendPacket(new SCLeavedTeamPacket(teamId, false, true));
                        member.Character.InParty = false;
                    }
                }
            }

            _activeTeams.Remove(teamId);
        }
        // TODO: Add this to a timer or trigger instead of calling on a party/raid disband. But is good enough and functional for now
        ChatManager.Instance.CleanUpChannels();
    }

    public void MakeTeamOwner(Character unit, uint teamId, uint memberId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if ((activeTeam?.OwnerId != unit.Id) || activeTeam.OwnerId == memberId) return;

        if (activeTeam.IsMember(memberId)) activeTeam.OwnerId = memberId;
        activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(activeTeam.Id, activeTeam.OwnerId));
    }

    public void ConvertToRaid(Character owner, uint teamId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam?.OwnerId != owner.Id)
            return;

        activeTeam.IsParty = false;
        activeTeam.BroadcastPacket(new SCTeamBecameRaidTeamPacket(activeTeam.Id));
        foreach (var m in activeTeam.Members)
            if ((m != null) && (m.Character != null))
                ChatManager.Instance.GetRaidChat(activeTeam).JoinChannel(m.Character);
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
        if (activeTeam == null || activeTeam.OwnerId != unit.Id && !activeTeam.IsParty) return;

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

    public void ChangeLootingRule(Character owner, uint teamId, LootingRule newRules, byte flags)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam?.OwnerId != owner.Id) return;

        // TODO - FLAGS??
        activeTeam.LootingRule = newRules;
        activeTeam.BroadcastPacket(new SCTeamLootingRuleChangedPacket(teamId, newRules, flags));
    }

    public void SetPingPos(Character unit, TeamPingPos teamPingPos)
    {
        var activeTeam = GetActiveTeam(teamPingPos.TeamId);
        if ((activeTeam.OwnerId != unit.Id) && (activeTeam == null || !activeTeam.IsMarked(unit.Id))) return;

        activeTeam.PingPosition = teamPingPos;
        activeTeam.BroadcastPacket(new SCTeamPingPosPacket(activeTeam.PingPosition));
    }

    public void SetOffline(Character unit)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        var memberInfo = activeTeam?.ChangeStatus(unit);
        if (memberInfo == null) return;

        if (activeTeam.OwnerId == unit.Id)
        {
            var newOwner = activeTeam.GetNewOwner();
            if (newOwner != 0)
            {
                activeTeam.OwnerId = newOwner;
                activeTeam.BroadcastPacket(new SCTeamOwnerChangedPacket(activeTeam.Id, newOwner), unit.Id);
            }
        }

        activeTeam.BroadcastPacket(new SCTeamMemberDisconnectedPacket(activeTeam.Id, unit.Id, memberInfo));
    }

    public void MemberRemoveFromTeam(Character unit, Character source, RiskyAction leaveType)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        var memberInfo = activeTeam?.ChangeStatus(unit);
        if (memberInfo == null) return;
        var sourceTeam = GetActiveTeamByUnit(source.Id);
        var sourceInfo = activeTeam?.ChangeStatus(source);
        if (sourceInfo == null) return;
        if (activeTeam.Id != sourceTeam.Id)
        {
            // Can only remove member from the same team
            return;
        }
        if (!activeTeam.IsParty)
            ChatManager.Instance.GetRaidChat(activeTeam).LeaveChannel(unit);
        ChatManager.Instance.GetPartyChat(activeTeam, unit).LeaveChannel(unit);
        AskRiskyTeam(source, activeTeam.Id, unit.Id, leaveType);
    }

    public void UpdatePosition(uint id)
    {
        var activeTeam = GetActiveTeamByUnit(id);
        if (activeTeam == null) return;

        var index = activeTeam.GetIndex(id);
        if (index < 0) return;

        // TODO - MAYBE USE TASK FOR BETTER PERFORMANCE
        activeTeam.BroadcastPacket(new SCTeamRemoteMembersExPacket(activeTeam.Id, [activeTeam.Members[index]]), id);
    }

    public void UpdateAtLogin(Character unit)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        if (activeTeam == null) return;

        var newInfo = activeTeam.ChangeStatus(unit);
        unit.SendPacket(new SCJoinedTeamPacket(activeTeam));
        unit.InParty = true;
        activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newInfo, Team.GetParty(activeTeam.GetIndex(unit.Id))));
        //activeTeam.BroadcastPacket(new SCRefreshTeamMemberPacket(activeTeam.Id, unit.Id, unit.ObjId));
        if (!activeTeam.IsParty)
            ChatManager.Instance.GetRaidChat(activeTeam).JoinChannel(unit);
        ChatManager.Instance.GetPartyChat(activeTeam, unit).JoinChannel(unit);
    }

    public void Load()
    {
        _activeTeams = new Dictionary<uint, Team>();
        _activeInvitations = new Dictionary<uint, InvitationTemplate>();
    }
}

public class InvitationTemplate
{
    public uint TeamId { get; set; }
    public Character Owner { get; set; }
    public Character Target { get; set; }
    public DateTime Time { get; set; }
    public bool IsParty { get; set; }
}
