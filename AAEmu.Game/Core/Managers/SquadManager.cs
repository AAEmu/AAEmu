using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Squad;

using NLog;

namespace AAEmu.Game.Core.Managers;

public interface ISquadManager : IInitializable
{
    void RequestList(Character character, uint catalogId, int page);
    void Create(Character character, SquadFieldType field, SquadOpenType openType, bool partyInvitation,
        string explanation, byte limitLevel, int limitGearScore);
    void Join(Character character, uint squadId, uint fieldType, int invitationId, int joinKey);
    void Leave(Character character);
    void Disband(Character character);
    void SetReady(Character character, bool ready);
    void ApplyMatching(Character character, uint catalogId);
    void Invite(Character character, string targetName, byte worldId, uint catalogId);
    void RefuseInvite(Character character, int squadId, int invitationId, long worldCharKey, sbyte refuseType);
    void Expel(Character character, ulong targetWorldCharKey);
    void ChangeRole(Character character, sbyte role);
    void ChangeOpenType(Character character, SquadOpenType openType);
    void DelegateLeader(Character character, ulong targetWorldCharKey);
    void ClearWaitingFor(Character character);
    void NotifyGameEnter(Character character);
    void NotifyGameLeave(Character character);
    /// <summary>One-shot after login: clear a client SquadBase left over from a prior session.</summary>
    void SyncClientSquadAfterLogin(Character character);
}

