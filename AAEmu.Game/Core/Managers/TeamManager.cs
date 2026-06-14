using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class TeamManager(IWorldManager worldManager, IChatManager chatManager, ITeamIdManager teamIdManager) : Singleton<TeamManager>, ITeamManager
{
    /*
     * TODO:
     *
     * RE-DO LEAVE / KICK / DISMISS
     */

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // ── Remote-member sync (see Party/Raid remote sync design) ────────────
    /// <summary>Tick interval for the periodic remote-member sync.</summary>
    private const int TeamSyncIntervalMs = 500;
    /// <summary>Force a fresh broadcast even if state didn't change (paranoia against packet loss).</summary>
    private const int ForcedResyncIntervalMs = 5000;
    /// <summary>
    /// Grace window after the last online member of a team goes offline before the
    /// team is auto-disbanded. Lets brief crash-then-reconnect cycles preserve the
    /// team while keeping <c>_activeTeams</c> from accumulating zombie entries on
    /// long-running servers.
    /// </summary>
    private static readonly TimeSpan OfflineTeamDisbandGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Per-(recipient,source) snapshot of the last values broadcast to that recipient.
    /// Used to skip identical packets ("delta-update") so a stationary raid produces zero
    /// remote-sync traffic.
    /// </summary>
    private readonly Dictionary<(uint recipientId, uint sourceId), MemberSyncState> _lastSyncState = [];

    // ConcurrentDictionary so the 500ms UpdateAllTeams tick can enumerate the team
    // set while game-handler threads concurrently join/disband. Reads (TryGetValue,
    // Values) are lock-free; mutations (TryAdd, TryRemove) are internally synced.
    private readonly ConcurrentDictionary<uint, Team> _activeTeams = new(); // teamId, Team
    private readonly Dictionary<uint, InvitationTemplate> _activeInvitations = []; // targetId, InvitationTemplate

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
            // Skip hostile players (friendly and neutral can be invited, supports custom nations)
            var relation = owner.GetRelationStateTo(character);
            if (!character.InParty && relation is RelationState.Friendly or RelationState.Neutral)
                AskToJoin(owner, "", teamId, false, character);
        }
    }

    public void AskToJoin(Character owner, string targetName, uint teamId, bool isParty, Character targetObj = null)
    {
        var target = targetObj ?? worldManager.GetCharacter(targetName);
        if (target == null) return;
        // TODO - CONFIG INVITE DISABLED

        // Only hostile players cannot be invited (friendly and neutral are allowed, supports custom nations)
        if (owner.GetRelationStateTo(target) == RelationState.Hostile)
        {
            owner.SendErrorMessage(ErrorMessageType.TeamInviteRefused);
            return;
        }


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
                target.SendPacket(new SCTeamPingPosPacket(true, activeTeam.PingPosition, 0));
                activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, newTeamMember, party), target.Id);

                // Make sure team-mates outside render distance learn each other's ObjId
                RefreshTeamMemberObjIds(activeTeam, target);
            }
        }

        _activeInvitations.Remove(activeInvitation.Target.Id);
    }

    public void MoveTeamMember(Character owner, uint teamId, uint targetId, uint target2Id, byte fromIndex, byte toIndex)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || activeTeam.OwnerId != owner.Id) return;

        var t1 = worldManager.GetCharacterById(targetId);
        var t2 = worldManager.GetCharacterById(target2Id);
        if (t1 != null)
            chatManager.GetPartyChat(activeTeam, t1).LeaveChannel(t1);
        if (t2 != null)
            chatManager.GetPartyChat(activeTeam, t2).LeaveChannel(t2);

        if (activeTeam.MoveMember(targetId, target2Id, fromIndex, toIndex))
        {
            activeTeam.BroadcastPacket(new SCTeamMemberMovedPacket(teamId, targetId, target2Id, fromIndex, toIndex));
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
        if (GetActiveTeamByUnit(activeInvitation.Owner.Id) != null || GetActiveTeamByUnit(activeInvitation.Target.Id) != null)
        {
            // TODO - ERROR MESSAGE ALREADY HAVE TEAM
            return;
        }

        var newTeam = new Team
        {
            Id = teamIdManager.GetNextId(),
            OwnerId = activeInvitation.Owner.Id,
            IsParty = activeInvitation.IsParty
        };
        if (newTeam.AddMember(activeInvitation.Owner).Item1 == null || newTeam.AddMember(activeInvitation.Target).Item1 == null) return;

        _activeTeams.TryAdd(newTeam.Id, newTeam);

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

        // Owner and target may be out of each other's render distance — make sure
        // both clients know each other's ObjId.
        RefreshTeamMemberObjIds(newTeam, activeInvitation.Target);
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
            Id = teamIdManager.GetNextId(),
            OwnerId = character.Id,
            IsParty = true
        };
        if (newTeam.AddMember(character).Item1 == null) return;

        _activeTeams.TryAdd(newTeam.Id, newTeam);

        character.SendPacket(new SCJoinedTeamPacket(newTeam));
        character.InParty = asParty;
        newTeam.BroadcastPacket(new SCTeamPingPosPacket(true, character.LocalPingPosition, 0));

        if (!newTeam.IsParty)
            chatManager.GetRaidChat(newTeam).JoinChannel(character);
        chatManager.GetPartyChat(newTeam, character).JoinChannel(character);
        // Trigger events
        character.Events?.OnTeamJoin(character, new OnTeamJoinArgs { Team = newTeam, Player = character });
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
            chatManager.GetRaidChat(activeTeam).LeaveChannel(unit);
        chatManager.GetPartyChat(activeTeam, unit).LeaveChannel(unit);

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
            // Find the target, and send its leave info
            var target = worldManager.GetCharacterById(targetId);
            if (target != null)
            {
                target.InParty = false;
                target.SendPacket(new SCLeavedTeamPacket(teamId, riskyAction == RiskyAction.Kick, false));
                target.Events?.OnTeamLeave(target, new OnTeamLeaveArgs { Id = activeTeam.Id, Team = activeTeam, Player = target });
            }

            // Drop cached remote-sync state for the leaver
            ClearSyncState(targetId);
        }

        // Disband if only one member left in Party (not raid)
        if (activeTeam.IsParty && activeTeam.MembersCount() <= 1)
            isAutoDisband = true;

        // If everybody is offline, also disband regardless of raid or party status
        if (activeTeam.MembersOnlineCount() <= 0)
            isAutoDisband = true;

        // TODO - Need to find why we need this
        activeTeam.BroadcastPacket(new SCTeamAckRiskyActionPacket(teamId, targetId, riskyAction, 0, 0));

        if (isAutoDisband || riskyAction == RiskyAction.Dismiss)
        {
            activeTeam.BroadcastPacket(new SCTeamDismissedPacket(teamId));
            foreach (var member in activeTeam.Members)
            {
                if (member?.Character != null)
                {
                    if (!activeTeam.IsParty)
                        chatManager.GetRaidChat(activeTeam).LeaveChannel(member.Character);
                    chatManager.GetPartyChat(activeTeam, member.Character).LeaveChannel(member.Character);

                    if (member.Character.IsOnline)
                    {
                        member.Character.SendPacket(new SCLeavedTeamPacket(teamId, false, true));
                        member.Character.InParty = false;
                        // trigger event
                        member.Character.Events?.OnTeamLeave(member.Character, new OnTeamLeaveArgs { Id = activeTeam.Id, Team = activeTeam, Player = member.Character });
                    }

                    // Drop cached remote-sync state per disbanded member
                    ClearSyncState(member.Character.Id);
                }
            }

            _activeTeams.TryRemove(teamId, out _);
        }
        // TODO: Add this to a timer or trigger instead of calling on a party/raid disband. But is good enough and functional for now
        chatManager.CleanUpChannels();
    }

    public void MakeTeamOwner(Character unit, uint teamId, uint memberId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam?.OwnerId != unit.Id || activeTeam.OwnerId == memberId) return;

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
        {
            if (m?.Character != null)
                chatManager.GetRaidChat(activeTeam).JoinChannel(m.Character);
        }

        // Once the party becomes a raid, members spread out across the world, so the
        // client must know everyone's ObjId for the raid UI even when out of render.
        // Walk the upper-triangle pairs so each (a,b) pair is refreshed exactly once
        // (avoids the O(N²) duplicate-send problem of calling RefreshTeamMemberObjIds
        // per member, which would send both directions for every pair twice).
        RefreshAllTeamMemberObjIds(activeTeam);
        // TODO: Handle raids in dungeons
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

    public void ChangeLootingRule(Character owner, uint teamId, byte flags, LootingRuleMethod lootingRuleMethod, byte minimumGrade, uint lootMaster, bool rollForBindOnPickup)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam?.OwnerId != owner.Id) return;

        // Flags:
        // 1: Method
        // 2: Grade
        // 4: LootMaster
        // 8: BindOnPickup
        if ((flags & 0x08) != 0)
        {
            activeTeam.LootingRule.RollForBindOnPickup = rollForBindOnPickup;
        }
        if ((flags & 0x04) != 0)
        {
            activeTeam.LootingRule.LootMaster = lootMaster;
        }
        if ((flags & 0x02) != 0)
        {
            activeTeam.LootingRule.MinimumGrade = minimumGrade;
        }
        if ((flags & 0x01) != 0)
        {
            activeTeam.LootingRule.LootMethod = lootingRuleMethod;
            if (activeTeam.LootingRule.LootMethod != LootingRuleMethod.LootMaster)
                activeTeam.LootingRule.LootMaster = 0;
        }

        activeTeam.BroadcastPacket(new SCTeamLootingRuleChangedPacket(teamId, activeTeam.LootingRule, flags));
    }

    public void SetPingPos(Character unit, uint teamId, bool hasPing, WorldSpawnPosition position, uint insId)
    {
        var activeTeam = GetActiveTeam(teamId);
        if (activeTeam == null || (activeTeam.OwnerId != unit.Id && !activeTeam.IsMarked(unit.Id)))
            return;

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

        // Tell every OUT-OF-RENDER team-mate's client-side world cache that this
        // ObjId is gone so raid-frame click-routing stops firing
        // CSMoveUnitPacket(target=oldObjId) after the character was Deleted from
        // the world. Mates in the same region already receive SCUnitsRemovedPacket
        // via Region.RemoveObject when activeChar.Delete() runs later in the
        // disconnect path — sending again would just be a duplicate.
        foreach (var member in activeTeam.Members)
        {
            if (member?.Character == null || !member.Character.IsOnline) continue;
            if (member.Character.Id == unit.Id) continue;
            if (AreInRenderDistance(member.Character, unit)) continue;
            member.Character.SendPacket(new SCUnitsRemovedPacket([unit.ObjId]));
        }

        // Drop any cached remote-sync state about this character so it doesn't leak.
        ClearSyncState(unit.Id);

        // Arm the zombie-team disband timer once the last online member goes offline.
        // The 500ms UpdateAllTeams tick checks the grace window and removes the team
        // from _activeTeams if it stays fully offline — otherwise the entry would
        // live forever because LeaveWorldTask no longer calls MemberRemoveFromTeam
        // (which used to be the only path that hit AskRiskyTeam's auto-disband).
        if (activeTeam.MembersOnlineCount() <= 0)
            activeTeam.AllOfflineSince = DateTime.UtcNow;
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
            chatManager.GetRaidChat(activeTeam).LeaveChannel(unit);
        chatManager.GetPartyChat(activeTeam, unit).LeaveChannel(unit);
        AskRiskyTeam(source, activeTeam.Id, unit.Id, leaveType);
    }

    public void UpdatePosition(uint id)
    {
        var activeTeam = GetActiveTeamByUnit(id);
        if (activeTeam == null) return;

        var index = activeTeam.GetIndex(id);
        if (index < 0) return;

        // TODO - MAYBE USE TASK FOR BETTER PERFORMANCE
        activeTeam.BroadcastPacket(new SCTeamRemoteMembersExPacket([activeTeam.Members[index]]), id);
    }

    public void UpdateAtLogin(Character unit)
    {
        var activeTeam = GetActiveTeamByUnit(unit.Id);
        if (activeTeam == null) return;

        // Lock against a concurrent DisbandOfflineTeam: it holds team.SyncLock
        // across its re-verify + member-wipe + TryRemove. Once we hold the lock,
        // re-check that the team is still in _activeTeams — if a disband ran to
        // completion between GetActiveTeamByUnit and the lock acquisition, we'd
        // otherwise reconnect against a dead team and end up with
        // InParty = true on a Character that has no live team backing it.
        lock (activeTeam.SyncLock)
        {
            if (!_activeTeams.ContainsKey(activeTeam.Id))
                return;

            // Cancel the zombie-team disband timer — at least one member is back online.
            activeTeam.AllOfflineSince = DateTime.MinValue;

            activeTeam.ChangeStatus(unit);
        }

        unit.SendPacket(new SCJoinedTeamPacket(activeTeam));
        unit.InParty = true;

        // 1) Reconnecting player gets a single bulk snapshot of every OTHER team-
        //    mate's current HP/MP/position/ObjId so the raid UI is fully populated.
        //    Self is excluded — the client already knows its own state and a
        //    self-entry in SCTeamRemoteMembersExPacket would just be wasted bytes.
        var allMembers = activeTeam.Members
            .Where(m => m?.Character != null && m.Character.Id != unit.Id)
            .ToArray();
        if (allMembers.Length > 0)
            unit.SendPacket(new SCTeamRemoteMembersExPacket(allMembers));

        // 2) Existing team-mates get the explicit "back online" signal
        //    (SCTeamMemberJoinedPacket — counterpart to SCTeamMemberDisconnectedPacket
        //    sent in SetOffline) plus *only* the reconnecting player's snapshot,
        //    not all 50 frames. Without the JoinedPacket the client party-frame
        //    stays stuck on the offline indicator even though HP/MP keep updating.
        var idx = activeTeam.GetIndex(unit.Id);
        if (idx >= 0)
        {
            var reconnectingMember = activeTeam.Members[idx];
            var partyIndex = Team.GetParty(idx);
            activeTeam.BroadcastPacket(new SCTeamMemberJoinedPacket(activeTeam.Id, reconnectingMember, partyIndex), unit.Id);
            activeTeam.BroadcastPacket(new SCTeamRemoteMembersExPacket([reconnectingMember]), unit.Id);
        }

        // Reconnect: force the ObjId refresh on EVERY online team-mate, not just
        // the out-of-render ones. SetOffline's SCTeamMemberDisconnectedPacket
        // carries WritePerson(IsOnline ? ObjId : 0) — i.e. ObjId = 0 after the
        // P1 setter-order fix — so each team-mate's raid-frame slot for this
        // player is currently keyed to 0. The reconnecting player gets the
        // SAME ObjId back via Character.UsedCharacterObjIds, but the in-range
        // world Spawn flow does not push the raid-frame re-mapping on its own;
        // without an explicit SCRefreshTeamMemberPacket the next click-to-
        // follow fires CSMoveUnitPacket(target=0) and the server logs
        // "Invalid target".
        RefreshTeamMemberObjIds(activeTeam, unit, force: true);

        if (!activeTeam.IsParty)
            chatManager.GetRaidChat(activeTeam).JoinChannel(unit);
        chatManager.GetPartyChat(activeTeam, unit).JoinChannel(unit);
    }

    public void Load()
    {
        // Synchronous tick (useAsync: false). UpdateAllTeams iterates _activeTeams +
        // _lastSyncState, which are mutated by game-handler threads on player join/
        // leave/disband. Running synchronously on the tick thread keeps these
        // reads simple and consistent with the rest of the manager.
        TickManager.Instance.OnTick.Subscribe(UpdateAllTeams, TimeSpan.FromMilliseconds(TeamSyncIntervalMs), false);
        Logger.Info("TeamManager loaded with {0}ms remote-sync interval", TeamSyncIntervalMs);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Remote-member sync
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Reusable scratch list to avoid per-tick allocations inside <see cref="UpdateAllTeams"/>.</summary>
    private readonly List<TeamMember> _onlineMembersScratch = [];

    /// <summary>Reusable per-recipient buffer of source members whose snapshots changed this tick.</summary>
    private readonly Dictionary<TeamMember, List<TeamMember>> _perRecipientScratch = [];

    /// <summary>
    /// 500ms tick: walk every active team and broadcast HP/MP/position to members
    /// that are NOT in render distance of each other. Members in render distance
    /// already receive that data via the normal Broadcast* pipeline, so they are
    /// skipped. Delta-cache (<see cref="_lastSyncState"/>) skips identical
    /// packets so a stationary raid produces no traffic.
    /// </summary>
    /// <remarks>
    /// Pair-walk is upper-triangle (i &lt; j) — <see cref="AreInRenderDistance"/> is
    /// symmetric so each pair only needs one neighbour check, not two. Sends are
    /// coalesced per recipient into one <see cref="SCTeamRemoteMembersExPacket"/>:
    /// a spread-out 50-man raid produces ~50 packets/tick instead of ~2,450.
    /// </remarks>
    private void UpdateAllTeams(TimeSpan delta)
    {
        var now = DateTime.UtcNow;

        if (_activeTeams.IsEmpty) return;

        // ConcurrentDictionary.Values returns a snapshot, so the enumeration
        // cannot be invalidated by a parallel Add/Remove during the tick.
        foreach (var team in _activeTeams.Values)
        {
            // Zombie-team cleanup: every member offline for longer than the grace
            // window → disband and free the slot. Done before the per-member walk so
            // the tick doesn't pay the snapshot cost on a doomed team.
            if (team.AllOfflineSince != DateTime.MinValue &&
                now - team.AllOfflineSince >= OfflineTeamDisbandGrace)
            {
                DisbandOfflineTeam(team);
                continue;
            }

            _onlineMembersScratch.Clear();
            foreach (var m in team.Members)
            {
                if (m?.Character?.IsOnline == true)
                    _onlineMembersScratch.Add(m);
            }
            if (_onlineMembersScratch.Count <= 1)
                continue;

            _perRecipientScratch.Clear();

            for (var i = 0; i < _onlineMembersScratch.Count; i++)
            {
                var a = _onlineMembersScratch[i];
                for (var j = i + 1; j < _onlineMembersScratch.Count; j++)
                {
                    var b = _onlineMembersScratch[j];

                    // Same/neighbouring region — normal broadcast already covers them
                    if (AreInRenderDistance(a.Character, b.Character))
                        continue;

                    // Each direction has its own (recipient, source) cache entry; check
                    // both, but the expensive region walk above only runs once per pair.
                    if (TryClaimSnapshot(a, b, now)) AddSnapshotTarget(a, b);
                    if (TryClaimSnapshot(b, a, now)) AddSnapshotTarget(b, a);
                }
            }

            foreach (var (recipient, sources) in _perRecipientScratch)
            {
                recipient.Character.SendPacket(new SCTeamRemoteMembersExPacket(sources.ToArray()));
            }
        }
    }

    /// <summary>
    /// Per-(recipient, source) delta-cache check. Returns true when the source's
    /// current snapshot differs from the cached one (or the forced-resync window
    /// has elapsed), and atomically updates the cache so subsequent ticks see the
    /// new state.
    /// </summary>
    private bool TryClaimSnapshot(TeamMember recipient, TeamMember source, DateTime now)
    {
        var key = (recipient.Character.Id, source.Character.Id);
        var pos = source.Character.Transform.World.Position;
        var nextState = new MemberSyncState(
            source.Character.Hp,
            source.Character.MaxHp,
            source.Character.Mp,
            source.Character.MaxMp,
            pos.X, pos.Y, pos.Z,
            source.Character.ObjId,
            now);

        lock (_lastSyncState)
        {
            if (_lastSyncState.TryGetValue(key, out var prev) &&
                prev.HasSameSnapshotAs(nextState) &&
                (now - prev.LastSyncTime).TotalMilliseconds < ForcedResyncIntervalMs)
            {
                return false;
            }
            _lastSyncState[key] = nextState;
            return true;
        }
    }

    private void AddSnapshotTarget(TeamMember recipient, TeamMember source)
    {
        if (!_perRecipientScratch.TryGetValue(recipient, out var list))
            _perRecipientScratch[recipient] = list = [];
        list.Add(source);
    }

    /// <summary>
    /// Auto-disband a team that has been fully offline past the grace window.
    /// Mirrors the per-member cleanup that <see cref="AskRiskyTeam"/> does on its
    /// auto-disband branch — most importantly resetting <c>Character.InParty</c>
    /// so a Character object that survives the disband (e.g. cached across a fast
    /// reconnect) cannot leave <c>InParty = true</c> with no backing team, which
    /// would break null-unchecked call sites like
    /// <c>Tagging.AddTagger → TeamManager.GetTeamByObjId</c>.
    /// <c>SCTeamDismissedPacket</c> is skipped because nobody is online to receive
    /// it; on next reconnect <see cref="UpdateAtLogin"/> sees no team and the
    /// client lands without raid UI, which is the desired end state.
    /// </summary>
    private void DisbandOfflineTeam(Team team)
    {
        // Hold the team's SyncLock across the re-verify check AND the member-wipe
        // loop so a concurrent UpdateAtLogin cannot land in the gap between them
        // (it would otherwise mark a member online after our check, then receive
        // InParty = false from the wipe). UpdateAtLogin takes the same lock and
        // additionally re-checks _activeTeams membership, so it gracefully bails
        // when this disband has already pulled the team out.
        lock (team.SyncLock)
        {
            // Re-verify under the latest member state before wiping anything. The
            // tick is decoupled from SetOffline / UpdateAtLogin by a multi-minute
            // grace window, and the SetOffline check/write pair
            // (MembersOnlineCount() ≤ 0, then assign AllOfflineSince) is not
            // atomic — a concurrent UpdateAtLogin landing in between can leave
            // AllOfflineSince armed with a stale "now" even though a member is
            // currently online.
            if (team.MembersOnlineCount() > 0)
            {
                team.AllOfflineSince = DateTime.MinValue;
                return;
            }

            foreach (var member in team.Members)
            {
                if (member?.Character == null) continue;
                member.Character.InParty = false;
                ClearSyncState(member.Character.Id);
            }

            _activeTeams.TryRemove(team.Id, out _);
        }

        chatManager.CleanUpChannels();
        Logger.Info("Disbanded zombie team {0} after {1} of all-offline", team.Id, OfflineTeamDisbandGrace);
    }

    /// <summary>
    /// Drop every cached sync-state entry mentioning <paramref name="characterId"/>
    /// as either recipient or source. Called whenever a character leaves the team
    /// (offline / leave / kick / disband) so the cache cannot leak.
    /// </summary>
    internal void ClearSyncState(uint characterId)
    {
        lock (_lastSyncState)
        {
            if (_lastSyncState.Count == 0) return;

            var toRemove = new List<(uint, uint)>();
            foreach (var key in _lastSyncState.Keys)
            {
                if (key.recipientId == characterId || key.sourceId == characterId)
                    toRemove.Add(key);
            }
            foreach (var key in toRemove)
                _lastSyncState.Remove(key);
        }
    }

    /// <summary>
    /// Send <see cref="SCRefreshTeamMemberPacket"/> in both directions so every
    /// existing team-mate learns the new member's ObjId. By default skips pairs
    /// already in render distance (those learn the ObjId via the world spawn
    /// flow); pass <paramref name="force"/> = <c>true</c> on reconnect, where the
    /// in-range world flow doesn't reliably overwrite cached raid-frame ObjIds.
    /// </summary>
    private static void RefreshTeamMemberObjIds(Team team, Character newMember, bool force = false)
    {
        if (team == null || newMember == null) return;

        foreach (var m in team.Members)
        {
            if (m?.Character == null || m.Character.Id == newMember.Id || !m.Character.IsOnline)
                continue;

            if (!force && AreInRenderDistance(m.Character, newMember))
                continue;

            m.Character.SendPacket(new SCRefreshTeamMemberPacket(team.Id, newMember.Id, newMember.ObjId));
            newMember.SendPacket(new SCRefreshTeamMemberPacket(team.Id, m.Character.Id, m.Character.ObjId));
        }
    }

    /// <summary>
    /// Refresh ObjIds for every out-of-render-distance pair in the team exactly once.
    /// Used by <see cref="ConvertToRaid"/> where the whole team needs to learn each
    /// other's ObjIds; iterating with <see cref="RefreshTeamMemberObjIds"/> per member
    /// would double every pair (O(N²) duplicates — ~4,900 redundant packets for a
    /// 50-member raid). Upper-triangle traversal (i &lt; j) avoids that.
    /// </summary>
    private static void RefreshAllTeamMemberObjIds(Team team)
    {
        if (team == null) return;

        var online = new List<Character>();
        foreach (var m in team.Members)
        {
            if (m?.Character != null && m.Character.IsOnline)
                online.Add(m.Character);
        }

        for (var i = 0; i < online.Count; i++)
        {
            for (var j = i + 1; j < online.Count; j++)
            {
                if (AreInRenderDistance(online[i], online[j]))
                    continue;
                online[i].SendPacket(new SCRefreshTeamMemberPacket(team.Id, online[j].Id, online[j].ObjId));
                online[j].SendPacket(new SCRefreshTeamMemberPacket(team.Id, online[i].Id, online[i].ObjId));
            }
        }
    }

    /// <summary>
    /// Symmetric region-based render-distance check: returns true if <paramref name="a"/>
    /// and <paramref name="b"/> share a Region or are listed as neighbours from either
    /// side. The bidirectional test guards against edge-of-world Regions whose
    /// <c>GetNeighbors()</c> set may not be symmetric.
    /// </summary>
    private static bool AreInRenderDistance(Character a, Character b)
    {
        if (a?.Region == null || b?.Region == null) return false;
        if (a.Region == b.Region) return true;

        foreach (var neighbour in a.Region.GetNeighbors())
        {
            if (neighbour == b.Region) return true;
        }
        foreach (var neighbour in b.Region.GetNeighbors())
        {
            if (neighbour == a.Region) return true;
        }
        return false;
    }
}

