using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TeamManager : Singleton<TeamManager>
    {
        /*
         * TODO:
         *
         * RE-DO LEAVE / KICK / DISMISS
         */
        private static Logger _log = LogManager.GetCurrentClassLogger();

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

        private Team GetActiveTeam(uint teamId)
        {
            if (teamId == 0) return null;
            return _activeTeams.ContainsKey(teamId) ? _activeTeams[teamId] : null;
        }

        private InvitationTemplate GetActiveInvitation(uint targetId)
        {
            return _activeInvitations.ContainsKey(targetId) ? _activeInvitations[targetId] : null;
        }

        public void InviteAreaToTeam(Character owner, uint teamId, bool isParty)
        {
            var characters = WorldManager.Instance.GetAround<Character>(owner, 100.0f); // TODO - CHECK IF 100m
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
            if (GetActiveInvitation(target.Id) != null)
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

            _activeInvitations.Add(target.Id, new InvitationTemplate
            {
                Owner = owner,
                Target = target,
                IsParty = activeTeam?.IsParty ?? isParty,
                Time = DateTime.Now,
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

            if (isReject || activeInvitation.Time.AddSeconds(60) < DateTime.Now) // 60 seconds for timeout
            {
                activeInvitation.Owner.SendPacket(new SCRejectedTeamPacket(activeInvitation.Target.Name, activeInvitation.IsParty));
                _activeInvitations.Remove(target.Id);
                return;
            }

            if (isArea)
            {
                // TODO
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
                    // TODO - ERROR TEAM DO NOT EXISTS ANYMORE
                    return;
                }
            }
            else
            {
                if (activeTeam.MembersCount() >= (activeTeam.IsParty ? 5 : 50)) // TODO - NEED TESTS
                {
                    // TODO - ERROR TEAM IS FULL
                    return;
                }

                var (newTeamMember, party) = activeTeam.AddMember(target);
                if (newTeamMember != null)
                {
                    target.SendPacket(new SCJoinedTeamPacket(activeTeam));
                    target.InParty = true;
                    target.SendPacket(new SCTeamPingPosPacket(true, activeTeam.PingPosition, 0));
                    activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newTeamMember, party), target.Id);
                }
            }

            _activeInvitations.Remove(activeInvitation.Target.Id);
        }

        public void MoveTeamMember(Character owner, uint teamId, uint targetId, uint target2Id, byte fromIndex, byte toIndex)
        {
            var activeTeam = GetActiveTeam(teamId);
            if (activeTeam == null || activeTeam.OwnerId != owner.Id) return;

            if (activeTeam.MoveMember(targetId, target2Id, fromIndex, toIndex))
            {
                activeTeam.BroadcastPacket(new SCTeamMemberMovedPacket(teamId, targetId, target2Id, fromIndex, toIndex));
            }
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

            // TODO - CHAT JOIN CHANNEL???
            activeInvitation.Owner.SendPacket(new SCJoinedTeamPacket(newTeam));
            activeInvitation.Owner.InParty = true;
            activeInvitation.Target.SendPacket(new SCJoinedTeamPacket(newTeam));
            activeInvitation.Target.InParty = true;
            newTeam.BroadcastPacket(new SCTeamPingPosPacket(true, new Point(0, 0, 0), 0)); // TODO - GET "REAL" POSITION FROM DUMP
        }

        public void AskRiskyTeam(Character unit, uint teamId, uint targetId, RiskyAction riskyAction)
        {
            var activeTeam = GetActiveTeam(teamId);
            if (activeTeam == null) return;
            var isDisband = false;

            if (riskyAction == RiskyAction.Kick && activeTeam.OwnerId != unit.Id ||
                riskyAction == RiskyAction.Leave && unit.Id != targetId) return;
            if ((riskyAction == RiskyAction.Leave || riskyAction == RiskyAction.Kick) &&
                activeTeam.RemoveMember(targetId))
            {
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
                        isDisband = true;
                    }
                }

                activeTeam.BroadcastPacket(new SCTeamMemberLeavedPacket(teamId, targetId,
                    riskyAction == RiskyAction.Kick));
                var target = WorldManager.Instance.GetCharacterById(targetId);
                if (target != null)
                {
                    target.InParty = false;
                    target.SendPacket(new SCLeavedTeamPacket(teamId, riskyAction == RiskyAction.Kick, false));
                }
            }

            // TODO - NEED TO FIND WHY NEED THIS
            activeTeam.BroadcastPacket(new SCTeamAckRiskyActionPacket(teamId, targetId, riskyAction, 0, 0));

            if (isDisband || riskyAction == RiskyAction.Dismiss || activeTeam.MembersCount() <= 1)
            {
                activeTeam.BroadcastPacket(new SCTeamDismissedPacket(teamId));
                foreach (var member in activeTeam.Members)
                {
                    if (member?.Character != null && member.Character.IsOnline)
                    {
                        member.Character.SendPacket(new SCLeavedTeamPacket(teamId, false, true));
                        member.Character.InParty = false;
                    }
                }

                _activeTeams.Remove(teamId);
            }
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

        public void SetPingPos(Character unit, uint teamId, bool hasPing, Point position, uint insId)
        {
            var activeTeam = GetActiveTeam(teamId);
            if (activeTeam == null || !activeTeam.IsMarked(unit.Id)) return;

            activeTeam.PingPosition = position;
            activeTeam.BroadcastPacket(new SCTeamPingPosPacket(hasPing, position, insId));
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

        public void UpdatePosition(uint id)
        {
            var activeTeam = GetActiveTeamByUnit(id);
            if (activeTeam == null) return;

            var index = activeTeam.GetIndex(id);
            if (index < 0) return;

            // TODO - MAYBE USE TASK FOR BETTER PERFORMANCE
            activeTeam.BroadcastPacket(new SCTeamRemoteMembersExPacket(new[] {activeTeam.Members[index]}), id);
        }

        public void UpdateAtLogin(Character unit)
        {
            var activeTeam = GetActiveTeamByUnit(unit.Id);
            if (activeTeam == null) return;

            var newInfo = activeTeam.ChangeStatus(unit);
            unit.SendPacket(new SCJoinedTeamPacket(activeTeam));
            unit.InParty = true;
            activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newInfo, activeTeam.GetParty(activeTeam.GetIndex(unit.Id))));
            //activeTeam.BroadcastPacket(new SCRefreshTeamMemberPacket(activeTeam.Id, unit.Id, unit.ObjId));
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
}