public class SquadManager : Singleton<SquadManager>, ISquadManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, Squad> _squads = [];
    private readonly Dictionary<uint, uint> _characterSquad = [];
    private readonly Dictionary<uint, PendingInvite> _pendingInvites = [];
    private readonly Lock _lock = new();
    private uint _nextSquadId = 1;
    private uint _nextInvitationId = 1;

    private sealed class PendingInvite
    {
        public uint InvitationId { get; init; }
        public uint SquadId { get; init; }
        public uint InviterId { get; init; }
        public uint TargetId { get; init; }
    }

    public void Initialize()
    {
        Logger.Info("SquadManager initialized");
    }

    public void ClearWaitingFor(Character character)
    {
        if (character == null)
            return;
        var withdrew = IndunMatchmakingManager.Instance.TryWithdraw(character);
        InstantGameManager.Instance.WithdrawFromBattlefield(character);
        // TryWithdraw already acks when it removed queue/invite state; otherwise still clear the UI.
        if (!withdrew)
            character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
    }

    public void NotifyGameEnter(Character character)
    {
        if (character == null)
            return;

        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out var squad))
                return;
            squad.EnterCommitted = true;
            squad.HasEnteredInstance = true;
        }

        // Do not SCCancelInstantGame here. Enter already handed the client SCInstantGameReentry
        // (playing state 7); a queue-clear cancel while the client is still on waiting (state 3)
        // resets the instant-game manager and blocks AskLeaveInstantGame for the whole run.
        // gameStarted maps to SquadBase.isStarted; destination maps to gameWorld.
        character.SendPacket(new SCSquadSetGameInfoPacket(0, gameStarted: true));
    }

    /// <summary>
    /// Clears squad + instant-game client state after leaving an instance so Recruit works again.
    /// Quick Enter must <see cref="SCDisbandSquadPacket"/> — resetting flags alone leaves the
    /// client SquadBase alive, greys Recruit, and shows Leave Recruit/Search with no Register.
    /// </summary>
    public void NotifyGameLeave(Character character)
    {
        if (character == null)
            return;

        Squad squad;
        var disband = false;
        List<uint> memberIds = [];
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
            {
                // No server squad — only clear the queue ack. Do not Disband here; a blank
                // Disband on every miss made the client spam "team disbanded" and broke Enter.
                character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
                return;
            }

            disband = SquadRules.ShouldDisbandAfterInstanceLeave(squad.OpenType);
            if (disband)
            {
                DisbandLocked(squad);
            }
            else
            {
                SquadRules.ResetAfterInstanceLeave(squad);
                squad.HasEnteredInstance = false;
                memberIds = squad.Members.Select(m => m.CharacterId).ToList();
            }
        }

        if (disband)
        {
            character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
            Logger.Info("Squad disband on leave id={0} char={1}", squad.Id, character.Name);
            return;
        }

        character.SendPacket(new SCSquadSetGameInfoPacket(0, gameStarted: false));
        foreach (var memberId in memberIds)
        {
            if (squad.GetMember(memberId) == null)
                continue;
            BroadcastReady(squad, memberId, ready: false);
        }

        character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
        Logger.Info("Squad notify leave id={0} char={1}", squad.Id, character.Name);
    }

    public void RequestList(Character character, uint catalogId, int page)
    {
        if (character == null)
            return;

        // Only heal when the *server* still owns a stale entered Quick Enter squad.
        // Do not Disband/ClearQueue on every list refresh — that spams "team disbanded" and
        // resets the client instant-game manager so Enter / signup stay broken.
        MaybeRecoverStaleEnter(character);

        List<SquadListEntry> entries;
        int total;
        lock (_lock)
        {
            var listed = SquadRules.FilterListed(_squads.Values, catalogId);
            var (pageSquads, totalCount) = SquadRules.Page(listed, page);
            total = totalCount;
            entries = pageSquads.Select(s => ToListEntry(s, character.Id)).ToList();
        }

        character.SendPacket(new SCSelectSquadListPacket((uint)total, (uint)Math.Max(0, page), entries));
    }

    private void MaybeRecoverStaleEnter(Character character)
    {
        if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
            return;

        bool needsLeave;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out var squad))
                return;

            needsLeave = squad.EnterCommitted ||
                         (squad.HasEnteredInstance &&
                          SquadRules.ShouldDisbandAfterInstanceLeave(squad.OpenType));
        }

        if (needsLeave)
            NotifyGameLeave(character);
    }

    /// <summary>
    /// After character load: if the server has no squad for them, send one Disband so a leftover
    /// client SquadBase from a previous World session does not grey Recruit. Not used on list
    /// refresh — that path was spamming Disband/ClearQueue and breaking Enter.
    /// </summary>
    public void SyncClientSquadAfterLogin(Character character)
    {
        if (character == null)
            return;

        lock (_lock)
        {
            if (_characterSquad.ContainsKey(character.Id))
                return;
        }

        character.SendPacket(new SCDisbandSquadPacket());
        character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
    }

    public void Create(Character character, SquadFieldType field, SquadOpenType openType, bool partyInvitation,
        string explanation, byte limitLevel, int limitGearScore)
    {
        if (character == null)
            return;

        ClearWaitingFor(character);

        var catalogId = field.InstanceId;
        var zone = IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogId);
        if (zone == null)
        {
            Logger.Warn("Squad create: unknown catalog={0} char={1}", catalogId, character.Name);
            return;
        }

        if (character.Level < zone.LevelMin || character.Level > zone.LevelMax)
        {
            Logger.Warn("Squad create: level reject char={0} catalog={1}", character.Name, catalogId);
            return;
        }

        Squad squad;
        lock (_lock)
        {
            if (!SquadRules.CanCreate(_characterSquad.ContainsKey(character.Id)))
            {
                Logger.Warn("Squad create: already in squad char={0}", character.Name);
                return;
            }

            var id = _nextSquadId++;
            squad = new Squad
            {
                Id = id,
                CatalogId = catalogId,
                ZoneGroupId = zone.ZoneGroupId,
                Field = ResolveField(field, zone.ZoneGroupId),
                OpenType = openType,
                PartyInvitation = partyInvitation,
                Explanation = explanation ?? "",
                LimitLevel = limitLevel,
                LimitGearScore = limitGearScore,
                MaxMembers = zone.MaxPlayers == 0 ? 5 : zone.MaxPlayers,
                LeaderCharacterId = character.Id
            };

            var leader = MakeMember(character, isLeader: true);
            squad.Members.Add(leader);

            _squads[id] = squad;
            _characterSquad[character.Id] = id;
        }

        var entry = ToListEntry(squad, character.Id);
        character.SendPacket(new SCCreateSquadPacket(ignoreMinGameSize: false, entry));
        BroadcastJoin(squad, squad.Members[0]);

        Logger.Info("Squad create id={0} catalog={1} zoneGroup={2} maxMembers={3} openType={4} leader={5}",
            squad.Id, catalogId, squad.ZoneGroupId, squad.MaxMembers, openType, character.Name);
    }

    public void Join(Character character, uint squadId, uint fieldType, int invitationId, int joinKey)
    {
        if (character == null)
            return;

        ClearWaitingFor(character);

        Squad squad;
        SquadMember member;
        lock (_lock)
        {
            if (_characterSquad.ContainsKey(character.Id))
                return;
            if (!_squads.TryGetValue(squadId, out squad))
                return;

            if (invitationId != 0)
            {
                if (!_pendingInvites.TryGetValue((uint)invitationId, out var invite) ||
                    invite.SquadId != squadId || invite.TargetId != character.Id)
                    return;
                _pendingInvites.Remove((uint)invitationId);
            }
            else if (!SquadRules.CanJoinPublic(squad, character.Id, character.Level))
            {
                return;
            }

            member = MakeMember(character, isLeader: false);
            squad.Members.Add(member);
            _characterSquad[character.Id] = squad.Id;
        }

        BroadcastJoin(squad, member);
        Logger.Info("Squad join id={0} char={1}", squad.Id, character.Name);
    }

    public void Leave(Character character)
    {
        if (character == null)
            return;

        Squad squad;
        SquadMember leaving;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;

            leaving = squad.GetMember(character.Id);
            if (leaving == null)
                return;

            if (leaving.IsLeader)
            {
                DisbandLocked(squad);
                return;
            }

            squad.Members.Remove(leaving);
            _characterSquad.Remove(character.Id);
        }

        BroadcastLeave(squad, leaving, expelled: false);
    }

    public void Disband(Character character)
    {
        if (character == null)
            return;

        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out var squad))
                return;
            if (squad.LeaderCharacterId != character.Id)
                return;
            DisbandLocked(squad);
        }
    }

    public void SetReady(Character character, bool ready)
    {
        if (character == null)
            return;

        Squad squad;
        bool newReady;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;
            var member = squad.GetMember(character.Id);
            if (member == null)
                return;
            member.Ready = ready;
            newReady = member.Ready;
        }

        BroadcastReady(squad, character.Id, newReady);
    }

    public void ApplyMatching(Character character, uint catalogId)
    {
        if (character == null)
            return;

        Squad squad;
        var readied = new List<uint>();
        bool shouldBeginEnter;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;
            if (squad.LeaderCharacterId != character.Id)
                return;
            if (catalogId != 0 && catalogId != squad.CatalogId)
                return;

            var squadZone = IndunGameData.Instance.GetDungeonZoneByCatalogId(squad.CatalogId);
            if (squadZone?.DirectMatching == true && squad.OpenType == SquadOpenType.DirectMatching)
                foreach (var m in squad.Members)
                {
                    if (m.Ready)
                        continue;
                    m.Ready = true;
                    readied.Add(m.CharacterId);
                }

            shouldBeginEnter = SquadRules.ShouldBeginEnterOnApply(squad);
        }

        // The client draws each member's tick from this packet and redraws the team window when
        // it arrives, so a ready flag flipped only server-side leaves the window looking untouched.
        foreach (var readiedCharacterId in readied)
            BroadcastReady(squad, readiedCharacterId, ready: true);

        if (!shouldBeginEnter || !SquadRules.ShouldQueueMatchingOnApply(squad))
            return;

        // Registering never enters on its own. The instance is only created once matchmaking
        // offers it and the team takes the offer, so creating one here dropped players into a
        // dungeon straight off the Register button.
        var memberIds = squad.Members.Select(m => m.CharacterId).ToList();
        if (!IndunMatchmakingManager.Instance.TryApplySquad(squad.CatalogId, squad.Id, memberIds,
                SquadRules.WaitsForOtherPlayers(squad.OpenType)))
            return;

        squad.MatchingApplied = true;
        squad.Joining = true;
        Logger.Info("Squad apply matching id={0} catalog={1} members={2}",
            squad.Id, squad.CatalogId, memberIds.Count);
    }

    public void Invite(Character character, string targetName, byte worldId, uint catalogId)
    {
        if (character == null || string.IsNullOrWhiteSpace(targetName))
            return;

        var target = WorldManager.Instance.GetCharacter(targetName);
        if (target == null)
            return;

        uint invitationId;
        uint squadId;
        SquadFieldType field;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out squadId) ||
                !_squads.TryGetValue(squadId, out var squad))
                return;
            field = squad.Field;
            if (squad.LeaderCharacterId != character.Id)
                return;
            if (catalogId != 0 && catalogId != squad.CatalogId)
                return;
            if (_characterSquad.ContainsKey(target.Id) || squad.IsFull)
                return;

            invitationId = _nextInvitationId++;
            _pendingInvites[invitationId] = new PendingInvite
            {
                InvitationId = invitationId,
                SquadId = squadId,
                InviterId = character.Id,
                TargetId = target.Id
            };
        }

        target.SendPacket(new SCInviteSquadMemberPacket(
            squadId,
            character.Id,
            character.Name,
            invitationId,
            field));
    }

    public void RefuseInvite(Character character, int squadId, int invitationId, long worldCharKey,
        sbyte refuseType)
    {
        if (character == null)
            return;

        uint inviterId = 0;
        lock (_lock)
        {
            if (_pendingInvites.TryGetValue((uint)invitationId, out var invite))
            {
                inviterId = invite.InviterId;
                _pendingInvites.Remove((uint)invitationId);
            }
        }

        var inviter = WorldManager.Instance.GetCharacterById(inviterId);
        inviter?.SendPacket(new SCRefuseSquadInvitationPacket(character.Id, refuseType));
        BroadcastToSquadMembers(squadId > 0 ? (uint)squadId : 0,
            new SCRefuseSquadInvitationPacket(character.Id, refuseType));
    }

    public void Expel(Character character, ulong targetWorldCharKey)
    {
        if (character == null)
            return;

        var targetId = (uint)targetWorldCharKey;
        Squad squad;
        SquadMember expelled;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;
            if (squad.LeaderCharacterId != character.Id)
                return;
            expelled = squad.GetMember(targetId);
            if (expelled == null || expelled.IsLeader)
                return;
            squad.Members.Remove(expelled);
            _characterSquad.Remove(targetId);
        }

        BroadcastLeave(squad, expelled, expelled: true);
    }

    public void ChangeRole(Character character, sbyte role)
    {
        if (character == null)
            return;
        Squad squad;
        var changed = false;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;
            var member = squad.GetMember(character.Id);
            if (member != null)
            {
                member.Role = role;
                changed = true;
            }
        }
        // Retail parity: every client's UI must see the new role.
        if (changed)
            BroadcastToOnlineMembers(squad,
                new SCChangeSquadMemberRolePacket(WorldCharKeyOf(character.Id), (byte)role));
    }

    public void ChangeOpenType(Character character, SquadOpenType openType)
    {
        if (character == null)
            return;
        Squad squad;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out squad))
                return;
            if (squad.LeaderCharacterId != character.Id)
                return;
            squad.OpenType = openType;
        }
        // Retail parity: board visibility change is broadcast to all members.
        BroadcastToOnlineMembers(squad,
            new SCChangeSquadOpenTypePacket((byte)openType));
    }

    public void DelegateLeader(Character character, ulong targetWorldCharKey)
    {
        if (character == null)
            return;
        var targetId = (uint)targetWorldCharKey;
        lock (_lock)
        {
            if (!_characterSquad.TryGetValue(character.Id, out var squadId) ||
                !_squads.TryGetValue(squadId, out var squad))
                return;
            if (squad.LeaderCharacterId != character.Id)
                return;
            var next = squad.GetMember(targetId);
            if (next == null)
                return;
            var prev = squad.GetMember(character.Id);
            if (prev != null)
                prev.IsLeader = false;
            next.IsLeader = true;
            squad.LeaderCharacterId = targetId;
        }

        BroadcastToOnlineMembers(GetSquadByCharacter(character.Id),
            new SCDelegateSquadLeaderPacket((long)targetWorldCharKey));
    }

    private void DisbandLocked(Squad squad)
    {
        foreach (var m in squad.Members.ToList())
            _characterSquad.Remove(m.CharacterId);
        _squads.Remove(squad.Id);

        foreach (var m in squad.Members)
        {
            var ch = WorldManager.Instance.GetCharacterById(m.CharacterId);
            ch?.SendPacket(new SCDisbandSquadPacket());
        }
        Logger.Info("Squad disband id={0}", squad.Id);
    }

    private void BroadcastJoin(Squad squad, SquadMember member)
    {
        var packet = new SCJoinSquadMemberPacket(
            WorldCharKeyOf(member.CharacterId),
            member.Name,
            member.Level,
            member.Ability1,
            member.Ability2,
            member.Ability3,
            eloRating: 0);
        BroadcastToOnlineMembers(squad, packet);
    }

    private void BroadcastLeave(Squad squad, SquadMember member, bool expelled)
    {
        var packet = new SCLeaveSquadMemberPacket(WorldCharKeyOf(member.CharacterId), mask: 0, expelled);
        BroadcastToOnlineMembers(squad, packet);
        var left = WorldManager.Instance.GetCharacterById(member.CharacterId);
        left?.SendPacket(packet);
    }

    private void BroadcastReady(Squad squad, uint characterId, bool ready)
    {
        BroadcastToOnlineMembers(squad,
            new SCReadySquadPacket(WorldCharKeyOf(characterId), ready, errorMessage: 0));
    }

    private void BroadcastToOnlineMembers(Squad squad, GamePacket packet)
    {
        if (squad == null || packet == null)
            return;
        foreach (var m in squad.Members)
        {
            var ch = WorldManager.Instance.GetCharacterById(m.CharacterId);
            ch?.SendPacket(packet);
        }
    }

    private void BroadcastToSquadMembers(uint squadId, GamePacket packet)
    {
        if (squadId == 0)
            return;
        lock (_lock)
        {
            if (_squads.TryGetValue(squadId, out var squad))
                BroadcastToOnlineMembers(squad, packet);
        }
    }

    private Squad GetSquadByCharacter(uint characterId)
    {
        lock (_lock)
        {
            if (_characterSquad.TryGetValue(characterId, out var id) && _squads.TryGetValue(id, out var squad))
                return squad;
            return null;
        }
    }

    private uint GetSquadCatalog(uint squadId)
    {
        lock (_lock)
            return _squads.TryGetValue(squadId, out var s) ? s.CatalogId : 0u;
    }

    /// <summary>
    /// Echo the client's own instance selector, filling in the value from the resolved zone group
    /// when the client left it empty. Keeping the client's kind and instance id is what lets its
    /// title, member-cap and matchmaking lookups all resolve against the same tables we used.
    /// </summary>
    private static SquadFieldType ResolveField(SquadFieldType field, uint zoneGroupId) =>
        field.Value != 0 ? field : field with { Value = zoneGroupId };

    /// <summary>
    /// Member identity as the client keys it. Every packet that touches a member must use the
    /// same composite, or the client files the same player under two different keys.
    /// </summary>
    private static ulong WorldCharKeyOf(uint characterId) =>
        SquadWorldCharKey.Make(characterId, (byte)Math.Min(byte.MaxValue, AppConfiguration.Instance.Id));

    private static SquadMember MakeMember(Character character, bool isLeader) =>
        new()
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Ability1 = (byte)character.Ability1,
            Ability2 = (byte)character.Ability2,
            Ability3 = (byte)character.Ability3,
            IsLeader = isLeader,
            Ready = false
        };

    private static SquadListEntry ToListEntry(Squad squad, uint viewerCharacterId)
    {
        var leader = squad.Members.FirstOrDefault(m => m.IsLeader) ?? squad.Members.FirstOrDefault();
        var isMine = squad.GetMember(viewerCharacterId) != null;
        var worldId = (byte)Math.Min(byte.MaxValue, AppConfiguration.Instance.Id);
        // Must match the leader's key inside the member array below, or the client cannot
        // resolve which member leads and treats the squad as leaderless.
        var leaderKey = leader == null
            ? 0ul
            : SquadWorldCharKey.Make(leader.CharacterId, worldId);
        return new SquadListEntry
        {
            SquadId = squad.Id,
            OpenType = squad.OpenType,
            OwnerName = leader?.Name ?? "",
            OwnerLevel = leader?.Level ?? 0,
            WorldName = AppConfiguration.Instance.Id.ToString(),
            ExplanationText = squad.Explanation,
            LimitLevel = squad.LimitLevel,
            LimitGearScore = squad.LimitGearScore,
            Field = squad.Field,
            CatalogWireId = squad.CatalogId,
            LeaderWorldCharKey = leaderKey,
            PublicKey = squad.Id,
            MatchingKey = squad.MatchingApplied ? squad.Id : 0ul,
            IsJoining = squad.Joining,
            HeaderByte = worldId,
            WorldId = worldId,
            Members = squad.Members.ToList(),
            IsMySquad = isMine,
            ButtonEnable = !isMine && !squad.IsFull && SquadRules.AcceptsBoardApplications(squad.OpenType),
            ButtonType = SquadRules.ListButtonType(isMine)
        };
    }
}