/// <summary>
/// Snapshot of a team-member's last broadcast state, keyed by (recipientId, sourceId).
/// Used by the delta-cache to suppress identical remote-member sync packets.
/// </summary>
public sealed record MemberSyncState(
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    float X,
    float Y,
    float Z,
    uint ObjId,
    DateTime LastSyncTime)
{
    /// <summary>
    /// Position-equality tolerance for the delta-cache comparison. Anything below
    /// this is sub-centimetre physics jitter that wouldn't be visible on the raid
    /// UI; using strict <c>==</c> here would defeat the delta cache for stationary
    /// raid mates whose solver still produces tiny floating-point wobble each tick.
    /// </summary>
    private const float PositionEpsilon = 0.01f;

    /// <summary>True when every field except <see cref="LastSyncTime"/> matches
    /// (position fields compared with <see cref="PositionEpsilon"/> tolerance).</summary>
    public bool HasSameSnapshotAs(MemberSyncState other) =>
        Hp == other.Hp && MaxHp == other.MaxHp &&
        Mp == other.Mp && MaxMp == other.MaxMp &&
        MathF.Abs(X - other.X) < PositionEpsilon &&
        MathF.Abs(Y - other.Y) < PositionEpsilon &&
        MathF.Abs(Z - other.Z) < PositionEpsilon &&
        ObjId == other.ObjId;
}

public class InvitationTemplate
{
    public uint TeamId { get; set; }
    public Character Owner { get; set; }
    public Character Target { get; set; }
    public DateTime Time { get; set; }
    public bool IsParty { get; set; }
}
