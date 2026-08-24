using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Indun.Matching;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

public interface IIndunMatchmakingManager : IInitializable
{
    bool TryApply(uint catalogId, Character character);
    bool TryWithdraw(Character character);
    bool TryInvitationAnswer(Character character, int invitationTime, bool acceptance);
    bool TryEnter(Character character);
    bool TryLeaveIndunMatch(Character character);
    bool IsInQueueOrSession(uint characterId);
    /// <summary>Queue a recruit squad for H-window matching (same catalog, shared warmup).</summary>
    bool TryApplySquad(uint catalogId, uint squadId, IReadOnlyList<uint> memberCharacterIds,
        bool waitsForOtherPlayers);
}

/// <summary>
/// H-window / instances catalog matchmaking for IndunZone targets (PERFECT invite or DIRECT enter).
/// Separate from PvP <see cref="InstantGameManager"/>.
/// </summary>
public class IndunMatchmakingManager : Singleton<IndunMatchmakingManager>, IIndunMatchmakingManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, List<IndunMatchApplicant>> _queues = [];
    private readonly Dictionary<ulong, IndunMatchSession> _sessions = [];
    private readonly Dictionary<uint, ulong> _characterSession = [];
    private readonly Dictionary<uint, uint> _characterQueueCatalog = [];
    private readonly Lock _lock = new();
    private long _matchingKeySeq;

    /// <summary>Injectable clock for tests; defaults to UTC now.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Starts building the instance copy a match will be offered. Injectable for tests.
    /// </summary>
    public Func<uint, IReadOnlyList<uint>, IPreparedIndunInstance> PrepareInstance { get; set; } =
        DefaultPrepareInstance;

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(OnTick, TimeSpan.FromSeconds(1));
    }

    public bool IsInQueueOrSession(uint characterId)
    {
        lock (_lock)
            return _characterQueueCatalog.ContainsKey(characterId) || _characterSession.ContainsKey(characterId);
    }

    public bool TryApply(uint catalogId, Character character)
    {
        if (character == null)
            return false;

        var dungeonZone = IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogId);
        if (dungeonZone == null)
            return false;

        if (character.Level < dungeonZone.LevelMin || character.Level > dungeonZone.LevelMax)
        {
            Logger.Warn(
                "IndunMatchmaking reject level char={0} catalog={1} level={2} range={3}-{4}",
                character.Name, catalogId, character.Level, dungeonZone.LevelMin, dungeonZone.LevelMax);
            character.SendPacket(new SCAppliedToInstantGamePacket(catalogId, errorMessageId: 1));
            return true;
        }

        if (dungeonZone.PartyOnly)
        {
            var team = TeamManager.Instance.GetTeamByObjId(character.ObjId);
            if (team == null)
            {
                character.SendPacket(new SCAppliedToInstantGamePacket(catalogId, errorMessageId: 1));
                return true;
            }
        }

        var now = UtcNow();
        lock (_lock)
        {
            if (_characterQueueCatalog.ContainsKey(character.Id) || _characterSession.ContainsKey(character.Id))
                return true;

            if (!_queues.TryGetValue(catalogId, out var queue))
            {
                queue = [];
                _queues[catalogId] = queue;
            }

            var team = TeamManager.Instance.GetTeamByObjId(character.ObjId);
            var teamId = team?.Id ?? 0u;
            queue.Add(new IndunMatchApplicant(character.Id, teamId, now));
            _characterQueueCatalog[character.Id] = catalogId;
        }

        Logger.Info("IndunMatchmaking queued char={0} catalog={1} inviteType={2} minMs={3}",
            character.Name, catalogId, dungeonZone.MatchingInvitationTypeId, dungeonZone.MinMatchingTimeMs);
        character.SendPacket(new SCAppliedToInstantGamePacket(catalogId));
        return true;
    }

    public bool TryWithdraw(Character character)
    {
        if (character == null)
            return false;

        IndunMatchSession abandoned = null;
        IPreparedIndunInstance orphan = null;
        var withdrew = false;
        lock (_lock)
        {
            if (_characterSession.TryGetValue(character.Id, out var key) &&
                _sessions.TryGetValue(key, out var session))
            {
                switch (session.Phase)
                {
                    case IndunMatchPhase.Preparing:
                        // The copy has not been offered to anyone yet, so this is just dropping out.
                        var leaving = session.Members.FirstOrDefault(m => m.CharacterId == character.Id);
                        if (leaving != null)
                            leaving.Declined = true;
                        _characterSession.Remove(character.Id);
                        withdrew = true;
                        if (session.Members.All(m => m.Declined))
                            abandoned = session;
                        break;
                    case IndunMatchPhase.Inviting:
                        orphan = DeclineAndMaybeRematch(session, character.Id, rematching: true);
                        withdrew = true;
                        break;
                    // Entering / Done: the match already handed the client Reentry; do not Cancel.
                }
            }

            if (_characterQueueCatalog.TryGetValue(character.Id, out var catalogId))
            {
                if (_queues.TryGetValue(catalogId, out var queue))
                    queue.RemoveAll(a => a.CharacterId == character.Id);
                _characterQueueCatalog.Remove(character.Id);
                withdrew = true;
                Logger.Info("IndunMatchmaking withdraw char={0}", character.Name);
            }
        }

        if (abandoned != null)
        {
            AbandonSession(abandoned, "all members withdrew");
            return true;
        }

        orphan?.Discard();

        // Only ack cancel when we actually pulled them out of queue/invite. A clear while they are
        // mid-enter (or already playing) resets the client instant-game manager and traps leave.
        if (withdrew)
            character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
        return withdrew;
    }

    public bool TryInvitationAnswer(Character character, int invitationTime, bool acceptance)
    {
        if (character == null)
            return false;

        IndunMatchSession session;
        IndunMatchApplicant member;
        IPreparedIndunInstance orphan = null;
        lock (_lock)
        {
            if (!_characterSession.TryGetValue(character.Id, out var key) ||
                !_sessions.TryGetValue(key, out session) ||
                session.Phase != IndunMatchPhase.Inviting)
                return false;

            member = session.Members.FirstOrDefault(m => m.CharacterId == character.Id);
            if (member == null)
                return false;

            if (!acceptance)
                orphan = DeclineAndMaybeRematch(session, character.Id, rematching: true);
            else
            {
                member.Accepted = true;
                // Progress updates keep the Allow Team Queue dialog alive for remaining holdouts.
                // When this accept fills the invite, Reentry closes it — skip the extra ping.
                if (!IndunMatchReadyRules.AllActiveAccepted(session.Members))
                    BroadcastInvitationProgress(session, character.Id);
            }
        }

        if (!acceptance)
        {
            orphan?.Discard();
            return true;
        }

        _ = invitationTime;
        Logger.Info("IndunMatchmaking accept char={0} matchingKey={1}", character.Name, session.MatchingKey);

        if (IndunMatchReadyRules.AllActiveAccepted(session.Members))
            CommitEnter(session);

        return true;
    }

    public bool TryEnter(Character character)
    {
        if (character == null)
            return false;

        IndunMatchSession session;
        lock (_lock)
        {
            if (!_characterSession.TryGetValue(character.Id, out var key) ||
                !_sessions.TryGetValue(key, out session) ||
                session.Phase != IndunMatchPhase.Inviting)
                return false;

            var member = session.Members.FirstOrDefault(m => m.CharacterId == character.Id);
            if (member == null || member.Declined)
                return false;
            member.Accepted = true;
            if (!IndunMatchReadyRules.AllActiveAccepted(session.Members))
                BroadcastInvitationProgress(session, character.Id);
        }

        // Enter commits when all active members have accepted (or solo).
        if (IndunMatchReadyRules.AllActiveAccepted(session.Members))
            CommitEnter(session);
        return true;
    }

    public bool TryLeaveIndunMatch(Character character)
    {
        if (character == null)
            return false;

        // AskLeaveInstantGame only sends this once the client is in playing state (7). Exit the
        // instance copy; matchmaking bookkeeping is already cleared on enter.
        var leftInstance = IndunManager.Instance.RequestLeaveInstance(character);
        TryWithdraw(character);
        return leftInstance;
    }

    private void OnTick(TimeSpan delta)
    {
        var now = UtcNow();
        List<Action> deferred = [];
        lock (_lock)
        {
            ExpireQueuedApplicants(now, deferred);
            FormReadyMatches(now, deferred);
            AdvancePreparingSessions(now, deferred);
            ExpireInvites(now, deferred);
        }

        foreach (var action in deferred)
            action();
    }

    private void ExpireQueuedApplicants(DateTime now, List<Action> deferred)
    {
        foreach (var (catalogId, queue) in _queues.ToList())
        {
            var zone = IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogId);
            if (zone == null)
                continue;

            foreach (var applicant in queue.ToList())
            {
                if (!IndunMatchReadyRules.IsQueueExpired(applicant.AppliedAt, now, zone.ApplyWaitingTimeMs))
                    continue;

                queue.Remove(applicant);
                _characterQueueCatalog.Remove(applicant.CharacterId);
                var charId = applicant.CharacterId;
                deferred.Add(() =>
                {
                    var ch = WorldManager.Instance.GetCharacterById(charId);
                    ch?.SendPacket(SCCancelInstantGamePacket.ClearQueue());
                    Logger.Info("IndunMatchmaking apply timeout charId={0} catalog={1}", charId, catalogId);
                });
            }
        }
    }

    private void FormReadyMatches(DateTime now, List<Action> deferred)
    {
        foreach (var (catalogId, queue) in _queues.ToList())
        {
            if (queue.Count == 0)
                continue;

            var zone = IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogId);
            if (zone == null)
                continue;

            var zoneKey = ResolveZoneKey(zone);
            if (zoneKey == 0)
                continue;

            while (queue.Count > 0)
            {
                var oldest = queue.Min(a => a.AppliedAt);
                if (!IndunMatchReadyRules.IsQueueReady(oldest, now, queue.Count, zone.MaxPlayers,
                        zone.MinMatchingTimeMs))
                    break;

                var group = TakeMatchGroup(queue, zone.MaxPlayers);
                if (group.Count == 0)
                    break;

                foreach (var a in group)
                    _characterQueueCatalog.Remove(a.CharacterId);

                var invitationType = (MatchingInvitationType)zone.MatchingInvitationTypeId;
                var session = CreateSession(catalogId, zone, zoneKey, group, invitationType);
                StartPreparing(session, now, deferred);
            }
        }
    }

    /// <summary>
    /// Registers a formed match and starts building its instance. Players stay on their registered
    /// screen until the copy is ready, which is what keeps entry itself instant.
    /// </summary>
    private void StartPreparing(IndunMatchSession session, DateTime now, List<Action> deferred)
    {
        session.Phase = IndunMatchPhase.Preparing;
        session.PreparingSince = now;
        _sessions[session.MatchingKey] = session;
        foreach (var m in session.Members)
            _characterSession[m.CharacterId] = session.MatchingKey;

        deferred.Add(() => BeginPreparation(session));
    }

    private void BeginPreparation(IndunMatchSession session)
    {
        var memberIds = session.Members.Where(m => !m.Declined).Select(m => m.CharacterId).ToList();
        var prepared = PrepareInstance?.Invoke(session.ZoneKey, memberIds);
        if (prepared == null)
        {
            Logger.Warn("IndunMatchmaking prepare failed catalog={0} matchingKey={1}",
                session.CatalogId, session.MatchingKey);
            AbandonSession(session, "instance could not be prepared");
            return;
        }

        lock (_lock)
            session.Prepared = prepared;

        Logger.Info("IndunMatchmaking preparing catalog={0} matchingKey={1} members={2}",
            session.CatalogId, session.MatchingKey, memberIds.Count);
    }

    private void AdvancePreparingSessions(DateTime now, List<Action> deferred)
    {
        foreach (var session in _sessions.Values.Where(s => s.Phase == IndunMatchPhase.Preparing).ToList())
        {
            var captured = session;
            switch (IndunMatchReadyRules.NextAfterPreparing(session.Prepared?.IsReady == true,
                        session.InvitationType, session.PreparingSince, now))
            {
                case IndunPrepareOutcome.Enter:
                    session.Phase = IndunMatchPhase.Entering;
                    deferred.Add(() => EnterDungeon(captured));
                    break;
                case IndunPrepareOutcome.Offer:
                    session.Phase = IndunMatchPhase.Inviting;
                    session.InviteOpenedAt = now;
                    deferred.Add(() => SendInvites(captured));
                    break;
                case IndunPrepareOutcome.GiveUp:
                    deferred.Add(() => AbandonSession(captured, "instance was not ready in time"));
                    break;
            }
        }
    }

    private void AbandonSession(IndunMatchSession session, string reason)
    {
        List<uint> memberIds;
        lock (_lock)
        {
            if (session.Phase == IndunMatchPhase.Done)
                return;
            memberIds = session.Members.Select(m => m.CharacterId).ToList();
        }

        Logger.Warn("IndunMatchmaking abandon catalog={0} matchingKey={1} reason={2}",
            session.CatalogId, session.MatchingKey, reason);

        CleanupSession(session);
        foreach (var id in memberIds)
            WorldManager.Instance.GetCharacterById(id)?.SendPacket(SCCancelInstantGamePacket.ClearQueue());
    }

    private static IPreparedIndunInstance DefaultPrepareInstance(uint zoneKey, IReadOnlyList<uint> memberIds)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        if (zone == null)
            return null;

        var owner = memberIds.Select(WorldManager.Instance.GetCharacterById).FirstOrDefault(c => c != null);
        return owner == null ? null : IndunManager.Instance.PrepareMatchInstance(zone.Id, owner, memberIds);
    }

    private void ExpireInvites(DateTime now, List<Action> deferred)
    {
        foreach (var session in _sessions.Values.Where(s => s.Phase == IndunMatchPhase.Inviting).ToList())
        {
            if (!IndunMatchReadyRules.IsInviteExpired(session.InviteOpenedAt, now, session.CleanupTermMs))
                continue;

            var keep = session.Members.Where(m => m.Accepted && !m.Declined).ToList();
            var rematch = session.Members.Where(m => !m.Accepted && !m.Declined).ToList();
            var allMembers = session.Members.ToList();

            foreach (var m in allMembers)
                _characterSession.Remove(m.CharacterId);
            _sessions.Remove(session.MatchingKey);

            if (keep.Count > 0)
            {
                session.Members.Clear();
                session.Members.AddRange(keep);
                session.Phase = IndunMatchPhase.Entering;
                foreach (var m in keep)
                    _characterSession[m.CharacterId] = session.MatchingKey;
                _sessions[session.MatchingKey] = session;
                var captured = session;
                deferred.Add(() => EnterDungeon(captured));
            }

            if (keep.Count == 0)
            {
                // Nobody took the copy that was built for this match.
                var orphan = session.Prepared;
                session.Prepared = null;
                session.Phase = IndunMatchPhase.Done;
                if (orphan != null)
                    deferred.Add(orphan.Discard);
            }

            if (rematch.Count > 0)
            {
                Requeue(session.CatalogId, rematch);
                var maxPlayers = session.MaxPlayers;
                deferred.Add(() =>
                {
                    foreach (var m in rematch)
                    {
                        var ch = WorldManager.Instance.GetCharacterById(m.CharacterId);
                        ch?.SendPacket(new SCMatchingInvitationInfoPacket(0, maxPlayers, rematching: true));
                    }
                });
            }
            else if (keep.Count == 0)
            {
                deferred.Add(() =>
                {
                    foreach (var m in allMembers)
                    {
                        var ch = WorldManager.Instance.GetCharacterById(m.CharacterId);
                        ch?.SendPacket(SCCancelInstantGamePacket.ClearQueue());
                    }
                });
            }
        }
    }

    /// <returns>A prepared copy nobody is left to take, for the caller to discard outside the lock.</returns>
    private IPreparedIndunInstance DeclineAndMaybeRematch(IndunMatchSession session, uint characterId,
        bool rematching)
    {
        var member = session.Members.FirstOrDefault(m => m.CharacterId == characterId);
        if (member == null)
            return null;

        member.Declined = true;
        _characterSession.Remove(characterId);

        var remaining = session.Members.Where(m => !m.Declined).ToList();
        if (remaining.Count == 0)
        {
            _sessions.Remove(session.MatchingKey);
            session.Phase = IndunMatchPhase.Done;
            var orphan = session.Prepared;
            session.Prepared = null;
            return orphan;
        }

        BroadcastInvitationProgress(session, characterId, rematching);
        // Leave remaining in invite; they can still accept.
        return null;
    }

    private IndunMatchSession CreateSession(uint catalogId, IndunZone zone, uint zoneKey,
        List<IndunMatchApplicant> group, MatchingInvitationType invitationType)
    {
        var key = (ulong)Interlocked.Increment(ref _matchingKeySeq);
        return new IndunMatchSession
        {
            MatchingKey = key,
            CatalogId = catalogId,
            ZoneGroupId = zone.ZoneGroupId,
            ZoneKey = zoneKey,
            MaxPlayers = zone.MaxPlayers,
            InvitationType = invitationType,
            CleanupTermMs = zone.MatchingCleanupTermMs,
            Members = group,
            InviteOpenedAt = UtcNow()
        };
    }

    private static List<IndunMatchApplicant> TakeMatchGroup(List<IndunMatchApplicant> queue, uint maxPlayers)
    {
        if (queue.Count == 0)
            return [];

        var cap = maxPlayers == 0 ? queue.Count : (int)Math.Min(maxPlayers, (uint)queue.Count);
        // Prefer same team as the oldest applicant.
        var seed = queue.OrderBy(a => a.AppliedAt).First();
        List<IndunMatchApplicant> group;
        if (seed.TeamId != 0)
        {
            group = queue.Where(a => a.TeamId == seed.TeamId).Take(cap).ToList();
            if (group.Count < cap)
            {
                foreach (var extra in queue.Where(a => a.TeamId != seed.TeamId).OrderBy(a => a.AppliedAt))
                {
                    if (group.Count >= cap)
                        break;
                    group.Add(extra);
                }
            }
        }
        else
        {
            group = queue.OrderBy(a => a.AppliedAt).Take(cap).ToList();
        }

        foreach (var a in group)
            queue.Remove(a);
        return group;
    }

    private void Requeue(uint catalogId, List<IndunMatchApplicant> members)
    {
        if (!_queues.TryGetValue(catalogId, out var queue))
        {
            queue = [];
            _queues[catalogId] = queue;
        }

        var now = UtcNow();
        foreach (var m in members)
        {
            var fresh = new IndunMatchApplicant(m.CharacterId, m.TeamId, now);
            queue.Add(fresh);
            _characterQueueCatalog[m.CharacterId] = catalogId;
        }
    }

    private void SendInvites(IndunMatchSession session)
    {
        var zi = new ZoneInstanceId(session.ZoneKey, 0);
        var accept = (uint)IndunMatchReadyRules.AcceptedCount(session.Members);
        var invitationTime = session.CleanupTermMs;
        foreach (var member in session.Members.Where(m => !m.Declined))
        {
            var ch = WorldManager.Instance.GetCharacterById(member.CharacterId);
            if (ch == null)
                continue;
            // maxEntry==1 is the client's dialog picker for "Enter Instance". Sending MaxPlayers
            // here opens "Allow Team Queue" (squad invite UI) even for a solo dungeon Quick Enter.
            ch.SendPacket(new SCInviteToInstantGamePacket(
                invitationTime,
                zi,
                InstantGameWireContract.NoBattleFieldType,
                session.MatchingKey,
                accept,
                InstantGameWireContract.DungeonEnterDialogSelector));
        }

        Logger.Info(
            "IndunMatchmaking invite catalog={0} matchingKey={1} members={2} cleanupMs={3}",
            session.CatalogId, session.MatchingKey, session.Members.Count, session.CleanupTermMs);
    }

    private void BroadcastInvitationProgress(IndunMatchSession session, uint answeringCharacterId,
        bool rematching = false)
    {
        var accept = (uint)IndunMatchReadyRules.AcceptedCount(session.Members);
        var answered = session.Members.FirstOrDefault(m => m.CharacterId == answeringCharacterId);
        foreach (var member in session.Members.Where(m => !m.Declined))
        {
            var ch = WorldManager.Instance.GetCharacterById(member.CharacterId);
            if (ch == null)
                continue;
            ch.SendPacket(new SCMatchingInvitationInfoPacket(accept, session.MaxPlayers, rematching));
            if (answered != null)
                ch.SendPacket(new SCInvitationAnswerPacket(answeringCharacterId, answered.Accepted && !answered.Declined));
        }
    }

    private void CommitEnter(IndunMatchSession session)
    {
        lock (_lock)
        {
            if (session.Phase == IndunMatchPhase.Entering || session.Phase == IndunMatchPhase.Done)
                return;
            session.Phase = IndunMatchPhase.Entering;
        }

        EnterDungeon(session);
    }

    private void EnterDungeon(IndunMatchSession session)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(session.ZoneKey);
        if (zone == null)
        {
            Logger.Warn("IndunMatchmaking missing zone key={0} catalog={1}", session.ZoneKey, session.CatalogId);
            CleanupSession(session);
            return;
        }

        var characters = session.Members
            .Where(m => !m.Declined && (session.InvitationType == MatchingInvitationType.Direct || m.Accepted))
            .Select(m => WorldManager.Instance.GetCharacterById(m.CharacterId))
            .Where(c => c != null)
            .ToList();

        if (characters.Count == 0)
        {
            CleanupSession(session);
            return;
        }

        var leader = characters[0];
        IPreparedIndunInstance preparedHandle;
        lock (_lock)
        {
            preparedHandle = session.Prepared;
            // Ownership moves to the players; session bookkeeping must not Discard this copy.
            session.Prepared = null;
        }

        var preparedDungeon = preparedHandle as Dungeon;
        var worldInstanceId = preparedDungeon?.World?.Id ?? 0u;
        var zi = new ZoneInstanceId(session.ZoneKey, worldInstanceId);
        var now = Helpers.UnixTimeNowInMilli();
        foreach (var ch in characters)
        {
            // Hand the copy over as a match already in progress rather than one being joined. A
            // dungeon has no opening ceremony, and the join path would instead park these players on
            // a battle field's standby screen, which also blocks them from leaving. No
            // "waiting_instance" notice either: the copy was built while they waited on the
            // registered screen, so entering it has nothing left to wait for.
            ch.SendPacket(new SCInstantGameReentryPacket(zi, session.CatalogId,
                InstantGameWireContract.NoBattleFieldType, now));

            SquadManager.Instance.NotifyGameEnter(ch);
        }

        // Prefer the copy matchmaking already built; fall back to a fresh request if prepare failed.
        if (preparedDungeon != null)
        {
            foreach (var ch in characters)
                preparedDungeon.QueuePlayer(ch);
        }
        else
        {
            IndunManager.Instance.RequestDungeonInstance(leader, zone.Id, 0);
            foreach (var ch in characters.Skip(1))
                IndunManager.Instance.RequestDungeonInstance(ch, zone.Id, 0);
        }

        Logger.Info(
            "IndunMatchmaking enter catalog={0} matchingKey={1} players={2} zoneId={3} worldInstance={4}",
            session.CatalogId, session.MatchingKey, characters.Count, zone.Id, worldInstanceId);

        CleanupSession(session);
    }

    private void CleanupSession(IndunMatchSession session)
    {
        IPreparedIndunInstance unused;
        lock (_lock)
        {
            session.Phase = IndunMatchPhase.Done;
            foreach (var m in session.Members)
            {
                _characterSession.Remove(m.CharacterId);
                _characterQueueCatalog.Remove(m.CharacterId);
            }
            _sessions.Remove(session.MatchingKey);

            // Still held here means the match ended without anyone taking the copy.
            unused = session.Prepared;
            session.Prepared = null;
        }

        unused?.Discard();
    }

    private static uint ResolveZoneKey(IndunZone dungeonZone)
    {
        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        if (zoneKeys == null || zoneKeys.Count == 0)
            return 0;
        return zoneKeys[0];
    }

    /// <param name="waitsForOtherPlayers">
    /// Whether the team is willing to be filled with strangers. A team that plays on its own has
    /// nothing to gather, so it is offered an instance straight away instead of sitting out the
    /// matching window that exists to collect more players.
    /// </param>
    public bool TryApplySquad(uint catalogId, uint squadId, IReadOnlyList<uint> memberCharacterIds,
        bool waitsForOtherPlayers)
    {
        if (squadId == 0 || memberCharacterIds == null || memberCharacterIds.Count == 0)
            return false;

        var dungeonZone = IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogId);
        if (dungeonZone == null)
        {
            Logger.Warn("IndunMatchmaking squad apply: unknown catalog={0}", catalogId);
            return false;
        }

        var now = UtcNow();
        var queued = new List<IndunMatchApplicant>();
        foreach (var charId in memberCharacterIds)
        {
            var ch = WorldManager.Instance.GetCharacterById(charId);
            if (ch == null)
                continue;
            if (ch.Level < dungeonZone.LevelMin || ch.Level > dungeonZone.LevelMax)
            {
                ch.SendPacket(new SCAppliedToInstantGamePacket(catalogId, errorMessageId: 1));
                return true;
            }
            TryWithdraw(ch);
            queued.Add(new IndunMatchApplicant(charId, squadId, now));
        }

        if (queued.Count == 0)
            return false;

        var zoneKey = ResolveZoneKey(dungeonZone);
        if (zoneKey == 0)
        {
            Logger.Warn("IndunMatchmaking squad apply: no zone for catalog={0}", catalogId);
            return false;
        }

        if (waitsForOtherPlayers)
        {
            lock (_lock)
            {
                if (!_queues.TryGetValue(catalogId, out var queue))
                {
                    queue = [];
                    _queues[catalogId] = queue;
                }

                foreach (var applicant in queued)
                {
                    queue.RemoveAll(a => a.CharacterId == applicant.CharacterId);
                    queue.Add(applicant);
                    _characterQueueCatalog[applicant.CharacterId] = catalogId;
                }
            }
        }

        // The client only raises the join dialog once it believes it is queued, so the applied ack
        // has to land before any offer we make below.
        foreach (var applicant in queued)
        {
            var ch = WorldManager.Instance.GetCharacterById(applicant.CharacterId);
            ch?.SendPacket(new SCAppliedToInstantGamePacket(catalogId));
        }

        Logger.Info(
            "IndunMatchmaking squad queued catalog={0} squadId={1} members={2} waitsForOthers={3} minMs={4} inviteType={5}",
            catalogId, squadId, queued.Count, waitsForOtherPlayers, dungeonZone.MinMatchingTimeMs,
            dungeonZone.MatchingInvitationTypeId);

        if (!waitsForOtherPlayers)
            OfferInstanceNow(catalogId, dungeonZone, zoneKey, queued);

        return true;
    }

    /// <summary>
    /// Give a team its own copy without going through the matching window. The team still waits for
    /// the copy to be built; what it skips is waiting for strangers to fill the remaining seats.
    /// </summary>
    private void OfferInstanceNow(uint catalogId, IndunZone zone, uint zoneKey,
        List<IndunMatchApplicant> group)
    {
        List<Action> deferred = [];
        lock (_lock)
        {
            var invitationType = (MatchingInvitationType)zone.MatchingInvitationTypeId;
            var session = CreateSession(catalogId, zone, zoneKey, group, invitationType);
            StartPreparing(session, UtcNow(), deferred);
        }

        foreach (var action in deferred)
            action();
    }
}
