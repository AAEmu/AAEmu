using System.Numerics;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Expeditions;
using AAEmu.Game.Utils.DB;

using NLog;
using MySql.Data.MySqlClient;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Core.Managers;

public class ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
    IWorldManager worldManager, IChatManager chatManager,
    IExpeditionPersistenceConnectionFactory persistenceConnections, IItemManager itemManager,
    IFactionManager factionManager, ITaskManager taskManager, IGameDataManager gameDataManager)
    : Singleton<ExpeditionManager>, IExpeditionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    //private ExpeditionConfig _config;
    private Regex _nameRegex;

    private Dictionary<FactionsEnum, Expedition> _expeditions;
    private readonly object _expeditionsSync = new();
    private readonly ConcurrentDictionary<uint, object> _membershipSync = new();
    private readonly ExpeditionInvitationStore _pendingInvitations = new();
    // Ordering-only dependency: Load reads HeirGameData while constructing the offline guild roster.
    private readonly IGameDataManager _gameDataManager = gameDataManager;

    public ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
        IWorldManager worldManager, IChatManager chatManager)
        : this(expeditionIdManager, teamManager, worldManager, chatManager,
            new MySqlExpeditionPersistenceConnectionFactory(), null, null, null, null)
    {
    }

    public ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
        IWorldManager worldManager, IChatManager chatManager,
        IExpeditionPersistenceConnectionFactory persistenceConnections)
        : this(expeditionIdManager, teamManager, worldManager, chatManager, persistenceConnections, null, null, null,
            null)
    {
    }

    public ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
        IWorldManager worldManager, IChatManager chatManager,
        IExpeditionPersistenceConnectionFactory persistenceConnections, IItemManager itemManager)
        : this(expeditionIdManager, teamManager, worldManager, chatManager, persistenceConnections, itemManager, null,
            null, null)
    {
    }

    public ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
        IWorldManager worldManager, IChatManager chatManager,
        IExpeditionPersistenceConnectionFactory persistenceConnections, IItemManager itemManager,
        IFactionManager factionManager)
        : this(expeditionIdManager, teamManager, worldManager, chatManager, persistenceConnections, itemManager,
            factionManager, null, null)
    {
    }

    public ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager,
        IWorldManager worldManager, IChatManager chatManager,
        IExpeditionPersistenceConnectionFactory persistenceConnections, IItemManager itemManager,
        IFactionManager factionManager, ITaskManager taskManager)
        : this(expeditionIdManager, teamManager, worldManager, chatManager, persistenceConnections, itemManager,
            factionManager, taskManager, null)
    {
    }

    /// <summary>
    /// Guild War economy, read from the configured decrypted game.compact content_configs (kind_id = 25), named through
    /// enum_content_configs - same pattern as AuctionFeeSchedule.
    /// TODO: expedition_war_duration and expedition_war_duration_for_protection use different units
    /// (milliseconds vs seconds) - do not assume they match if adding more duration configs here.
    /// </summary>
    private readonly Dictionary<string, long> _contentConfig = [];
    private IItemManager ItemManagerForPersistence => itemManager ?? ItemManager.Instance;
    private IFactionManager FactionManagerForAlliance => factionManager ?? FactionManager.Instance;
    private ITaskManager TaskManagerForScheduling => taskManager ?? TaskManager.Instance;

    private sealed record WarState(uint EnemyId, DateTime? DeclaredAt, DateTime? ProtectedUntil,
        DateTime? EndsAt, uint KillScore, bool IsDeclarer, uint Deposit, uint Wins, uint Losses, uint Draws,
        Dictionary<uint, uint> KillsByMember);

    private static WarState CaptureWarState(Expedition expedition) => new(
        expedition.WarEnemyExpeditionId, expedition.WarDeclaredAt, expedition.WarProtectedUntil,
        expedition.WarEndsAt, expedition.WarKillScore, expedition.WarIsDeclarer, expedition.WarDeposit,
        expedition.WarWins, expedition.WarLosses, expedition.WarDraws,
        new Dictionary<uint, uint>(expedition.WarKillsByMember));

    private static void RestoreWarState(Expedition expedition, WarState state)
    {
        expedition.WarEnemyExpeditionId = state.EnemyId;
        expedition.WarDeclaredAt = state.DeclaredAt;
        expedition.WarProtectedUntil = state.ProtectedUntil;
        expedition.WarEndsAt = state.EndsAt;
        expedition.WarKillScore = state.KillScore;
        expedition.WarIsDeclarer = state.IsDeclarer;
        expedition.WarDeposit = state.Deposit;
        expedition.WarWins = state.Wins;
        expedition.WarLosses = state.Losses;
        expedition.WarDraws = state.Draws;
        expedition.WarKillsByMember.Clear();
        foreach (var (memberId, kills) in state.KillsByMember)
            expedition.WarKillsByMember[memberId] = kills;
    }

    private IDisposable AcquireMembershipLocks(IEnumerable<uint> characterIds)
    {
        var locks = characterIds.Distinct().OrderBy(id => id)
            .Select(id => _membershipSync.GetOrAdd(id, static _ => new object())).ToArray();
        foreach (var sync in locks)
            Monitor.Enter(sync);
        return new MembershipLockLease(locks);
    }

    private sealed class MembershipLockLease(object[] locks) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = locks.Length - 1; index >= 0; index--)
                Monitor.Exit(locks[index]);
        }
    }

    public IDisposable BeginCharacterLoginAssociation(Character character)
    {
        using var connection = persistenceConnections.Open();
        return BeginCharacterLoginAssociation(character, connection);
    }

    public CharacterDeletionGuard BeginCharacterDeletionGuard(uint characterId)
    {
        var ownsPersistenceGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
        if (ownsPersistenceGate)
            PersistenceGate.EnterOperation();
        var membershipSync = _membershipSync.GetOrAdd(characterId, static _ => new object());
        Monitor.Enter(membershipSync);
        Expedition expedition = null;
        try
        {
            lock (_expeditionsSync)
            {
                foreach (var candidate in _expeditions.Values.OrderBy(value => (uint)value.Id))
                {
                    Monitor.Enter(candidate.SyncRoot);
                    if (candidate.GetMember(characterId) != null)
                    {
                        expedition = candidate;
                        break;
                    }
                    Monitor.Exit(candidate.SyncRoot);
                }
            }
            return new CharacterDeletionGuard(membershipSync, expedition, ownsPersistenceGate, characterId);
        }
        catch
        {
            if (expedition != null) Monitor.Exit(expedition.SyncRoot);
            Monitor.Exit(membershipSync);
            if (ownsPersistenceGate) PersistenceGate.ExitOperation();
            throw;
        }
    }

    public sealed class CharacterDeletionGuard(object membershipSync, Expedition expedition,
        bool ownsPersistenceGate, uint characterId) : IDisposable
    {
        private bool _disposed;
        public uint ExpeditionId => expedition == null ? 0u : (uint)expedition.Id;
        public bool IsOwner => expedition?.OwnerId == characterId;
        public void ApplyTo(Character character) => character.Expedition = expedition;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (expedition != null)
                Monitor.Exit(expedition.SyncRoot);
            Monitor.Exit(membershipSync);
            if (ownsPersistenceGate)
                PersistenceGate.ExitOperation();
        }
    }

    internal IDisposable BeginCharacterLoginAssociation(Character character, MySqlConnection connection)
    {
        var ownsPersistenceGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
        if (ownsPersistenceGate)
            PersistenceGate.EnterOperation();
        var membershipSync = _membershipSync.GetOrAdd(character.Id, static _ => new object());
        Monitor.Enter(membershipSync);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT expedition_id,expedition_rejoin_until FROM characters WHERE id=@characterId AND deleted=0";
            command.Parameters.AddWithValue("@characterId", character.Id);
            FactionsEnum expeditionId;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                    throw new InvalidOperationException($"Character {character.Id} disappeared during guild login association.");
                expeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
                character.ExpeditionRejoinUntil = reader.GetInt64("expedition_rejoin_until");
            }
            lock (_expeditionsSync)
                character.Expedition = _expeditions.GetValueOrDefault(expeditionId);
            if (character.Expedition is { } expedition)
            {
                lock (expedition.SyncRoot)
                {
                    var member = expedition.GetMember(character.Id);
                    if (member == null)
                        throw new InvalidOperationException($"Character {character.Id} has a guild id without a roster row.");
                    var seenAt = ServerCalendar.UtcNow;
                    using var updateSeen = connection.CreateCommand();
                    updateSeen.CommandText = "UPDATE expedition_members SET last_leave_time=@seenAt WHERE character_id=@characterId AND expedition_id=@expeditionId";
                    updateSeen.Parameters.AddWithValue("@seenAt", seenAt);
                    updateSeen.Parameters.AddWithValue("@characterId", character.Id);
                    updateSeen.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    if (updateSeen.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Failed to persist guild member session timestamp.");
                    member.LastWorldLeaveTime = seenAt;
                }
            }
            return new CharacterLoginAssociation(membershipSync, ownsPersistenceGate);
        }
        catch
        {
            Monitor.Exit(membershipSync);
            if (ownsPersistenceGate)
                PersistenceGate.ExitOperation();
            throw;
        }
    }

    private sealed class CharacterLoginAssociation(object membershipSync, bool ownsPersistenceGate) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Monitor.Exit(membershipSync);
            if (ownsPersistenceGate)
                PersistenceGate.ExitOperation();
        }
    }

    public IEnumerable<Expedition> Expeditions
    {
        get
        {
            using var persistenceOperation = PersistenceOperationScope.Enter();
            lock (_expeditionsSync)
                return _expeditions.Values.ToArray();
        }
    }

    public long GetContentConfig(string name, long fallback = 0) => _contentConfig.GetValueOrDefault(name, fallback);

    private long WarConfig(string name, long fallback) => GetContentConfig(name, fallback);

    private bool IsCurrentSession(Character character) => character != null &&
        ReferenceEquals(character.Connection?.ActiveChar, character) &&
        ReferenceEquals(worldManager.GetCharacterById(character.Id), character);

    private void PublishMemberRemoved(Expedition expedition, Character character)
    {
        if (ExpeditionPublicAssignmentServices.TryGet(out var publicAssignments))
            publicAssignments.OnCharacterLogout(character);
        chatManager.GetGuildChat(expedition)?.LeaveChannel(character);
        character.Bonuses[Buffs.ExpeditionBonusesIndex] = [];
        character.SendPacket(new SCUnitStatePacket(character));
        character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp), true);
    }

    private static void SendExpeditionList(Character character, IReadOnlyList<Expedition> expeditions)
    {
        foreach (var chunk in ChunkExpeditionList(expeditions))
            character.SendPacket(new SCExpeditionListPacket(chunk));
    }

    internal static IReadOnlyList<Expedition[]> ChunkExpeditionList(IReadOnlyList<Expedition> expeditions)
    {
        if (expeditions.Count == 0)
            return [Array.Empty<Expedition>()];

        var chunks = new List<Expedition[]>();
        for (var index = 0; index < expeditions.Count; index += SCExpeditionListPacket.MaxExpeditionsPerPacket)
            chunks.Add(expeditions.Skip(index).Take(SCExpeditionListPacket.MaxExpeditionsPerPacket).ToArray());
        return chunks;
    }

    /// <summary>Testing override for the war duration in minutes (set via the /gwtime GM command).
    /// 0 = use expedition_war_duration from config (1h on retail).</summary>
    public static int WarDurationTestMinutes { get; set; }

    private void LoadWarEconomyConfig()
    {
        _contentConfig.Clear();

        using var connection = SQLite.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT e.name, c.value FROM content_configs c " +
            "JOIN enum_content_configs e ON e.id = c.id " +
            "WHERE c.kind_id = 25";
        command.Prepare();

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            _contentConfig[reader.GetString("name")] = reader.GetInt32("value");

        Logger.Info($"Loaded {_contentConfig.Count} expedition content configuration values");
    }

    public FactionsEnum GetAllianceId(Expedition expedition)
    {
        var motherId = FactionManagerForAlliance.GetFaction(expedition.MotherId)?.MotherId ?? FactionsEnum.Invalid;
        return motherId == FactionsEnum.Invalid ? expedition.MotherId : motherId;
    }

    private Expedition Create(string name, Character owner, FactionsEnum sponsorId)
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)expeditionIdManager.GetNextId(),
            MotherId = sponsorId,
            Name = name,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            UnitOwnerType = 0,
            PoliticalSystem = 1,
            Created = DateTime.UtcNow,
            LastExpUpdateTime = DateTime.UtcNow,
            AggroLink = false,
            DiplomacyTarget = false,
            Members = []
        };
        expedition.Policies = GetDefaultPolicies(expedition.Id);

        var member = GetMemberFromCharacter(expedition, owner, true);

        expedition.Members.Add(member);

        return expedition;
    }

    public void Load()
    {
        _expeditions = [];
        _pendingInvitations.Clear();
        _nameRegex = new Regex(AppConfiguration.Instance.Expedition.NameRegex, RegexOptions.Compiled);
        LoadWarEconomyConfig();

        using (var connection = persistenceConnections.Open())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM expeditions";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var expedition = new Expedition
                        {
                            Id = (FactionsEnum)reader.GetUInt32("id"),
                            MotherId = (FactionsEnum)reader.GetUInt32("mother"),
                            Name = reader.GetString("name"),
                            OwnerId = reader.GetUInt32("owner"),
                            OwnerName = reader.GetString("owner_name"),
                            UnitOwnerType = 0,
                            PoliticalSystem = 1,
                            Level = reader.GetUInt32("level"),
                            Exp = reader.GetUInt32("exp"),
                            DailyExp = reader.GetUInt32("daily_exp"),
                            LastExpUpdateTime = reader.GetDateTime("last_exp_update_time"),
                            Notice = reader.GetString("notice"),
                            ResidenceHouseId = reader.GetUInt32("residence_house_id"),
                            Interest = reader.GetInt16("interest"),
                            WarDeposit = reader.GetUInt32("war_deposit"),
                            WarWins = reader.GetUInt32("war_wins"),
                            WarLosses = reader.GetUInt32("war_losses"),
                            WarDraws = reader.GetUInt32("war_draws"),
                            DailyContributionPoint = ServerCalendar.IsNewDailyPeriod(
                                reader.GetDateTime("last_contribution_point_added"), ServerCalendar.UtcNow)
                                ? 0u
                                : reader.GetUInt32("daily_contribution_point"),
                            LastContributionPointAdded = reader.GetDateTime("last_contribution_point_added"),
                            LastAssignmentUpdateTime = reader.GetDateTime("last_assignment_update_time"),
                            WarEnemyExpeditionId = reader.GetUInt32("war_enemy_expedition_id"),
                            WarDeclaredAt = reader.IsDBNull(reader.GetOrdinal("war_declared_at")) ? null : reader.GetDateTime("war_declared_at"),
                            WarProtectedUntil = reader.IsDBNull(reader.GetOrdinal("war_protected_until")) ? null : reader.GetDateTime("war_protected_until"),
                            WarEndsAt = reader.IsDBNull(reader.GetOrdinal("war_ends_at")) ? null : reader.GetDateTime("war_ends_at"),
                            WarKillScore = reader.GetUInt32("war_kill_score"),
                            WarIsDeclarer = reader.GetBoolean("war_is_declarer"),
                            Created = reader.GetDateTime("created_at"),
                            AggroLink = false,
                            DiplomacyTarget = false
                        };

                        _expeditions.Add(expedition.Id, expedition);
                    }
                }
            }

            foreach (var expedition in _expeditions.Values)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT em.*, c.faction_id, c.heir_exp FROM expedition_members em " +
                        "INNER JOIN characters c ON c.id = em.character_id " +
                        "WHERE em.expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var member = new ExpeditionMember
                            {
                                CharacterId = reader.GetUInt32("character_id"),
                                ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id"),
                                Role = reader.GetByte("role"),
                                Memo = reader.GetString("memo"),
                                LastWorldLeaveTime = reader.GetDateTime("last_leave_time"),
                                Name = reader.GetString("name"),
                                Level = reader.GetByte("level"),
                                HeirLevel = HeirGameData.Instance.GetLevelForExp(reader.GetInt64("heir_exp")),
                                FactionId = (FactionsEnum)reader.GetUInt32("faction_id"),
                                ContributionPoint = reader.GetUInt32("contribution_point"),
                                WeeklyContributionPoint = reader.GetUInt32("weekly_contribution_point"),
                                WeeklyContributionPeriodStart = reader.GetDateTime("weekly_contribution_period_start"),
                                Abilities =
                                [
                                    reader.GetByte("ability1"), reader.GetByte("ability2"), reader.GetByte("ability3")
                                ],
                                IsOnline = false,
                                InParty = false
                            };
                            expedition.Members.Add(member);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT * FROM expedition_role_policies WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var policy = new ExpeditionRolePolicy
                            {
                                ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id"),
                                Role = reader.GetByte("role"),
                                Name = reader.GetString("name"),
                                DominionDeclare = reader.GetBoolean("dominion_declare"),
                                Invite = reader.GetBoolean("invite"),
                                Expel = reader.GetBoolean("expel"),
                                Promote = reader.GetBoolean("promote"),
                                Dismiss = reader.GetBoolean("dismiss"),
                                Chat = reader.GetBoolean("chat"),
                                ManagerChat = reader.GetBoolean("manager_chat"),
                                SiegeMaster = reader.GetBoolean("siege_master"),
                                JoinSiege = reader.GetBoolean("join_siege"),
                                UseInstance = reader.GetBoolean("use_instance")
                            };
                            expedition.Policies.Add(policy);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT expedition_buff_id, grade FROM expedition_buff_purchases WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            expedition.PurchasedBuffGrades[reader.GetUInt32("expedition_buff_id")] = reader.GetByte("grade");
                    }
                }

                Logger.Info("Expedition loaded: {0} ({1}) - {2} members, {3} policies, level={4}, exp={5}, residenceHouseId={6}",
                    expedition.Name, expedition.Id, expedition.Members.Count, expedition.Policies.Count, expedition.Level, expedition.Exp, expedition.ResidenceHouseId);
            }

            using (var resetDailyContribution = connection.CreateCommand())
            {
                resetDailyContribution.CommandText =
                    "UPDATE expeditions SET daily_contribution_point=0 WHERE last_contribution_point_added<@today";
                resetDailyContribution.Parameters.AddWithValue("@today", ServerCalendar.TodayUtc);
                resetDailyContribution.ExecuteNonQuery();
            }
        }

        // A war's end is normally driven by a one-shot ExpeditionWarEndTask, which does not survive a
        // World restart. Re-arm it for anything still active, and catch up immediately on anything whose
        // deadline already passed while the server was down.
        foreach (var expedition in _expeditions.Values)
        {
            if (!expedition.WarEndsAt.HasValue)
                continue;

            var remaining = expedition.WarEndsAt.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                EndWar(expedition.Id);
            else
                TaskManagerForScheduling.Schedule(new ExpeditionWarEndTask(expedition.Id), remaining);
        }
        EvaluateInactiveOwners();
        TaskManagerForScheduling.CronSchedule(new ExpeditionInactiveOwnerTask(), "0 0 * */1 * *");
    }

    public void EvaluateInactiveOwners()
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        Expedition[] expeditions;
        lock (_expeditionsSync)
            expeditions = _expeditions.Values.ToArray();
        var now = ServerCalendar.UtcNow;
        var ownerHours = GetContentConfig("auto_expedition_change_owner_last_logout_hours_of_owner", 0);
        var candidateHours = GetContentConfig("auto_expedition_change_owner_last_logout_hours_of_candidate", 0);
        var minimumContribution = GetContentConfig(
            "auto_expedition_change_owner_min_contribution_point_of_candidate", 0);
        if (ownerHours <= 0 || candidateHours <= 0 || minimumContribution < 0)
            return;

        foreach (var expedition in expeditions)
        {
            lock (expedition.SyncRoot)
            {
                var successor = InactiveExpeditionOwnerRules.SelectSuccessor(expedition, now,
                    TimeSpan.FromHours(ownerHours), TimeSpan.FromHours(candidateHours),
                    checked((uint)minimumContribution));
                if (successor == null)
                    continue;
                var previousOwner = expedition.GetMember(expedition.OwnerId);
                if (previousOwner == null)
                    continue;
                var previousSuccessorRole = successor.Role;
                previousOwner.Role = 0;
                successor.Role = byte.MaxValue;
                expedition.OwnerId = successor.CharacterId;
                expedition.OwnerName = successor.Name;
                try
                {
                    Save(expedition);
                }
                catch
                {
                    expedition.OwnerId = previousOwner.CharacterId;
                    expedition.OwnerName = previousOwner.Name;
                    previousOwner.Role = byte.MaxValue;
                    successor.Role = previousSuccessorRole;
                    throw;
                }
                expedition.SendPacket(new SCExpeditionOwnerChangedPacket(previousOwner.CharacterId,
                    successor.CharacterId, successor.Name));
                expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(previousOwner, 0));
                expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(successor, 0));
            }
        }
    }

    public static List<ExpeditionRolePolicy> GetDefaultPolicies(FactionsEnum expeditionId)
    {
        var res = new List<ExpeditionRolePolicy>();
        foreach (var rolePolicy in AppConfiguration.Instance.Expedition.RolePolicies)
        {
            var policy = rolePolicy.Clone();
            policy.ExpeditionId = expeditionId;
            res.Add(policy);
        }

        return res;
    }

    public bool TryChangeContributionPoints(Character character, int amount, bool addToWeeklyTotal)
    {
        var expedition = character.Expedition;
        var member = expedition?.GetMember(character);
        if (member == null)
            return false;

        if (amount == 0)
            return true;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        lock (member)
        {
            if (!ReferenceEquals(character.Expedition, expedition) || expedition.GetMember(character) != member)
                return false;
            var newTotal = (long)member.ContributionPoint + amount;
            var weekStart = ServerCalendar.WeekStartMondayUtc;
            var currentWeekly = WeeklyContributionRules.CurrentValue(member.WeeklyContributionPoint,
                member.WeeklyContributionPeriodStart, weekStart);
            var weeklyDelta = addToWeeklyTotal && amount > 0 ? amount : 0;
            var newWeeklyTotal = (long)currentWeekly + weeklyDelta;
            if (newTotal is < 0 or > uint.MaxValue || newWeeklyTotal > uint.MaxValue)
                return false;

            using var connection = persistenceConnections.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE expedition_members SET contribution_point = @contribution_point, weekly_contribution_point = @weekly_contribution_point, weekly_contribution_period_start = @week_start WHERE character_id = @character_id AND expedition_id = @expedition_id";
            command.Parameters.AddWithValue("@contribution_point", (uint)newTotal);
            command.Parameters.AddWithValue("@weekly_contribution_point", (uint)newWeeklyTotal);
            command.Parameters.AddWithValue("@week_start", weekStart);
            command.Parameters.AddWithValue("@character_id", member.CharacterId);
            command.Parameters.AddWithValue("@expedition_id", member.ExpeditionId);
            if (command.ExecuteNonQuery() != 1)
                return false;

            member.ContributionPoint = (uint)newTotal;
            member.WeeklyContributionPoint = (uint)newWeeklyTotal;
            member.WeeklyContributionPeriodStart = weekStart;
        }

        character.SendPacket(new SCAddContributionPointPacket(unchecked((uint)amount), member.ContributionPoint));
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0), worldManager);
        // Refresh the guild overview aggregate after this member's personal contribution balance changes.
        expedition.SendDescriptor(worldManager);
        return true;
    }

    public bool TryAddDailyContributionPoints(Character character, int requestedAmount)
    {
        if (requestedAmount <= 0 || character.Expedition is not { } expedition)
            return false;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            var member = expedition.GetMember(character);
            if (member == null || !ReferenceEquals(character.Expedition, expedition))
                return false;
            lock (member)
            {
                var limit = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level)?.DailyContributionPoint ?? 0;
                var periodStart = ServerCalendar.TodayUtc;
                var contributionAddedAt = ServerCalendar.UtcNow;
                var guildDaily = ServerCalendar.IsNewDailyPeriod(expedition.LastContributionPointAdded, periodStart)
                    ? 0u
                    : expedition.DailyContributionPoint;
                using var connection = persistenceConnections.Open();
                using var transaction = connection.BeginTransaction();
                using (var seed = connection.CreateCommand())
                {
                    seed.Transaction = transaction;
                    seed.CommandText = "INSERT IGNORE INTO expedition_daily_activity (expedition_id,character_id,period_start,contribution_used) VALUES (@expeditionId,@characterId,@periodStart,0)";
                    seed.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    seed.Parameters.AddWithValue("@characterId", character.Id);
                    seed.Parameters.AddWithValue("@periodStart", periodStart);
                    seed.ExecuteNonQuery();
                }
                int used;
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT contribution_used FROM expedition_daily_activity WHERE expedition_id=@expeditionId AND character_id=@characterId AND period_start=@periodStart FOR UPDATE";
                    read.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    read.Parameters.AddWithValue("@characterId", character.Id);
                    read.Parameters.AddWithValue("@periodStart", periodStart);
                    used = Convert.ToInt32(read.ExecuteScalar());
                }
                var accepted = limit <= 0 ? requestedAmount : Math.Min(requestedAmount, Math.Max(0, limit - used));
                var newTotal = (long)member.ContributionPoint + accepted;
                var weekStart = ServerCalendar.WeekStartMondayUtc;
                var currentWeekly = WeeklyContributionRules.CurrentValue(member.WeeklyContributionPoint,
                    member.WeeklyContributionPeriodStart, weekStart);
                var newWeekly = (long)currentWeekly + accepted;
                if (accepted <= 0 || newTotal > uint.MaxValue || newWeekly > uint.MaxValue)
                {
                    transaction.Rollback();
                    return false;
                }
                using (var updateDaily = connection.CreateCommand())
                {
                    updateDaily.Transaction = transaction;
                    updateDaily.CommandText = "UPDATE expedition_daily_activity SET contribution_used=@used WHERE expedition_id=@expeditionId AND character_id=@characterId AND period_start=@periodStart";
                    updateDaily.Parameters.AddWithValue("@used", used + accepted);
                    updateDaily.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    updateDaily.Parameters.AddWithValue("@characterId", character.Id);
                    updateDaily.Parameters.AddWithValue("@periodStart", periodStart);
                    if (updateDaily.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Failed to persist the guild daily contribution counter.");
                }
                using (var updateMember = connection.CreateCommand())
                {
                    updateMember.Transaction = transaction;
                    updateMember.CommandText = "UPDATE expedition_members SET contribution_point=@total,weekly_contribution_point=@weekly,weekly_contribution_period_start=@weekStart WHERE expedition_id=@expeditionId AND character_id=@characterId AND contribution_point=@oldTotal AND weekly_contribution_point=@oldWeekly";
                    updateMember.Parameters.AddWithValue("@total", (uint)newTotal);
                    updateMember.Parameters.AddWithValue("@weekly", (uint)newWeekly);
                    updateMember.Parameters.AddWithValue("@weekStart", weekStart);
                    updateMember.Parameters.AddWithValue("@oldTotal", member.ContributionPoint);
                    updateMember.Parameters.AddWithValue("@oldWeekly", member.WeeklyContributionPoint);
                    updateMember.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    updateMember.Parameters.AddWithValue("@characterId", character.Id);
                    if (updateMember.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Guild contribution state changed during daily credit.");
                }
                var updatedGuildDaily = (uint)Math.Min(uint.MaxValue, (ulong)guildDaily + (uint)accepted);
                using (var updateExpedition = connection.CreateCommand())
                {
                    updateExpedition.Transaction = transaction;
                    updateExpedition.CommandText = "UPDATE expeditions SET daily_contribution_point=@daily,last_contribution_point_added=@addedAt WHERE id=@expeditionId";
                    updateExpedition.Parameters.AddWithValue("@daily", updatedGuildDaily);
                    updateExpedition.Parameters.AddWithValue("@addedAt", contributionAddedAt);
                    updateExpedition.Parameters.AddWithValue("@expeditionId", expedition.Id);
                    if (updateExpedition.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Failed to persist the guild daily contribution summary.");
                }
                transaction.Commit();
                member.ContributionPoint = (uint)newTotal;
                member.WeeklyContributionPoint = (uint)newWeekly;
                member.WeeklyContributionPeriodStart = weekStart;
                expedition.DailyContributionPoint = updatedGuildDaily;
                expedition.LastContributionPointAdded = contributionAddedAt;
                character.SendPacket(new SCAddContributionPointPacket((uint)accepted, member.ContributionPoint));
                expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0), worldManager);
                expedition.SendDescriptor(worldManager);
                return true;
            }
        }
    }

    /// <summary>
    /// Sets the recruitment-board interest bitmask shown as icons in the info panel.
    /// </summary>
    public void SetInterest(Character character, short interest)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition) ||
                expedition.OwnerId != character.Id || expedition.GetMember(character)?.Role != byte.MaxValue)
                return;
            var previousInterest = expedition.Interest;
            expedition.Interest = interest;
            try
            {
                Save(expedition);
            }
            catch
            {
                expedition.Interest = previousInterest;
                throw;
            }
            expedition.SendDescriptor();
        }
    }

    /// <summary>
    /// Sets the guild-notice text shown in the info panel.
    /// </summary>
    public void SetNotice(Character character, string notice)
    {
        if (!ExpeditionTextRules.IsValidNotice(notice))
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition) ||
                expedition.OwnerId != character.Id || expedition.GetMember(character)?.Role != byte.MaxValue)
                return;
            var previousNotice = expedition.Notice;
            expedition.Notice = notice ?? string.Empty;
            try
            {
                Save(expedition);
            }
            catch
            {
                expedition.Notice = previousNotice;
                throw;
            }
            expedition.SendDescriptor();
        }
    }

    public bool RenameExpedition(Character character, uint requestedId, string newName, bool isExpedition)
    {
        var expedition = character.Expedition;
        if (!isExpedition || expedition == null || (uint)expedition.Id != requestedId ||
            string.IsNullOrEmpty(newName) || !_nameRegex.IsMatch(newName))
            return false;

        ItemConsumptionPublication committedPublication;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_expeditionsSync)
        lock (expedition.SyncRoot)
        lock (character.Inventory.MutationSyncRoot)
        {
            if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition) || expedition.OwnerId != character.Id ||
                expedition.GetMember(character)?.Role != byte.MaxValue ||
                string.Equals(newName, expedition.Name, StringComparison.OrdinalIgnoreCase) ||
                _expeditions.Values.Any(other => other.Id != expedition.Id &&
                    string.Equals(newName, other.Name, StringComparison.OrdinalIgnoreCase)))
                return false;

            var ticketId = checked((uint)GetContentConfig("expedition_name_change_ticket"));
            var renamePeriodDays = GetContentConfig("expedition_rename_period");
            if (ticketId == 0 || renamePeriodDays < 0 ||
                !character.Inventory.TryPlanBagConsumption(ticketId, 1, out var consumption))
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                return false;
            }

            var snapshots = consumption.CapturePersistenceSnapshots(ItemManagerForPersistence);
            var now = ServerCalendar.UtcNow;
            using var connection = persistenceConnections.Open();
            using var transaction = connection.BeginTransaction();
            using (var cooldown = connection.CreateCommand())
            {
                cooldown.Transaction = transaction;
                cooldown.CommandText = "SELECT last_renamed_at FROM expedition_renames WHERE expedition_id=@id FOR UPDATE";
                cooldown.Parameters.AddWithValue("@id", expedition.Id);
                var value = cooldown.ExecuteScalar();
                if (value != null && value != DBNull.Value &&
                    ServerCalendar.AsUtc(Convert.ToDateTime(value)).AddDays(renamePeriodDays) > now)
                {
                    transaction.Rollback();
                    return false;
                }
            }
            using (var rename = connection.CreateCommand())
            {
                rename.Transaction = transaction;
                rename.CommandText = "UPDATE expeditions SET name=@newName WHERE id=@id AND name=@oldName";
                rename.Parameters.AddWithValue("@newName", newName);
                rename.Parameters.AddWithValue("@oldName", expedition.Name);
                rename.Parameters.AddWithValue("@id", expedition.Id);
                if (rename.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
            }
            using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText = "INSERT INTO expedition_renames (expedition_id,last_renamed_at) VALUES (@id,@now) ON DUPLICATE KEY UPDATE last_renamed_at=VALUES(last_renamed_at)";
                stamp.Parameters.AddWithValue("@id", expedition.Id);
                stamp.Parameters.AddWithValue("@now", now);
                stamp.ExecuteNonQuery();
            }
            ItemManagerForPersistence.PersistSnapshots(connection, transaction, snapshots);
            transaction.Commit();

            expedition.Name = newName;
            committedPublication = consumption.ApplyCommitted(ItemTaskType.SkillEffectConsumption);
            committedPublication.PublishPackets();
            foreach (var online in worldManager.GetAllCharacters())
                online.SendPacket(new SCFactionRenamedPacket((uint)expedition.Id, newName, false));
        }
        try
        {
            committedPublication.PublishCallbacks();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to publish committed guild rename item callbacks for character {0}", character.Id);
        }
        return true;
    }

    /// <summary>
    /// Adds guild exp and auto-advances the guild's level as far as expedition_levels allows without
    /// requiring an item (see ExpeditionLevelGameData). A level gated behind an item stops the auto
    /// climb and waits for TryLevelUp.
    /// </summary>
    public bool AddExp(Expedition expedition, uint amount)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        if (expedition == null || amount == 0)
            return false;

        ExpeditionExpCommit staged;
        lock (expedition.SyncRoot)
        {
            using var connection = persistenceConnections.Open();
            using var transaction = connection.BeginTransaction();
            if (!TryStageExp(expedition, amount, connection, transaction, out staged))
            {
                transaction.Rollback();
                return false;
            }
            transaction.Commit();
            staged.Apply();
        }
        staged.Publish();
        return true;
    }

    /// <summary>
    /// Persists a planned guild EXP change in a caller-owned transaction without changing live state.
    /// Apply the returned commit only after the transaction commits, then publish after leaving any
    /// broader caller locks.
    /// </summary>
    public bool TryStageExp(Expedition expedition, uint amount, MySqlConnection connection,
        MySqlTransaction transaction, out ExpeditionExpCommit commit)
    {
        commit = null;
        if (expedition == null || amount == 0 || connection == null || transaction == null ||
            !ReferenceEquals(transaction.Connection, connection))
            return false;
        if ((!PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld) ||
            !Monitor.IsEntered(expedition.SyncRoot))
            throw new InvalidOperationException(
                "Caller-owned guild EXP transactions must hold the persistence gate and guild lock through Apply.");

        lock (expedition.SyncRoot)
        {
            var gameData = ExpeditionLevelGameData.Instance;
            var now = ServerCalendar.UtcNow;
            var currentDaily = ServerCalendar.IsNewDailyPeriod(expedition.LastExpUpdateTime, now)
                ? 0u
                : expedition.DailyExp;
            var dailyLimit = gameData.GetLevel(expedition.Level)?.DailyExp ?? 0;
            var accepted = GuildExpProgressionRules.GetAcceptedAmount(amount, currentDaily, dailyLimit);
            if (accepted == 0)
            {
                commit = ExpeditionExpCommit.NoOp(expedition, worldManager);
                return true;
            }

            var nextExpLong = (long)expedition.Exp + accepted;
            var maxLevel = gameData.GetLevel(gameData.MaxLevel);
            if (maxLevel != null && expedition.Exp >= maxLevel.TotalExp)
            {
                commit = ExpeditionExpCommit.NoOp(expedition, worldManager);
                return true;
            }
            if (maxLevel != null && nextExpLong > maxLevel.TotalExp)
                nextExpLong = maxLevel.TotalExp;
            nextExpLong = Math.Min(nextExpLong, uint.MaxValue);
            var nextExp = (uint)nextExpLong;
            if (nextExp == expedition.Exp)
            {
                commit = ExpeditionExpCommit.NoOp(expedition, worldManager);
                return true;
            }

            var applied = nextExp - expedition.Exp;
            var nextDaily = checked(currentDaily + applied);
            var nextLevel = gameData.GetAutoLevelForExp(expedition.Level, nextExp);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE expeditions SET exp=@nextExp,level=@nextLevel,daily_exp=@nextDaily,last_exp_update_time=@now WHERE id=@id AND exp=@oldExp AND level=@oldLevel AND daily_exp=@oldDaily";
            command.Parameters.AddWithValue("@nextExp", nextExp);
            command.Parameters.AddWithValue("@nextLevel", nextLevel);
            command.Parameters.AddWithValue("@nextDaily", nextDaily);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@id", expedition.Id);
            command.Parameters.AddWithValue("@oldExp", expedition.Exp);
            command.Parameters.AddWithValue("@oldLevel", expedition.Level);
            command.Parameters.AddWithValue("@oldDaily", expedition.DailyExp);
            if (command.ExecuteNonQuery() != 1)
                return false;

            commit = new ExpeditionExpCommit(expedition, worldManager, expedition.Exp, expedition.Level,
                expedition.DailyExp, expedition.LastExpUpdateTime, nextExp, nextLevel, nextDaily, now, applied);
            return true;
        }
    }

    public sealed class ExpeditionExpCommit
    {
        private readonly Expedition _expedition;
        private readonly IWorldManager _worldManager;
        private readonly uint _oldExp;
        private readonly uint _oldLevel;
        private readonly uint _oldDaily;
        private readonly DateTime _oldUpdatedAt;
        private readonly uint _newExp;
        private readonly uint _newLevel;
        private readonly uint _newDaily;
        private readonly DateTime _newUpdatedAt;
        private bool _applied;

        internal ExpeditionExpCommit(Expedition expedition, IWorldManager worldManager, uint oldExp, uint oldLevel,
            uint oldDaily, DateTime oldUpdatedAt, uint newExp, uint newLevel, uint newDaily, DateTime newUpdatedAt,
            uint appliedAmount)
        {
            _expedition = expedition;
            _worldManager = worldManager;
            _oldExp = oldExp;
            _oldLevel = oldLevel;
            _oldDaily = oldDaily;
            _oldUpdatedAt = oldUpdatedAt;
            _newExp = newExp;
            _newLevel = newLevel;
            _newDaily = newDaily;
            _newUpdatedAt = newUpdatedAt;
            AppliedAmount = appliedAmount;
        }

        internal static ExpeditionExpCommit NoOp(Expedition expedition, IWorldManager worldManager) =>
            new(expedition, worldManager, expedition.Exp, expedition.Level, expedition.DailyExp,
                expedition.LastExpUpdateTime, expedition.Exp, expedition.Level, expedition.DailyExp,
                expedition.LastExpUpdateTime, 0);

        public uint AppliedAmount { get; }

        public void Apply()
        {
            if ((!PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld) ||
                !Monitor.IsEntered(_expedition.SyncRoot))
                throw new InvalidOperationException(
                    "Caller-owned guild EXP transactions must retain the persistence gate and guild lock through Apply.");
            lock (_expedition.SyncRoot)
            {
                if (_applied)
                    return;
                if (_expedition.Exp != _oldExp || _expedition.Level != _oldLevel ||
                    _expedition.DailyExp != _oldDaily || _expedition.LastExpUpdateTime != _oldUpdatedAt)
                    throw new InvalidOperationException("Guild EXP state changed before the committed plan was applied.");
                _expedition.Exp = _newExp;
                _expedition.Level = _newLevel;
                _expedition.DailyExp = _newDaily;
                _expedition.LastExpUpdateTime = _newUpdatedAt;
                _applied = true;
            }
        }

        public void Publish()
        {
            if (!_applied)
                throw new InvalidOperationException("Apply the committed guild EXP plan before publishing it.");
            if (AppliedAmount == 0)
                return;
            _expedition.SendPacket(new SCExpeditionExpAddPacket(AppliedAmount), _worldManager);
            _expedition.SendDescriptor(_worldManager);
        }
    }

    /// <summary>
    /// Stages the same personal and weekly contribution credit for each eligible guild member in a
    /// caller-owned transaction. The caller must retain the persistence gate and guild lock through
    /// <see cref="ExpeditionContributionCommit.Apply"/>.
    /// </summary>
    public bool TryStageContributionCredits(Expedition expedition, IReadOnlyCollection<uint> characterIds,
        uint amountPerMember, MySqlConnection connection, MySqlTransaction transaction,
        out ExpeditionContributionCommit commit)
    {
        commit = null;
        if (expedition == null || characterIds == null || connection == null || transaction == null ||
            !ReferenceEquals(transaction.Connection, connection))
            return false;
        if ((!PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld) ||
            !Monitor.IsEntered(expedition.SyncRoot))
            throw new InvalidOperationException(
                "Caller-owned guild contribution transactions must hold the persistence gate and guild lock through Apply.");

        var weekStart = ServerCalendar.WeekStartMondayUtc;
        var entries = new List<ExpeditionContributionCommit.Entry>();
        foreach (var characterId in characterIds.Distinct().OrderBy(id => id))
        {
            var member = expedition.GetMember(characterId);
            if (member == null)
                return false;
            var currentWeekly = WeeklyContributionRules.CurrentValue(member.WeeklyContributionPoint,
                member.WeeklyContributionPeriodStart, weekStart);
            var nextContribution = checked(member.ContributionPoint + amountPerMember);
            var nextWeekly = checked(currentWeekly + amountPerMember);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE expedition_members SET contribution_point=@contribution,weekly_contribution_point=@weekly,weekly_contribution_period_start=@weekStart WHERE expedition_id=@expeditionId AND character_id=@characterId AND contribution_point=@oldContribution AND weekly_contribution_point=@oldWeekly";
            command.Parameters.AddWithValue("@contribution", nextContribution);
            command.Parameters.AddWithValue("@weekly", nextWeekly);
            command.Parameters.AddWithValue("@weekStart", weekStart);
            command.Parameters.AddWithValue("@expeditionId", expedition.Id);
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@oldContribution", member.ContributionPoint);
            command.Parameters.AddWithValue("@oldWeekly", member.WeeklyContributionPoint);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    $"Guild contributor {characterId} changed while staging the completion transaction.");
            entries.Add(new ExpeditionContributionCommit.Entry(member, member.ContributionPoint,
                member.WeeklyContributionPoint, member.WeeklyContributionPeriodStart, nextContribution,
                nextWeekly, weekStart));
        }

        commit = new ExpeditionContributionCommit(expedition, worldManager, entries, amountPerMember);
        return true;
    }

    public sealed class ExpeditionContributionCommit
    {
        private readonly Expedition _expedition;
        private readonly IWorldManager _worldManager;
        private readonly IReadOnlyList<Entry> _entries;
        private readonly uint _amountPerMember;
        private bool _applied;

        internal ExpeditionContributionCommit(Expedition expedition, IWorldManager worldManager,
            IReadOnlyList<Entry> entries, uint amountPerMember)
        {
            _expedition = expedition;
            _worldManager = worldManager;
            _entries = entries;
            _amountPerMember = amountPerMember;
        }

        public void Apply()
        {
            if ((!PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld) ||
                !Monitor.IsEntered(_expedition.SyncRoot))
                throw new InvalidOperationException(
                    "Caller-owned guild contribution transactions must retain the persistence gate and guild lock through Apply.");
            if (_applied)
                return;
            foreach (var entry in _entries)
            {
                if (entry.Member.ContributionPoint != entry.OldContribution ||
                    entry.Member.WeeklyContributionPoint != entry.OldWeekly ||
                    entry.Member.WeeklyContributionPeriodStart != entry.OldWeekStart)
                    throw new InvalidOperationException(
                        $"Guild contributor {entry.Member.CharacterId} changed before the committed plan was applied.");
            }
            foreach (var entry in _entries)
            {
                entry.Member.ContributionPoint = entry.NewContribution;
                entry.Member.WeeklyContributionPoint = entry.NewWeekly;
                entry.Member.WeeklyContributionPeriodStart = entry.NewWeekStart;
            }
            _applied = true;
        }

        public void Publish()
        {
            if (!_applied)
                throw new InvalidOperationException("Apply the committed guild contribution plan before publishing it.");
            foreach (var entry in _entries)
            {
                var character = _worldManager.GetCharacterById(entry.Member.CharacterId);
                if (character?.Connection?.ActiveChar == character &&
                    ReferenceEquals(character.Expedition, _expedition))
                    character.SendPacket(new SCAddContributionPointPacket(_amountPerMember,
                        entry.NewContribution));
                bool isCurrentMember;
                lock (_expedition.SyncRoot)
                    isCurrentMember = _expedition.GetMember(entry.Member.CharacterId) == entry.Member;
                if (isCurrentMember)
                    _expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(entry.Member, 0),
                        _worldManager);
            }
        }

        internal sealed record Entry(ExpeditionMember Member, uint OldContribution, uint OldWeekly,
            DateTime OldWeekStart, uint NewContribution, uint NewWeekly, DateTime NewWeekStart);
    }

    /// <summary>
    /// Handles an explicit CSExpeditionLevelUpPacket - confirms a level gated behind
    /// expedition_levels.require_item_id, consuming the item from the requesting member's own
    /// inventory. The owner gate matches the non-consuming level-change effect path.
    /// </summary>
    public bool TryLevelUp(Character character)
    {
        if (!IsCurrentSession(character))
            return false;
        ItemConsumptionPublication committedPublication;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = character.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        lock (character.Inventory.MutationSyncRoot)
        {
        if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition))
            return false;
        var member = expedition.GetMember(character);
        if (member == null || expedition.OwnerId != character.Id || member.Role != byte.MaxValue)
            return false;

        if (!ExpeditionLevelGameData.Instance.TryGetLevelUpRequirement(expedition.Level, expedition.Exp, out var requirement))
            return false;

        if (!character.Inventory.TryPlanBagConsumption(
                requirement.RequireItemId, requirement.RequireItemAmount, out var consumption))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        var snapshots = consumption.CapturePersistenceSnapshots(ItemManagerForPersistence);
        using var connection = persistenceConnections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE expeditions SET level=@newLevel WHERE id=@id AND level=@oldLevel";
        command.Parameters.AddWithValue("@newLevel", requirement.Id);
        command.Parameters.AddWithValue("@oldLevel", expedition.Level);
        command.Parameters.AddWithValue("@id", (uint)expedition.Id);
        if (command.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            return false;
        }
        ItemManagerForPersistence.PersistSnapshots(connection, transaction, snapshots);
        transaction.Commit();

        expedition.Level = requirement.Id;
        committedPublication = consumption.ApplyCommitted(ItemTaskType.ExpeditionCreation);
        committedPublication.PublishPackets();
        expedition.SendDescriptor(worldManager);
        }
        try { committedPublication.PublishCallbacks(); }
        catch (Exception ex) { Logger.Error(ex, "Failed to publish committed guild level-up item callbacks for character {0}", character.Id); }
        return true;
    }

    public bool TryApplyLevelChange(Character character, uint targetLevel)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return false;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            var member = expedition.GetMember(character);
            var levelData = ExpeditionLevelGameData.Instance.GetLevel(targetLevel);
            if (member == null || !GuildLevelChangeRules.CanApply(expedition.OwnerId, character.Id, member.Role,
                    expedition.Level, targetLevel, expedition.Exp, levelData))
                return false;

            var previousLevel = expedition.Level;
            expedition.Level = targetLevel;
            try
            {
                Save(expedition);
            }
            catch
            {
                expedition.Level = previousLevel;
                throw;
            }
            expedition.SendDescriptor();
            return true;
        }
    }

    /// <summary>
    /// Purchases/upgrades one guild prestige-shop buff to <paramref name="targetGrade"/>. Grades must
    /// be purchased in order (can't skip from grade 2 to grade 4); paid for the same way the existing
    /// Guild Contribution Shop spends Contribution Points - straight from the purchasing character's
    /// own contribution_point balance, not a separate guild-pooled currency - but the unlocked grade
    /// applies guild-wide once purchased.
    /// </summary>
    public bool TryPurchaseBuffGrade(Character character, uint buffId, byte targetGrade)
    {
        if (!IsCurrentSession(character))
            return false;
        ItemConsumptionPublication committedPublication = null;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = character.Expedition;
        if (expedition == null)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: {0} has no expedition (buffId={1}, targetGrade={2})", character.Name, buffId, targetGrade);
            return false;
        }

        lock (expedition.SyncRoot)
        {

        var grade = ExpeditionBuffGameData.Instance.GetGrade(buffId, targetGrade);
        if (grade == null)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: no game data for buffId={0} grade={1} (character {2}, expedition {3})", buffId, targetGrade, character.Name, expedition.Name);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var currentGrade = expedition.PurchasedBuffGrades.GetValueOrDefault(buffId, (byte)0);
        if (targetGrade != currentGrade + 1)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: buffId={0} requested grade={1} but current grade is {2} (must buy {3} next) (character {4}, expedition {5})",
                buffId, targetGrade, currentGrade, currentGrade + 1, character.Name, expedition.Name);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        if (expedition.Level < grade.ExpeditionLevelId)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: buffId={0} grade={1} requires expedition level {2}, expedition {3} is level {4}",
                buffId, targetGrade, grade.ExpeditionLevelId, expedition.Name, expedition.Level);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        // expedition_buff_grades.housing requires the guild to already have its Guild Residence placed.
        if (grade.Housing && expedition.ResidenceHouseId == 0)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: buffId={0} grade={1} requires the guild to have a placed Guild Residence, expedition {2} has none",
                buffId, targetGrade, expedition.Name);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        if (grade.Contribution < 0 || grade.Count < 0)
            return false;
        var contributionCost = (uint)grade.Contribution;
        var member = expedition.GetMember(character);
        if (member == null || member.ContributionPoint < contributionCost)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: buffId={0} grade={1} costs {2} contribution, character {3} could not pay",
                buffId, targetGrade, grade.Contribution, character.Name);
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return false;
        }

        lock (member)
        lock (character.Inventory.MutationSyncRoot)
        {
            if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition) ||
                expedition.GetMember(character.Id) != member)
                return false;
            if (member.ContributionPoint < contributionCost)
                return false;
            ItemConsumptionPlan consumption = null;
            IReadOnlyList<ItemPersistenceSnapshot> snapshots = [];
            if (grade.ItemId != 0)
            {
                if (!character.Inventory.TryPlanBagConsumption(grade.ItemId, grade.Count, out consumption))
                {
                    character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                    return false;
                }
                snapshots = consumption.CapturePersistenceSnapshots(ItemManagerForPersistence);
            }

            using var connection = persistenceConnections.Open();
            using var transaction = connection.BeginTransaction();
            using (var lockPurchase = connection.CreateCommand())
            {
                lockPurchase.Transaction = transaction;
                lockPurchase.CommandText = "SELECT grade FROM expedition_buff_purchases WHERE expedition_id=@expeditionId AND expedition_buff_id=@buffId FOR UPDATE";
                lockPurchase.Parameters.AddWithValue("@expeditionId", expedition.Id);
                lockPurchase.Parameters.AddWithValue("@buffId", buffId);
                var storedGrade = lockPurchase.ExecuteScalar();
                var databaseGrade = storedGrade == null || storedGrade == DBNull.Value ? (byte)0 : Convert.ToByte(storedGrade);
                if (databaseGrade != currentGrade)
                {
                    transaction.Rollback();
                    return false;
                }
            }
            if (grade.Contribution > 0)
            {
                using var debit = connection.CreateCommand();
                debit.Transaction = transaction;
                debit.CommandText = "UPDATE expedition_members SET contribution_point=contribution_point-@cost WHERE expedition_id=@expeditionId AND character_id=@characterId AND contribution_point>=@cost";
                debit.Parameters.AddWithValue("@cost", grade.Contribution);
                debit.Parameters.AddWithValue("@expeditionId", expedition.Id);
                debit.Parameters.AddWithValue("@characterId", character.Id);
                if (debit.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
            }
            using (var savePurchase = connection.CreateCommand())
            {
                savePurchase.Transaction = transaction;
                savePurchase.CommandText = "INSERT INTO expedition_buff_purchases (expedition_id,expedition_buff_id,grade) VALUES (@expeditionId,@buffId,@grade) ON DUPLICATE KEY UPDATE grade=VALUES(grade)";
                savePurchase.Parameters.AddWithValue("@expeditionId", expedition.Id);
                savePurchase.Parameters.AddWithValue("@buffId", buffId);
                savePurchase.Parameters.AddWithValue("@grade", targetGrade);
                savePurchase.ExecuteNonQuery();
            }
            if (snapshots.Count > 0)
                ItemManagerForPersistence.PersistSnapshots(connection, transaction, snapshots);
            if (ExpeditionActivityServices.TryGet(out var activityService))
                activityService.RecordBuffPurchase((uint)expedition.Id, member.Name, contributionCost, buffId,
                    targetGrade, ServerCalendar.UtcNow, connection, transaction);
            transaction.Commit();

            member.ContributionPoint -= contributionCost;
            expedition.PurchasedBuffGrades[buffId] = targetGrade;
            if (consumption != null)
            {
                committedPublication = consumption.ApplyCommitted(ItemTaskType.ExpeditionBuffGrade);
                committedPublication.PublishPackets();
            }

            expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0), worldManager);
            expedition.SendPacket(new SCExpeditionBuffsPacket((uint)expedition.Id, expedition.PurchasedBuffGrades), worldManager);
            expedition.SendPacket(new SCExpeditionBuffChangedPacket((int)expedition.Id, (int)buffId, currentGrade, targetGrade), worldManager);
            expedition.ApplyBuffBonusesToAllOnline(worldManager);
            Logger.Info("Expedition buff purchase: {0}'s guild ({1}) bought buff {2} grade {3}", character.Name, expedition.Name, buffId, targetGrade);
        }
        }
        if (committedPublication != null)
        {
            try { committedPublication.PublishCallbacks(); }
            catch (Exception ex) { Logger.Error(ex, "Failed to publish committed guild buff item callbacks for character {0}", character.Id); }
        }
        return true;
    }

    /// <summary>Sends the guild's current full prestige-shop buff state to one client - handles CSExpeditionBuffPacket's "view" request and should also fire on Expedition join/login, mirroring SendExpeditionInfo.</summary>
    public void SendExpeditionBuffs(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        lock (expedition.SyncRoot)
            character.SendPacket(new SCExpeditionBuffsPacket((uint)expedition.Id,
                new Dictionary<uint, byte>(expedition.PurchasedBuffGrades)));
    }

    public Expedition GetExpedition(FactionsEnum id)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_expeditionsSync)
            return _expeditions.GetValueOrDefault(id);
    }

    public void CreateExpedition(string name, FactionsEnum sponsorId, GameConnection connection)
    {
        var owner = connection.ActiveChar;
        if (!IsCurrentSession(owner))
            return;
        var sponsor = FactionManagerForAlliance.GetFaction(sponsorId);
        if (sponsor is not { ShowCreateExpedition: true } || sponsor.MotherId != owner.Faction.MotherId)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        if (owner.Expedition != null)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionAlreadyMember);
            return;
        }

        if (name.Length > 32)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameLength);
            return;
        }

        if (!_nameRegex.IsMatch(name))
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameCharacter);
            return;
        }

        lock (_expeditionsSync)
            foreach (var exp in _expeditions.Values)
                if (string.Equals(name, exp.Name, StringComparison.OrdinalIgnoreCase))
                {
                    connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameExist);
                    return;
                }

        // ----------------- Conditions, can change this...
        var team = teamManager.GetActiveTeamByUnit(owner.Id);
        if (team is not { IsParty: true })
        {
            // We send the same error on number of party members when we don't have a party
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        // Check the number of members in the party that meet the requirements
        List<TeamMember> validMembers = [];
        List<TeamMember> teamMembers = [.. team.Members.ToList()];

        foreach (var m in teamMembers)
        {
            if (m?.Character == null)
                continue;

            if (m.Character.Level < AppConfiguration.Instance.Expedition.Create.Level)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateLevel);
                return;
            }
            if (m.Character.Expedition != null)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMemberExpedition);
                return;
            }
            if (m.Character.Faction.MotherId != owner.Faction.MotherId)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateFaction);
                return;
            }
            validMembers.Add(m);
        }

        validMembers = validMembers.DistinctBy(member => member.Character.Id).ToList();
        if (validMembers.All(member => member.Character.Id != owner.Id))
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        if (validMembers.Count < AppConfiguration.Instance.Expedition.Create.PartyMemberCount)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }
        var foundingLevel = ExpeditionLevelGameData.Instance.GetLevel(1);
        if (foundingLevel is { MemberLimit: > 0 } && validMembers.Count > foundingLevel.MemberLimit)
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionMemberLimit);
            return;
        }

        using var membershipLocks = AcquireMembershipLocks(validMembers.Select(member => member.Character.Id));
        lock (_expeditionsSync)
        {
        if (_expeditions.Values.Any(expedition => string.Equals(name, expedition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionNameExist);
            return;
        }
        if (!IsCurrentSession(owner) || validMembers.Any(member => member.Character.Expedition != null))
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionCreateMemberExpedition);
            return;
        }
        var unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (validMembers.Any(member => member.Character.ExpeditionRejoinUntil > unixNow))
        {
            owner.SendPacket(new SCExpeditionRejoinFailPacket(0, 0,
                validMembers.Max(member => member.Character.ExpeditionRejoinUntil - unixNow)));
            return;
        }
        if (validMembers.Any(member => member.Character.Faction.MotherId != owner.Faction.MotherId))
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionCreateFaction);
            return;
        }
        if (!ReferenceEquals(teamManager.GetActiveTeamByUnit(owner.Id), team) ||
            !team.IsParty ||
            validMembers.Any(member => !IsCurrentSession(member.Character)) ||
            validMembers.Any(member => team.Members.All(current => current?.Character != member.Character)))
        {
            owner.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        Expedition expedition;
        lock (owner.WalletSyncRoot)
        {
            if (owner.Money < AppConfiguration.Instance.Expedition.Create.Cost)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
                return;
            }

            owner.Money -= AppConfiguration.Instance.Expedition.Create.Cost;
            expedition = Create(name, owner, sponsorId);
            _expeditions.Add(expedition.Id, expedition);
            owner.Expedition = expedition;

            foreach (var m in validMembers)
            {
                if (m.Character.Id == owner.Id)
                    continue;
                var invited = m.Character;
                invited.Expedition = expedition;
                expedition.Members.Add(GetMemberFromCharacter(expedition, invited, false));
            }
            try
            {
                PersistNewExpedition(expedition, validMembers.Select(m => m.Character).ToArray(), owner,
                    AppConfiguration.Instance.Expedition.Create.Cost);
            }
            catch
            {
                owner.Money += AppConfiguration.Instance.Expedition.Create.Cost;
                foreach (var member in validMembers)
                    if (ReferenceEquals(member.Character.Expedition, expedition))
                        member.Character.Expedition = null;
                if (_expeditions.GetValueOrDefault(expedition.Id) == expedition)
                    _expeditions.Remove(expedition.Id);
                throw;
            }
        }

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ExpeditionCreation,
            [new MoneyChange(-AppConfiguration.Instance.Expedition.Create.Cost)], []));
        // Client truth (prod x2game.dll): opcode 0x13 is SCFactionRetryRenamePacket and there is no
        // SCFactionCreatedPacket class in this client build, so the old send landed on the wrong handler.
        // Guild creation is announced by the expedition list + descriptor packets around this point.

        var expeditionsSnapshot = _expeditions.Values.ToArray();
        foreach (var online in worldManager.GetAllCharacters())
            SendExpeditionList(online, expeditionsSnapshot);

        foreach (var member in validMembers)
        {
            var joined = member.Character;
            WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(joined.ObjId, 0, (int)expedition.Id);
            joined.BroadcastPacket(
                new SCUnitExpeditionChangedPacket(joined.ObjId, joined.Id, "", joined.Name, 0, (uint)expedition.Id, false), true);
            joined.SendPacket(new SCUnitExpeditionChangedPacket(0, joined.Id, "", joined.Name, 0, (uint)expedition.Id, false));
            SendExpeditionInfo(joined);
            expedition.OnCharacterLogin(joined, worldManager, chatManager);
        }
        }
    }

    public void Invite(GameConnection connection, string invitedName)
    {
        var inviter = connection.ActiveChar;
        var expedition = inviter?.Expedition;
        if (expedition == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
        if (!IsCurrentSession(inviter) || !ReferenceEquals(inviter.Expedition, expedition))
            return;

        var inviterMember = expedition.GetMember(inviter);
        if (inviterMember == null)
        {
            Logger.Info("Invite: {0} rejected - not a member of any Expedition", inviter.Name);
            return;
        }

        var policy = expedition.GetPolicyByRole(inviterMember.Role);
        if (policy == null)
        {
            Logger.Info("Invite: {0} rejected - no ExpeditionRolePolicy found for role {1}", inviter.Name, inviterMember.Role);
            return;
        }
        if (!policy.Invite)
        {
            Logger.Info("Invite: {0} rejected - role {1}'s policy has Invite=false", inviter.Name, inviterMember.Role);
            return;
        }

        var invited = worldManager.GetCharacter(invitedName);
        if (invited == null || !IsCurrentSession(invited))
        {
            Logger.Info("Invite: {0} rejected - target '{1}' not found online", inviter.Name, invitedName);
            return;
        }
        if (invited.Expedition != null)
        {
            Logger.Info("Invite: {0} rejected - target '{1}' already has an Expedition ({2})", inviter.Name, invitedName, invited.Expedition.Id);
            return;
        }
        // Same top-level alliance check Create() already enforces on founding members (Nuia/Haranya/Pirate -
        // MotherId, not the exact race) - Invite had no equivalent, so a guild could pick up members from
        // another faction entirely, which retail doesn't allow.
        if (invited.Faction.MotherId != inviter.Faction.MotherId)
        {
            Logger.Info("Invite: {0} rejected - target '{1}' is a different faction (invited MotherId={2}, inviter MotherId={3})",
                inviter.Name, invitedName, invited.Faction.MotherId, inviter.Faction.MotherId);
            inviter.SendErrorMessage(ErrorMessageType.ExpeditionBadFaction);
            return;
        }

        var level = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level);
        if (level is { MemberLimit: > 0 } && expedition.Members.Count >= level.MemberLimit)
        {
            inviter.SendErrorMessage(ErrorMessageType.ExpeditionMemberLimit);
            return;
        }

        _pendingInvitations.Set(invited.Id, new ExpeditionInvitation(
            expedition.Id, inviter.Id, GetAllianceId(expedition), inviter, invited));

        Logger.Info("Invite: {0} sending SCExpeditionInvitationPacket to {1} (inviter.Id={2}, expedition={3}/{4})",
            inviter.Name, invited.Name, inviter.Id, (uint)expedition.Id, expedition.Name);
        invited.SendPacket(
            new SCExpeditionInvitationPacket(inviter.Id, inviter.Name, (uint)expedition.Id,
                expedition.Name)
        );
        }
    }

    public void ReplyInvite(GameConnection connection, FactionsEnum id1, uint id2, bool reply)
    {
        var invited = connection.ActiveChar;
        if (!_pendingInvitations.TryConsume(invited.Id, invited, id1, id2, out var invitation) || !reply)
            return;

        if (invited.Expedition != null ||
            invited.Faction.MotherId != invitation.MotherId ||
            GetExpedition(invitation.ExpeditionId) is not { } expedition)
            return;

        var inviter = worldManager.GetCharacterById(invitation.InviterId);
        if (inviter == null || !ReferenceEquals(inviter, invitation.InviterSession) ||
            !TryBeginMemberJoin(inviter, invited, expedition, out var transition))
            return;

        using (transition)
        using (var dbConnection = persistenceConnections.Open())
        using (var transaction = dbConnection.BeginTransaction())
        {
            transition.Persist(dbConnection, transaction);
            transaction.Commit();
            transition.Commit();
        }
    }

    public void OnCharacterLogout(Character character)
    {
        var currentCharacter = worldManager.GetCharacterById(character.Id);
        if (currentCharacter != null && !ReferenceEquals(currentCharacter, character))
            return;
        _pendingInvitations.RemoveFor(character.Id, character);
        if (character.Expedition is not { } expedition)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            expedition.OnCharacterLogout(character, worldManager, chatManager);
            var member = expedition.GetMember(character.Id);
            if (member == null)
                return;
            using var connection = persistenceConnections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE expedition_members SET last_leave_time=@leftAt WHERE character_id=@characterId AND expedition_id=@expeditionId";
            command.Parameters.AddWithValue("@leftAt", member.LastWorldLeaveTime);
            command.Parameters.AddWithValue("@characterId", character.Id);
            command.Parameters.AddWithValue("@expeditionId", expedition.Id);
            if (command.ExecuteNonQuery() != 1)
                Logger.Warn("Failed to persist guild logout timestamp for character {0}.", character.Id);
        }
    }

    public bool TryBeginMemberJoin(Character actor, Character candidate, Expedition expedition,
        out ExpeditionMemberJoin transition)
    {
        transition = null;
        var ownsPersistenceGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
        if (ownsPersistenceGate)
            PersistenceGate.EnterOperation();
        var membershipSync = _membershipSync.GetOrAdd(candidate.Id, static _ => new object());
        Monitor.Enter(membershipSync);
        Monitor.Enter(expedition.SyncRoot);
        try
        {
            var unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var actorMember = actor?.Expedition == expedition ? expedition.GetMember(actor) : null;
            var actorPolicy = actorMember == null ? null : expedition.GetPolicyByRole(actorMember.Role);
            var level = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level);
            if (!IsCurrentSession(actor) ||
                !IsCurrentSession(candidate) ||
                expedition.isDisbanded || actorPolicy?.Invite != true || candidate.Expedition != null ||
                candidate.ExpeditionRejoinUntil > unixNow ||
                candidate.Faction.MotherId != GetAllianceId(expedition) ||
                level is { MemberLimit: > 0 } && expedition.Members.Count >= level.MemberLimit)
                return false;

            transition = new ExpeditionMemberJoin(candidate, expedition,
                GetMemberFromCharacter(expedition, candidate, false), membershipSync, ownsPersistenceGate,
                unixNow, worldManager, chatManager);
            return true;
        }
        finally
        {
            if (transition == null)
            {
                Monitor.Exit(expedition.SyncRoot);
                Monitor.Exit(membershipSync);
                if (ownsPersistenceGate)
                    PersistenceGate.ExitOperation();
            }
        }
    }

    public bool TryBeginMemberJoin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition,
        out OfflineExpeditionMemberJoin transition) =>
        TryBeginMemberJoin(actor, candidate, expedition, null, out transition);

    internal bool TryBeginMemberJoin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition,
        FactionsEnum candidateMotherId, out OfflineExpeditionMemberJoin transition) =>
        TryBeginMemberJoin(actor, candidate, expedition, (FactionsEnum?)candidateMotherId, out transition);

    private bool TryBeginMemberJoin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition,
        FactionsEnum? validatedCandidateMotherId, out OfflineExpeditionMemberJoin transition)
    {
        transition = null;
        var ownsPersistenceGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
        if (ownsPersistenceGate)
            PersistenceGate.EnterOperation();
        var membershipSync = _membershipSync.GetOrAdd(candidate.CharacterId, static _ => new object());
        Monitor.Enter(membershipSync);
        Monitor.Enter(expedition.SyncRoot);
        try
        {
            var unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var actorMember = actor?.Expedition == expedition ? expedition.GetMember(actor) : null;
            var actorPolicy = actorMember == null ? null : expedition.GetPolicyByRole(actorMember.Role);
            var candidateMotherId = validatedCandidateMotherId ??
                                    FactionManagerForAlliance.GetFaction(candidate.FactionId)?.MotherId;
            var level = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level);
            if (!IsCurrentSession(actor) ||
                expedition.isDisbanded || actorPolicy?.Invite != true || candidate.ExpeditionId != 0 ||
                candidate.ExpeditionRejoinUntil > unixNow ||
                candidateMotherId != GetAllianceId(expedition) ||
                expedition.GetMember(candidate.CharacterId) != null ||
                level is { MemberLimit: > 0 } && expedition.Members.Count >= level.MemberLimit)
                return false;

            var member = new ExpeditionMember
            {
                ExpeditionId = expedition.Id,
                CharacterId = candidate.CharacterId,
                Name = candidate.Name,
                Level = candidate.Level,
                HeirLevel = candidate.HeirLevel,
                FactionId = candidate.FactionId,
                Abilities = [candidate.Ability1, candidate.Ability2, candidate.Ability3],
                Role = 0,
                Memo = string.Empty,
                LastWorldLeaveTime = DateTime.UtcNow,
                WeeklyContributionPeriodStart = ServerCalendar.WeekStartMondayUtc,
                IsOnline = false
            };
            transition = new OfflineExpeditionMemberJoin(expedition, member, membershipSync, ownsPersistenceGate,
                unixNow, worldManager, chatManager);
            return true;
        }
        finally
        {
            if (transition == null)
            {
                Monitor.Exit(expedition.SyncRoot);
                Monitor.Exit(membershipSync);
                if (ownsPersistenceGate)
                    PersistenceGate.ExitOperation();
            }
        }
    }

    public void ChangeExpeditionRolePolicy(GameConnection connection, ExpeditionRolePolicy policy)
    {
        if (policy == null || !ExpeditionTextRules.IsValidRoleName(policy.Name))
            return;
        var expedition = connection.ActiveChar.Expedition;
        if (expedition == null || expedition.Id != policy.ExpeditionId)
            return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
        if (!IsCurrentSession(connection.ActiveChar) || !ReferenceEquals(connection.ActiveChar.Expedition, expedition)) return;

        var characterMember = expedition.GetMember(connection.ActiveChar);
        if (characterMember == null) return;

        if (characterMember.Role != 255 || expedition.OwnerId != characterMember.CharacterId) return;

        var currentPolicy = expedition.GetPolicyByRole(policy.Role);
        if (currentPolicy == null || currentPolicy.Role == 255)
            return;
        var previousPolicy = currentPolicy.Clone();
        currentPolicy.ApplyPermissionsFrom(policy);
        try
        {
            Save(expedition);
        }
        catch
        {
            currentPolicy.ApplyPermissionsFrom(previousPolicy);
            throw;
        }
        expedition.SendPacket(new SCExpeditionRolePolicyChangedPacket(currentPolicy, true));
        }
    }

    /// <summary>
    /// Removes a character from their Guild
    /// </summary>
    /// <param name="character"></param>
    public void LeaveCurrentSession(Character character) => LeaveCore(character, true);

    public void Leave(Character character) => LeaveCore(character, false);

    private void LeaveCore(Character character, bool requireCurrentSession)
    {
        var expedition = character.Expedition;
        if (expedition == null) return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        using var membershipLock = AcquireMembershipLocks([character.Id]);
        lock (expedition.SyncRoot)
        {
        if ((requireCurrentSession && !IsCurrentSession(character)) || !ReferenceEquals(character.Expedition, expedition))
            return;
        if (expedition.OwnerId == character.Id)
        {
            character.SendErrorMessage(ErrorMessageType.ExpeditionOwnerCannotLeave);
            return;
        }

        var member = expedition.GetMember(character);
        if (member == null)
            return;
        expedition.RemoveMember(member);
        var changedPacket = new SCUnitExpeditionChangedPacket(
            character.ObjId,
            character.Id,
            "",
            character.Name,
            (uint)expedition.Id,
            0,
            false
        );
        character.Expedition = null;
        try
        {
            Save(expedition, character);
        }
        catch
        {
            character.Expedition = expedition;
            expedition.RestoreMember(member);
            throw;
        }
        PublishMemberRemoved(expedition, character);
        WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(character.ObjId, (int)expedition.Id, 0);
        character.BroadcastPacket(changedPacket, true);
        expedition.SendPacket(changedPacket, worldManager);
        // unitId=0 sentinel = "this is about you" - clears the leaving character's own MyExpeditionId cache
        // (without this, their client keeps IsExpedInfoLoaded() pinned to the old, now-stale guild id).
        character.SendPacket(new SCUnitExpeditionChangedPacket(0, character.Id, "", character.Name, (uint)expedition.Id, 0, false));
        }
    }

    public void Kick(GameConnection connection, uint kickedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        if (expedition == null)
            return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        using var membershipLock = AcquireMembershipLocks([kickedId]);
        lock (expedition.SyncRoot)
        {
        if (!IsCurrentSession(character)) return;

        if (!ReferenceEquals(character.Expedition, expedition))
            return;

        var characterMember = expedition.GetMember(character);
        if (characterMember == null || !expedition.GetPolicyByRole(characterMember.Role).Expel)
            return;

        var kicked = expedition.GetMember(kickedId);
        if (kicked == null || kicked.CharacterId == expedition.OwnerId || kicked.CharacterId == character.Id ||
            characterMember.Role <= kicked.Role)
            return;

        expedition.RemoveMember(kicked);

        var kickedChar = worldManager.GetCharacterById(kickedId);

        var changedPacket = new SCUnitExpeditionChangedPacket(kickedChar?.ObjId ?? 0,
            kicked.CharacterId, character.Name, kicked.Name, (uint)expedition.Id, 0, true);

        if (kickedChar is not null)
            kickedChar.Expedition = null;

        try
        {
            Save(expedition, kickedChar);
        }
        catch
        {
            expedition.RestoreMember(kicked);
            if (kickedChar is not null)
                kickedChar.Expedition = expedition;
            throw;
        }

        if (kickedChar is not null)
        {
            PublishMemberRemoved(expedition, kickedChar);
            WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(kickedChar.ObjId, (int)expedition.Id, 0);
            kickedChar.BroadcastPacket(changedPacket, true);
            // unitId=0 sentinel = "this is about you" - clears the kicked character's own MyExpeditionId cache.
            kickedChar.SendPacket(new SCUnitExpeditionChangedPacket(0, kicked.CharacterId, character.Name, kicked.Name, (uint)expedition.Id, 0, true));
        }
        expedition.SendPacket(changedPacket);

        }
    }

    public void ChangeMemberRole(GameConnection connection, byte newRole, uint changedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        if (expedition == null)
            return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {

        if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition))
            return;

        var changerMember = expedition.GetMember(character);
        var changerPolicy = changerMember == null ? null : expedition.GetPolicyByRole(changerMember.Role);
        var targetPolicy = expedition.GetPolicyByRole(newRole);
        var changedMember = expedition.GetMember(changedId);
        if (changerMember == null || changerPolicy?.Promote != true || targetPolicy == null ||
            changedMember == null || changedMember.CharacterId == expedition.OwnerId ||
            changedMember.CharacterId == changerMember.CharacterId ||
            changerMember.Role <= changedMember.Role || changerMember.Role <= newRole)
            return;

        var previousRole = changedMember.Role;
        changedMember.Role = newRole;
        try
        {
            Save(expedition);
        }
        catch
        {
            changedMember.Role = previousRole;
            throw;
        }
        // Member LIST row refresh uses the status broadcast; RoleChanged announces the identity separately.
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(changedMember, 0));
        expedition.SendPacket(new SCExpeditionRoleChangedPacket(changedMember.CharacterId, changedMember.Name, newRole));
        }
    }

    public void ChangeOwner(GameConnection connection, uint newOwnerId)
    {
        var owner = connection.ActiveChar;
        var expedition = owner.Expedition;

        if (expedition == null)
            return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
        if (!IsCurrentSession(owner) || !ReferenceEquals(owner.Expedition, expedition)) return;

        var ownerMember = expedition.GetMember(owner);
        if (ownerMember == null || ownerMember.Role != byte.MaxValue || expedition.OwnerId != owner.Id ||
            newOwnerId == owner.Id)
            return;

        var newOwnerMember = expedition.GetMember(newOwnerId);
        if (newOwnerMember == null) return;

        var previousNewOwnerRole = newOwnerMember.Role;
        newOwnerMember.Role = 255;
        ownerMember.Role = 0;

        expedition.OwnerId = newOwnerId;
        expedition.OwnerName = newOwnerMember.Name;

        try
        {
            Save(expedition);
        }
        catch
        {
            expedition.OwnerId = ownerMember.CharacterId;
            expedition.OwnerName = ownerMember.Name;
            ownerMember.Role = 255;
            newOwnerMember.Role = previousNewOwnerRole;
            throw;
        }

        expedition.SendPacket(
            new SCExpeditionOwnerChangedPacket(
                ownerMember.CharacterId,
                newOwnerMember.CharacterId,
                newOwnerMember.Name
            )
        );
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(ownerMember, 0));
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(newOwnerMember, 0));
        }
    }

    /// <summary>
    /// Guild War step 1: the enemy-member right-click menu calls X2Faction:RequestDeclarationMoney(),
    /// which sends CSRequestDeclarationMoneyPacket. We validate the same preconditions DeclareWar checks,
    /// compute the declaration cost, and reply with SCExpeditionWarDeclarationMoney - that response is
    /// what opens the client's confirm dialog. Only clicking OK there sends CSDeclareExpeditionWarPacket
    /// (which re-validates and actually spends the money). A failed precondition here just surfaces an
    /// error message and sends no response, so no dialog opens - matching "can't declare right now".
    /// </summary>
    public void RequestDeclarationMoney(GameConnection connection, uint targetObjId)
    {
        var character = connection.ActiveChar;
        if (!IsCurrentSession(character))
            return;
        var expedition = character?.Expedition;
        var ownerMember = expedition?.GetMember(character);
        if (ownerMember == null || ownerMember.Role != 255)
        {
            Logger.Debug($"RequestDeclarationMoney: rejected, {character?.Name} is not their expedition's owner");
            return;
        }

        if (expedition.IsAtWar || expedition.IsProtected)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var targetCharacter = worldManager.GetCharacterByObjId(targetObjId);
        var targetExpedition = targetCharacter?.Expedition;
        if (targetExpedition == null || targetExpedition.Id == expedition.Id)
        {
            Logger.Debug($"RequestDeclarationMoney: rejected, target objId {targetObjId} did not resolve to another guild's member (targetCharacter={targetCharacter?.Name})");
            character.SendErrorMessage(ErrorMessageType.ExpeditionNoTarget);
            return;
        }

        if (targetExpedition.IsAtWar || targetExpedition.IsProtected)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var requiredMoney = WarConfig("expedition_war_initial_money_for_declaration", 1000000);
        if (requiredMoney is < 0 or > uint.MaxValue)
            return;
        character.SendPacket(new SCExpeditionWarDeclarationMoney(targetObjId, (uint)requiredMoney));
        Logger.Info($"RequestDeclarationMoney: {expedition.Name} -> {targetExpedition.Name}, cost {requiredMoney} (confirm dialog opened)");
    }

    /// <summary>
    /// Guild War: CSDeclareExpeditionWarPacket was a fully-parsed no-op stub - X2Faction:DeclareExpeditionWar
    /// (declaring war on another guild from the member-search / guild-info window) never had any
    /// server-side reaction, so nothing happened. Only the guild owner can declare, matching the
    /// owner-only gate ChangeOwner already uses (Role 255).
    /// </summary>
    public void DeclareWar(GameConnection connection, uint targetObjId, uint money)
    {
        var character = connection.ActiveChar;
        if (!IsCurrentSession(character))
            return;
        var expedition = character.Expedition;
        var ownerMember = expedition?.GetMember(character);
        if (ownerMember == null || ownerMember.Role != 255)
        {
            Logger.Debug($"DeclareWar: rejected, {character.Name} is not their expedition's owner");
            return;
        }

        if (expedition.IsAtWar || expedition.IsProtected)
        {
            Logger.Debug($"DeclareWar: rejected, {expedition.Name} is already at war or currently protected (WarEndsAt={expedition.WarEndsAt}, WarProtectedUntil={expedition.WarProtectedUntil})");
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var targetCharacter = worldManager.GetCharacterByObjId(targetObjId);
        var targetExpedition = targetCharacter?.Expedition;
        if (targetExpedition == null || targetExpedition.Id == expedition.Id)
        {
            Logger.Debug($"DeclareWar: rejected, target objId {targetObjId} did not resolve to another guild's member (targetCharacter={targetCharacter?.Name})");
            character.SendErrorMessage(ErrorMessageType.ExpeditionNoTarget);
            return;
        }

        if (targetExpedition.IsAtWar || targetExpedition.IsProtected)
        {
            Logger.Debug($"DeclareWar: rejected, target guild {targetExpedition.Name} is already at war or currently protected (WarEndsAt={targetExpedition.WarEndsAt}, WarProtectedUntil={targetExpedition.WarProtectedUntil})");
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var requiredMoney = WarConfig("expedition_war_initial_money_for_declaration", 1000000);
        if (requiredMoney is < 0 or > uint.MaxValue)
            return;
        if (money < requiredMoney)
        {
            character.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
            return;
        }

        using var persistenceOperation = PersistenceOperationScope.Enter();
        var firstGuild = (uint)expedition.Id < (uint)targetExpedition.Id ? expedition : targetExpedition;
        var secondGuild = ReferenceEquals(firstGuild, expedition) ? targetExpedition : expedition;
        lock (firstGuild.SyncRoot)
        lock (secondGuild.SyncRoot)
        lock (character.WalletSyncRoot)
        {
        if (!IsCurrentSession(character) || character.Expedition != expedition || expedition.OwnerId != character.Id ||
            expedition.GetMember(character)?.Role != byte.MaxValue || targetCharacter.Expedition != targetExpedition ||
            expedition.IsAtWar || expedition.IsProtected || targetExpedition.IsAtWar || targetExpedition.IsProtected ||
            character.Money < requiredMoney)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var sourceState = CaptureWarState(expedition);
        var targetState = CaptureWarState(targetExpedition);
        character.Money -= requiredMoney;
        var now = ServerCalendar.UtcNow;
        // expedition_war_duration is in milliseconds (3600000 = 1h) - see _warConfig doc comment.
        // WarDurationTestMinutes (set via /gwtime) overrides it for testing; 0 = use the config value.
        var endsAt = WarDurationTestMinutes > 0
            ? now.AddMinutes(WarDurationTestMinutes)
            : now.AddMilliseconds(WarConfig("expedition_war_duration", 3600000));

        expedition.WarEnemyExpeditionId = (uint)targetExpedition.Id;
        expedition.WarDeclaredAt = now;
        expedition.WarEndsAt = endsAt;
        expedition.WarProtectedUntil = null;
        expedition.WarKillScore = 0;
        expedition.WarKillsByMember.Clear();
        expedition.WarIsDeclarer = true;
        expedition.WarDeposit = checked((uint)requiredMoney);

        targetExpedition.WarEnemyExpeditionId = (uint)expedition.Id;
        targetExpedition.WarDeclaredAt = now;
        targetExpedition.WarEndsAt = endsAt;
        targetExpedition.WarProtectedUntil = null;
        targetExpedition.WarKillScore = 0;
        targetExpedition.WarKillsByMember.Clear();
        targetExpedition.WarIsDeclarer = false;
        targetExpedition.WarDeposit = 0;

        try
        {
            using var dbConnection = persistenceConnections.Open();
            using var transaction = dbConnection.BeginTransaction();
            using (var debit = dbConnection.CreateCommand())
            {
                debit.Transaction = transaction;
                debit.CommandText = "UPDATE characters SET money=money-@cost WHERE id=@characterId AND money>=@cost AND expedition_id=@expeditionId";
                debit.Parameters.AddWithValue("@cost", requiredMoney);
                debit.Parameters.AddWithValue("@characterId", character.Id);
                debit.Parameters.AddWithValue("@expeditionId", expedition.Id);
                if (debit.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Failed to persist the guild war declaration fee.");
            }
            expedition.Save(dbConnection, transaction);
            targetExpedition.Save(dbConnection, transaction);
            transaction.Commit();
            expedition.ConfirmRemovedMembersSaved(expedition.RemovedMemberIds.ToArray());
            targetExpedition.ConfirmRemovedMembersSaved(targetExpedition.RemovedMemberIds.ToArray());
        }
        catch
        {
            character.Money += requiredMoney;
            RestoreWarState(expedition, sourceState);
            RestoreWarState(targetExpedition, targetState);
            throw;
        }

        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DeclareExpeditionWar,
            [new MoneyChange(-requiredMoney)], []));

        // Broadcast server-wide, not just to the two guilds - bystanders get their own "War declared!"
        // banner, same idiom as EndWar's global result broadcast below.
        var endsAtUnix = Helpers.UnixTime(endsAt);
        var declarePacket = new SCExpeditionWarStatePacket((int)expedition.Id, (int)targetExpedition.Id, true, endsAtUnix, false);
        foreach (var onlineCharacter in worldManager.GetAllCharacters())
            onlineCharacter.SendPacket(declarePacket);

        // The guild info panel's own "protectDate" field (SCExpeditionDescPacket) reads WarProtectedUntil/
        // WarEndsAt live off the Expedition now, so both guilds' open info windows pick this up on their
        // next refresh without a dedicated push.
        TaskManagerForScheduling.Schedule(new ExpeditionWarEndTask(expedition.Id), endsAt - now);

        Logger.Info($"Guild War declared: {expedition.Name} ({expedition.Id}) vs {targetExpedition.Name} ({targetExpedition.Id}), ends {endsAt:u}");
        }
    }

    /// <summary>Testing helper (GM /gwend wipe): clears the war/protection state on both guilds outright -
    /// no rewards, no post-war protection - so a fresh war can be declared immediately. Any pending
    /// ExpeditionWarEndTask harmlessly no-ops (EndWar returns early once WarEndsAt is null).</summary>
    public void WipeWar(FactionsEnum expeditionId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        Expedition expedition;
        Expedition enemyExpedition;
        lock (_expeditionsSync)
        {
            if (!_expeditions.TryGetValue(expeditionId, out expedition))
                return;
            _expeditions.TryGetValue((FactionsEnum)expedition.WarEnemyExpeditionId, out enemyExpedition);
        }

        var first = enemyExpedition == null || (uint)expedition.Id < (uint)enemyExpedition.Id ? expedition : enemyExpedition;
        var second = ReferenceEquals(first, expedition) ? enemyExpedition : expedition;
        lock (first.SyncRoot)
        {
        if (second != null)
            Monitor.Enter(second.SyncRoot);
        try
        {
        if (enemyExpedition != null &&
            (expedition.WarEnemyExpeditionId != (uint)enemyExpedition.Id ||
             enemyExpedition.WarEnemyExpeditionId != (uint)expedition.Id))
            return;
        var sourceState = CaptureWarState(expedition);
        var enemyState = enemyExpedition == null ? null : CaptureWarState(enemyExpedition);

        var enemyId = (int)expedition.WarEnemyExpeditionId;
        foreach (var e in new[] { expedition, enemyExpedition })
        {
            if (e == null)
                continue;
            e.WarEndsAt = null;
            e.WarProtectedUntil = null;
            e.WarDeclaredAt = null;
            e.WarEnemyExpeditionId = 0;
            e.WarKillScore = 0;
            e.WarKillsByMember.Clear();
        }
        try
        {
            using var connection = persistenceConnections.Open();
            using var transaction = connection.BeginTransaction();
            expedition.Save(connection, transaction);
            enemyExpedition?.Save(connection, transaction);
            transaction.Commit();
            expedition.ConfirmRemovedMembersSaved(expedition.RemovedMemberIds.ToArray());
            if (enemyExpedition != null)
                enemyExpedition.ConfirmRemovedMembersSaved(enemyExpedition.RemovedMemberIds.ToArray());
        }
        catch
        {
            RestoreWarState(expedition, sourceState);
            if (enemyExpedition != null)
                RestoreWarState(enemyExpedition, enemyState);
            throw;
        }

        expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false, 0, true));
        enemyExpedition?.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false, 0, true));
        Logger.Info($"Guild War WIPED (GM): {expedition.Name} vs {enemyExpedition?.Name ?? enemyId.ToString()}");
        }
        finally
        {
            if (second != null)
                Monitor.Exit(second.SyncRoot);
        }
        }
    }

    /// <summary>GM /endgp: clear a guild's post-war / Ceasefire protection immediately (by name).
    /// Returns the resolved expedition name, or null if no guild by that name.</summary>
    public string EndGuildProtection(string guildName)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_expeditionsSync)
        {
            var expedition = _expeditions.Values.FirstOrDefault(e =>
                string.Equals(e.Name, guildName, StringComparison.OrdinalIgnoreCase));
            if (expedition == null)
                return null;
            lock (expedition.SyncRoot)
            {
                var previous = expedition.WarProtectedUntil;
                expedition.WarProtectedUntil = null;
                try { Save(expedition); }
                catch
                {
                    expedition.WarProtectedUntil = previous;
                    throw;
                }
                expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, 0, false, 0, true));
                Logger.Info($"Guild protection cleared (GM /endgp): {expedition.Name}");
                return expedition.Name;
            }
        }
    }

    /// <summary>Fires when a war's scheduled duration runs out (see ExpeditionWarEndTask, re-armed on Load()).</summary>
    public void EndWar(FactionsEnum expeditionId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        Expedition expedition;
        Expedition enemyExpedition;
        lock (_expeditionsSync)
        {
            if (!_expeditions.TryGetValue(expeditionId, out expedition))
                return;
            _expeditions.TryGetValue((FactionsEnum)expedition.WarEnemyExpeditionId, out enemyExpedition);
        }
        var first = enemyExpedition == null || (uint)expedition.Id < (uint)enemyExpedition.Id ? expedition : enemyExpedition;
        var second = ReferenceEquals(first, expedition) ? enemyExpedition : expedition;
        lock (first.SyncRoot)
        {
        if (second != null)
            Monitor.Enter(second.SyncRoot);
        try
        {
        if (expedition.WarEndsAt == null)
            return;
        if (enemyExpedition != null &&
            (expedition.WarEnemyExpeditionId != (uint)enemyExpedition.Id ||
             enemyExpedition.WarEnemyExpeditionId != (uint)expedition.Id))
            return;

        var now = DateTime.UtcNow;
        var protectionDuration = WarDurationTestMinutes > 0
            ? TimeSpan.FromMinutes(WarDurationTestMinutes)
            : TimeSpan.FromSeconds(WarConfig("expedition_war_duration_for_protection", 0));
        var protectedUntil = now.Add(protectionDuration);
        var ourScore = expedition.WarKillScore;
        var theirScore = enemyExpedition?.WarKillScore ?? 0;

        var declarer = expedition.WarIsDeclarer ? expedition : enemyExpedition;
        var defender = expedition.WarIsDeclarer ? enemyExpedition : expedition;
        var sourceState = CaptureWarState(expedition);
        var enemyState = enemyExpedition == null ? null : CaptureWarState(enemyExpedition);

        expedition.WarEndsAt = null;
        expedition.WarProtectedUntil = expedition.WarIsDeclarer ? null : protectedUntil;
        if (enemyExpedition != null)
        {
            enemyExpedition.WarEndsAt = null;
            enemyExpedition.WarProtectedUntil = enemyExpedition.WarIsDeclarer ? null : protectedUntil;
        }

        var sourceOutcome = ourScore == theirScore ? 0 : ourScore > theirScore ? 1 : -1;
        var enemyOutcome = -sourceOutcome;
        ApplyWarOutcome(expedition, sourceOutcome);
        expedition.WarDeposit = 0;
        if (enemyExpedition != null)
        {
            ApplyWarOutcome(enemyExpedition, enemyOutcome);
            enemyExpedition.WarDeposit = 0;
        }
        List<(ExpeditionMember Member, uint Previous, uint Updated, uint Reward)> sourceRewards;
        List<(ExpeditionMember Member, uint Previous, uint Updated, uint Reward)> enemyRewards;
        try
        {
            using var connection = persistenceConnections.Open();
            using var transaction = connection.BeginTransaction();
            expedition.Save(connection, transaction);
            enemyExpedition?.Save(connection, transaction);
            sourceRewards = PersistWarRewards(expedition, sourceOutcome, connection, transaction);
            enemyRewards = enemyExpedition == null
                ? []
                : PersistWarRewards(enemyExpedition, enemyOutcome, connection, transaction);
            if (declarer != null && defender != null)
            {
                using var history = connection.CreateCommand();
                history.Transaction = transaction;
                history.CommandText = "INSERT INTO expedition_war_histories (declarer_id,declarer_name,defendant_id,defendant_name,declared_at,declarer_kills,defendant_kills) VALUES (@declarerId,@declarerName,@defenderId,@defenderName,@declaredAt,@declarerKills,@defenderKills)";
                history.Parameters.AddWithValue("@declarerId", declarer.Id);
                history.Parameters.AddWithValue("@declarerName", declarer.Name);
                history.Parameters.AddWithValue("@defenderId", defender.Id);
                history.Parameters.AddWithValue("@defenderName", defender.Name);
                history.Parameters.AddWithValue("@declaredAt", sourceState.DeclaredAt ?? now);
                history.Parameters.AddWithValue("@declarerKills", declarer.WarKillScore);
                history.Parameters.AddWithValue("@defenderKills", defender.WarKillScore);
                if (history.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Failed to persist guild war history.");
            }
            transaction.Commit();
            expedition.ConfirmRemovedMembersSaved(expedition.RemovedMemberIds.ToArray());
            if (enemyExpedition != null)
                enemyExpedition.ConfirmRemovedMembersSaved(enemyExpedition.RemovedMemberIds.ToArray());
        }
        catch
        {
            RestoreWarState(expedition, sourceState);
            if (enemyExpedition != null)
                RestoreWarState(enemyExpedition, enemyState);
            throw;
        }

        PublishWarRewards(expedition, sourceRewards);
        if (enemyExpedition != null)
            PublishWarRewards(enemyExpedition, enemyRewards);

        var enemyId = (int)expedition.WarEnemyExpeditionId;
        var defenderUnix = defender != null ? Helpers.UnixTime(protectedUntil) : 0;

        // TODO: order matters here - the terminated war-state packet must go out BEFORE the final
        // kill-score packet, or the client only shows a generic "tied" banner instead of the real result.
        expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false,
            expedition.WarIsDeclarer ? 0 : defenderUnix, true), worldManager);
        enemyExpedition?.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false,
            enemyExpedition.WarIsDeclarer ? 0 : defenderUnix, true), worldManager);

        if (declarer != null && defender != null)
        {
            // result: 1 = the 'id' guild (declarer) won, 2 = the 'id2' guild (defender) won, 0 = draw.
            // This packet only ever displays for bystanders, not the two war participants themselves -
            // broadcast server-wide, same idiom as HeroManager.BroadcastPhaseChange. The two guilds' own
            // members get their personal win/lost banner instead, from the kill-score packet below.
            var declarerScore = declarer.WarKillScore;
            var defenderScore = defender.WarKillScore;
            byte result = declarerScore == defenderScore ? (byte)0 : declarerScore > defenderScore ? (byte)1 : (byte)2;
            var resultPacket = new SCNotifyExpeditionWarResultPacket((uint)declarer.Id, (uint)defender.Id, result);

            foreach (var character in worldManager.GetAllCharacters())
                character.SendPacket(resultPacket);

            declarer.SendPacket(new SCExpeditionWarKillScorePacket(declarer, defender), worldManager);
            defender.SendPacket(new SCExpeditionWarKillScorePacket(defender, declarer), worldManager);
        }

        Logger.Info($"Guild War ended: {declarer?.Name ?? expedition.Name} (declarer, {declarer?.WarKillScore ?? ourScore} kills) vs {defender?.Name ?? enemyExpedition?.Name ?? "?"} (defender, {defender?.WarKillScore ?? theirScore} kills); {defender?.Name ?? "?"} protected {protectionDuration.TotalMinutes}min");
        }
        finally
        {
            if (second != null)
                Monitor.Exit(second.SyncRoot);
        }
        }
    }

    private static void ApplyWarOutcome(Expedition expedition, int outcome)
    {
        if (outcome > 0)
            expedition.WarWins = expedition.WarWins == uint.MaxValue ? uint.MaxValue : expedition.WarWins + 1;
        else if (outcome < 0)
            expedition.WarLosses = expedition.WarLosses == uint.MaxValue ? uint.MaxValue : expedition.WarLosses + 1;
        else
            expedition.WarDraws = expedition.WarDraws == uint.MaxValue ? uint.MaxValue : expedition.WarDraws + 1;
    }

    /// <summary>outcome: 1 = win, -1 = loss, 0 = draw. Pays every member (online or not) directly against
    /// expedition_members, mirroring TryChangeContributionPoints' SQL - most members will be offline by
    /// the time a weeks-long war concludes.</summary>
    private List<(ExpeditionMember Member, uint Previous, uint Updated, uint Reward)> PersistWarRewards(
        Expedition expedition, int outcome, MySqlConnection connection, MySqlTransaction transaction)
    {
        var reward = (int)WarConfig(outcome switch
        {
            1 => "expedition_war_reward_for_win",
            -1 => "expedition_war_reward_for_lose",
            _ => "expedition_war_reward_for_draw"
        }, 0);
        var staged = new List<(ExpeditionMember, uint, uint, uint)>();
        if (reward <= 0)
            return staged;
        foreach (var member in expedition.Members.ToArray())
        {
            lock (member)
            {
                var newTotal = (uint)Math.Clamp((long)member.ContributionPoint + reward, 0, uint.MaxValue);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "UPDATE expedition_members SET contribution_point = @contribution_point WHERE character_id = @character_id AND expedition_id = @expedition_id AND contribution_point = @previous";
                command.Parameters.AddWithValue("@contribution_point", newTotal);
                command.Parameters.AddWithValue("@character_id", member.CharacterId);
                command.Parameters.AddWithValue("@expedition_id", member.ExpeditionId);
                command.Parameters.AddWithValue("@previous", member.ContributionPoint);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Guild war reward state changed for member {member.CharacterId}.");
                staged.Add((member, member.ContributionPoint, newTotal, newTotal - member.ContributionPoint));
            }
        }
        return staged;
    }

    private void PublishWarRewards(Expedition expedition,
        IReadOnlyList<(ExpeditionMember Member, uint Previous, uint Updated, uint Reward)> rewards)
    {
        foreach (var reward in rewards)
        {
            lock (reward.Member)
                reward.Member.ContributionPoint = reward.Updated;
            var character = worldManager.GetCharacterById(reward.Member.CharacterId);
            if (character == null)
                continue;

            character.SendPacket(new SCAddContributionPointPacket(reward.Reward, reward.Updated));
            expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(reward.Member, 0), worldManager);
        }
        expedition.SendDescriptor(worldManager);
    }

    /// <summary>
    /// Called from CharacterCombat.DoDie on every PvP kill - credits the killer's expedition's war score if
    /// killer and victim's guilds are the current active war enemies of each other.
    /// </summary>
    public void RegisterWarKill(Character killer, Character victim)
    {
        var killerExpedition = killer.Expedition;
        var victimExpedition = victim.Expedition;
        if (killerExpedition == null || victimExpedition == null)
            return;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        var first = (uint)killerExpedition.Id < (uint)victimExpedition.Id ? killerExpedition : victimExpedition;
        var second = ReferenceEquals(first, killerExpedition) ? victimExpedition : killerExpedition;
        lock (first.SyncRoot)
        lock (second.SyncRoot)
        {

        if (killer.Expedition != killerExpedition || victim.Expedition != victimExpedition ||
            !killerExpedition.IsAtWar || !victimExpedition.IsAtWar ||
            killerExpedition.WarEnemyExpeditionId != (uint)victimExpedition.Id ||
            victimExpedition.WarEnemyExpeditionId != (uint)killerExpedition.Id)
        {
            Logger.Debug($"RegisterWarKill: {killer.Name} killed {victim.Name} but no active war between {killerExpedition.Name} and {victimExpedition.Name} (IsAtWar={killerExpedition.IsAtWar}, enemyId={killerExpedition.WarEnemyExpeditionId})");
            return;
        }

        var previousScore = killerExpedition.WarKillScore;
        killerExpedition.WarKillsByMember.TryGetValue(killer.Id, out var memberKills);
        killerExpedition.WarKillScore++;
        killerExpedition.WarKillsByMember[killer.Id] = memberKills + 1;
        try
        {
            Save(killerExpedition);
        }
        catch
        {
            killerExpedition.WarKillScore = previousScore;
            if (memberKills == 0)
                killerExpedition.WarKillsByMember.Remove(killer.Id);
            else
                killerExpedition.WarKillsByMember[killer.Id] = memberKills;
            throw;
        }
        Logger.Info($"Guild War kill: {killer.Name} ({killerExpedition.Name}) killed {victim.Name} ({victimExpedition.Name}) - score now {killerExpedition.WarKillScore} (this member: {memberKills + 1})");

        // Push the updated scoreboard to both guilds so open scoreboards update without waiting for
        // the client's next poll.
        killerExpedition.SendPacket(new SCExpeditionWarKillScorePacket(killerExpedition, victimExpedition));
        victimExpedition.SendPacket(new SCExpeditionWarKillScorePacket(victimExpedition, killerExpedition));
        }
    }

    /// <summary>Answers CSExpeditionWarKillScorePacket - the client's periodic guild-war-scoreboard poll.</summary>
    public void SendWarKillScore(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        var expedition = character?.Expedition;
        if (expedition == null || (!expedition.IsAtWar && !expedition.IsProtected))
            return;

        _expeditions.TryGetValue((FactionsEnum)expedition.WarEnemyExpeditionId, out var enemyExpedition);
        character.SendPacket(new SCExpeditionWarKillScorePacket(expedition, enemyExpedition));
    }

    /// <summary>
    /// Backs the "정전 협정서"/Ceasefire Agreement item (id 52121, use_skill_id 31460) via
    /// ProtectionForExpedition's special effect (protection_for_expedition, value1 = duration in seconds,
    /// 172800 on this build) - was a declared-but-empty TODO stub, found while chasing why a declared war
    /// "had no effect" (the target guild had used the item, and nothing server-side ever recorded it).
    /// Cannot be used while already at war - a live war still has to run its course or be waited out.
    /// </summary>
    public void SetProtection(Character character, int durationSeconds)
    {
        var expedition = character.Expedition;
        if (expedition == null || durationSeconds <= 0)
            return;

        if (expedition.IsAtWar)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            var previousDeadline = expedition.WarProtectedUntil;
            var baseTime = previousDeadline is { } deadline && deadline > DateTime.UtcNow
                ? deadline
                : DateTime.UtcNow;
            expedition.WarProtectedUntil = baseTime.AddSeconds(durationSeconds);
            try
            {
                Save(expedition);
            }
            catch
            {
                expedition.WarProtectedUntil = previousDeadline;
                throw;
            }
            expedition.SendDescriptor();
        }

        Logger.Info($"Guild War protection: {expedition.Name} protected until {expedition.WarProtectedUntil:u} (via Ceasefire Agreement, used by {character.Name})");
    }

    /// <summary>Backs the guild info panel's "cancel protection" button - CSCancelExpeditionProtectionPacket
    /// was a fully-parsed (empty-body) no-op stub. Owner-only, matching that button's own visibility gate.</summary>
    public void CancelProtection(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
        var ownerMember = expedition.GetMember(character);
        if (!IsCurrentSession(character) || !ReferenceEquals(character.Expedition, expedition) || expedition.OwnerId != character.Id ||
            ownerMember?.Role != byte.MaxValue || !expedition.IsProtected)
            return;
        var previous = expedition.WarProtectedUntil;
        expedition.WarProtectedUntil = null;
        try { Save(expedition); }
        catch
        {
            expedition.WarProtectedUntil = previous;
            throw;
        }
            expedition.SendDescriptor();
        }

        Logger.Info($"Guild War protection cancelled early for {expedition.Name} by {character.Name}");
    }

    public bool Disband(Character owner)
    {
        var guild = owner.Expedition;
        if (guild == null)
        {
            // Error, not in a guild
            owner.SendErrorMessage(ErrorMessageType.OnlyExpeditionMember);
            return false;
        }
        if (guild.OwnerId != owner.Id)
        {
            // Error, only guild owner can disband
            owner.SendErrorMessage(ErrorMessageType.OnlyExpeditionOwner);
            return false;
        }
        using var persistenceOperation = PersistenceOperationScope.Enter();
        uint[] memberIds;
        lock (guild.SyncRoot)
            memberIds = guild.Members.Select(member => member.CharacterId).OrderBy(id => id).ToArray();
        using var membershipLocks = AcquireMembershipLocks(memberIds);
        lock (_expeditionsSync)
        lock (guild.SyncRoot)
        {
        if (!IsCurrentSession(owner) || guild.OwnerId != owner.Id || guild.isDisbanded || !ReferenceEquals(owner.Expedition, guild) ||
            !guild.Members.Select(member => member.CharacterId).OrderBy(id => id).SequenceEqual(memberIds))
            return false;
        var originalMembers = guild.Members.ToArray();
        var originalName = guild.Name;
        var originalOwnerId = guild.OwnerId;
        var onlineMembers = new List<Character>();
        for (var i = guild.Members.Count - 1; i >= 0; i--)
        {
            var c = worldManager.GetCharacterById(guild.Members[i].CharacterId);
            if (c != null)
            {
                c.Expedition = null;
                onlineMembers.Add(c);
            }
            guild.RemoveMember(guild.Members[i]);
        }
        guild.Name = "$deleted-guild-" + guild.Id;
        guild.OwnerId = 0;
        guild.isDisbanded = true;
        try
        {
            Save(guild, onlineMembers.ToArray());
        }
        catch
        {
            guild.Name = originalName;
            guild.OwnerId = originalOwnerId;
            guild.isDisbanded = false;
            foreach (var member in originalMembers)
                guild.RestoreMember(member);
            foreach (var character in onlineMembers)
                character.Expedition = guild;
            throw;
        }
        lock (_expeditionsSync)
            _expeditions.Remove(guild.Id);

        foreach (var c in onlineMembers)
        {
            PublishMemberRemoved(guild, c);
            if (c.IsOnline)
                c.SendPacket(new SCExpeditionDismissedPacket((uint)guild.Id, true));
            var changedPacket = new SCUnitExpeditionChangedPacket(
                c.ObjId, c.Id, owner.Name, c.Name, (uint)guild.Id, 0, false);
            WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(c.ObjId, (int)guild.Id, 0);
            c.BroadcastPacket(changedPacket, true);
            c.SendPacket(new SCUnitExpeditionChangedPacket(
                0, c.Id, owner.Name, c.Name, (uint)guild.Id, 0, false));
        }

        Expedition[] expeditionsSnapshot;
        lock (_expeditionsSync)
            expeditionsSnapshot = _expeditions.Values.ToArray();
        foreach (var online in worldManager.GetAllCharacters())
            SendExpeditionList(online, expeditionsSnapshot);
        return true;
        }
    }

    public static void SendExpeditionInfo(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;
        lock (expedition.SyncRoot)
        {
        var members = expedition.Members.ToArray();
        var total = (uint)members.Length;
        var id = expedition.Id;

        Logger.Info("SendExpeditionInfo: {0} -> guild {1} ({2}), {3} members, {4} policies, totalContribution={5} (per-member: {6})",
            character.Name, expedition.Name, id, total, expedition.Policies.Count,
            expedition.TotalContributionPoint,
            string.Join(",", members.Select(m => $"{m.Name}={m.ContributionPoint}")));

        // TODO: send order matters - desc must go out before RolePolicyList/MemberList, or the client's
        // level-up button and role permissions latch against stale cached values.
        expedition.SendDescriptor(character);
        character.SendPacket(new SCExpeditionRolePolicyListPacket(expedition.Policies));

        for (var i = 0; i < members.Length; i += 20)
        {
            var block = members.Skip(i).Take(20).ToList();
            character.SendPacket(new SCExpeditionMemberListPacket((uint)id, block));
        }

        character.SendPacket(new SCExpeditionMemberListEndPacket((int)total, (int)id));
        character.SendPacket(new SCExpeditionBuffsPacket((uint)id, expedition.PurchasedBuffGrades));

        // Guild War: a client loses X2Faction's war/protection state on disconnect, so a (re)connecting
        // member would see the enemy guild as friendly (green) and be unable to target them. Re-push it
        // here, the same shape DeclareWar/EndWar broadcast - see also ExpeditionWarEndTask re-arm on Load().
        var exp = expedition;
        if (exp.IsAtWar || exp.IsProtected)
        {
            var until = Helpers.UnixTime(exp.WarEndsAt ?? exp.WarProtectedUntil ?? DateTime.UtcNow);
            character.SendPacket(new SCExpeditionWarStatePacket((int)id, (int)exp.WarEnemyExpeditionId, exp.IsAtWar, until, false));
        }
        }
    }

    public sealed class ExpeditionMemberJoin : IDisposable
    {
        private readonly Character _candidate;
        private readonly Expedition _expedition;
        private readonly ExpeditionMember _member;
        private readonly object _membershipSync;
        private readonly bool _ownsPersistenceGate;
        private readonly long _unixNow;
        private readonly IWorldManager _worldManager;
        private readonly IChatManager _chatManager;
        private bool _persisted;
        private bool _completed;

        internal ExpeditionMemberJoin(Character candidate, Expedition expedition,
            ExpeditionMember member, object membershipSync, bool ownsPersistenceGate, long unixNow,
            IWorldManager worldManager = null, IChatManager chatManager = null)
        {
            _candidate = candidate;
            _expedition = expedition;
            _member = member;
            _membershipSync = membershipSync;
            _ownsPersistenceGate = ownsPersistenceGate;
            _unixNow = unixNow;
            _worldManager = worldManager;
            _chatManager = chatManager;
            candidate.Expedition = expedition;
            expedition.Members.Add(member);
        }

        public void Persist(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_completed || _persisted)
                throw new InvalidOperationException("The guild membership transition is no longer writable.");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE characters SET expedition_id=@expeditionId,expedition_rejoin_until=0 WHERE id=@characterId AND deleted=0 AND expedition_id=0 AND expedition_rejoin_until<=@unixNow";
            command.Parameters.AddWithValue("@expeditionId", (int)_expedition.Id);
            command.Parameters.AddWithValue("@characterId", _candidate.Id);
            command.Parameters.AddWithValue("@unixNow", _unixNow);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Joining character is no longer guildless.");
            _member.Save(connection, transaction);
            _persisted = true;
        }

        public void Commit()
        {
            if (_completed || !_persisted)
                throw new InvalidOperationException("Persist and commit the database transaction before publishing guild membership.");
            _completed = true;
            try
            {
                if (_candidate.Expedition != _expedition || !_expedition.Members.Contains(_member))
                    return;
                WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(_candidate.ObjId, 0, (int)_expedition.Id);
                _candidate.BroadcastPacket(new SCUnitExpeditionChangedPacket(
                    _candidate.ObjId, _candidate.Id, "", _candidate.Name, 0, (uint)_expedition.Id, false), true);
                _candidate.SendPacket(new SCUnitExpeditionChangedPacket(
                    0, _candidate.Id, "", _candidate.Name, 0, (uint)_expedition.Id, false));
                SendExpeditionInfo(_candidate);
                _expedition.OnCharacterLogin(_candidate, _worldManager, _chatManager);
            }
            finally
            {
                Monitor.Exit(_expedition.SyncRoot);
                Monitor.Exit(_membershipSync);
                if (_ownsPersistenceGate)
                    PersistenceGate.ExitOperation();
            }
        }

        public void Dispose()
        {
            if (_completed)
                return;
            if (ReferenceEquals(_candidate.Expedition, _expedition))
                _candidate.Expedition = null;
            _expedition.Members.Remove(_member);
            _completed = true;
            Monitor.Exit(_expedition.SyncRoot);
            Monitor.Exit(_membershipSync);
            if (_ownsPersistenceGate)
                PersistenceGate.ExitOperation();
        }
    }

    public sealed class OfflineExpeditionMemberJoin : IDisposable
    {
        private readonly Expedition _expedition;
        private readonly ExpeditionMember _member;
        private readonly object _membershipSync;
        private readonly bool _ownsPersistenceGate;
        private readonly long _unixNow;
        private readonly IWorldManager _worldManager;
        private readonly IChatManager _chatManager;
        private bool _persisted;
        private bool _completed;

        internal OfflineExpeditionMemberJoin(Expedition expedition, ExpeditionMember member, object membershipSync,
            bool ownsPersistenceGate, long unixNow, IWorldManager worldManager, IChatManager chatManager)
        {
            _expedition = expedition;
            _member = member;
            _membershipSync = membershipSync;
            _ownsPersistenceGate = ownsPersistenceGate;
            _unixNow = unixNow;
            _worldManager = worldManager;
            _chatManager = chatManager;
            expedition.Members.Add(member);
        }

        public void Persist(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_completed || _persisted)
                throw new InvalidOperationException("The offline guild membership transition is no longer writable.");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE characters SET expedition_id=@expeditionId,expedition_rejoin_until=0 WHERE id=@characterId AND deleted=0 AND expedition_id=0 AND expedition_rejoin_until<=@unixNow";
            command.Parameters.AddWithValue("@expeditionId", (int)_expedition.Id);
            command.Parameters.AddWithValue("@characterId", _member.CharacterId);
            command.Parameters.AddWithValue("@unixNow", _unixNow);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Offline applicant is no longer guildless.");
            _member.Save(connection, transaction);
            _persisted = true;
        }

        public void Commit()
        {
            if (_completed || !_persisted)
                throw new InvalidOperationException("Persist and commit the database transaction before publishing guild membership.");
            _completed = true;
            try
            {
                var liveCharacter = _worldManager.GetCharacterById(_member.CharacterId);
                if (liveCharacter != null && liveCharacter.Expedition == null)
                {
                    liveCharacter.Expedition = _expedition;
                    liveCharacter.ExpeditionRejoinUntil = 0;
                    _member.Refresh(liveCharacter);
                    WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(
                        liveCharacter.ObjId, 0, (int)_expedition.Id);
                    SendExpeditionInfo(liveCharacter);
                    _expedition.OnCharacterLogin(liveCharacter, _worldManager, _chatManager);
                }
                if (_expedition.Members.Contains(_member))
                    _expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(_member, 0), _worldManager);
            }
            finally
            {
                Monitor.Exit(_expedition.SyncRoot);
                Monitor.Exit(_membershipSync);
                if (_ownsPersistenceGate)
                    PersistenceGate.ExitOperation();
            }
        }

        public void Dispose()
        {
            if (_completed) return;
            _expedition.Members.Remove(_member);
            _completed = true;
            Monitor.Exit(_expedition.SyncRoot);
            Monitor.Exit(_membershipSync);
            if (_ownsPersistenceGate)
                PersistenceGate.ExitOperation();
        }
    }

    public void Save(Expedition expedition, params Character[] characters)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
        var removedMembers = expedition.RemovedMemberIds.ToArray();
        var rejoinUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                          GetContentConfig("expedition_rejoin", 0) * 60 * 60;
        expedition.RemovedMemberRejoinUntil = rejoinUntil;
        using var connection = persistenceConnections.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            expedition.Save(connection, transaction);
            transaction.Commit();
            expedition.ConfirmRemovedMembersSaved(removedMembers);
            foreach (var character in characters.Where(c => c != null).DistinctBy(c => c.Id))
                if (character.Expedition == null)
                    character.ExpeditionRejoinUntil = rejoinUntil;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        }
    }

    public bool TrySetResidenceHouseId(Expedition expedition, uint expectedHouseId, uint newHouseId)
    {
        if (expedition == null)
            return false;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (expedition.isDisbanded || expedition.ResidenceHouseId != expectedHouseId)
                return false;
            using var connection = persistenceConnections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE expeditions SET residence_house_id=@newHouseId WHERE id=@id AND residence_house_id=@expectedHouseId";
            command.Parameters.AddWithValue("@newHouseId", newHouseId);
            command.Parameters.AddWithValue("@id", expedition.Id);
            command.Parameters.AddWithValue("@expectedHouseId", expectedHouseId);
            if (command.ExecuteNonQuery() != 1)
                return false;
            expedition.ResidenceHouseId = newHouseId;
            expedition.SendDescriptor(worldManager);
            return true;
        }
    }

    private void PersistNewExpedition(Expedition expedition, IReadOnlyCollection<Character> founders,
        Character owner, long creationCost)
    {
        using var connection = persistenceConnections.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var character in founders.DistinctBy(character => character.Id))
            {
                using var claim = connection.CreateCommand();
                claim.Transaction = transaction;
                claim.CommandText = character.Id == owner.Id
                    ? "UPDATE characters SET expedition_id=@expeditionId,expedition_rejoin_until=0,money=money-@creationCost WHERE id=@characterId AND deleted=0 AND expedition_id=0 AND expedition_rejoin_until<=@unixNow AND money>=@creationCost"
                    : "UPDATE characters SET expedition_id=@expeditionId,expedition_rejoin_until=0 WHERE id=@characterId AND deleted=0 AND expedition_id=0 AND expedition_rejoin_until<=@unixNow";
                claim.Parameters.AddWithValue("@expeditionId", (uint)expedition.Id);
                claim.Parameters.AddWithValue("@characterId", character.Id);
                if (character.Id == owner.Id)
                    claim.Parameters.AddWithValue("@creationCost", creationCost);
                claim.Parameters.AddWithValue("@unixNow", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (claim.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Founding character {character.Id} is no longer guildless.");
            }
            expedition.Save(connection, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static ExpeditionMember GetMemberFromCharacter(Expedition expedition, Character character, bool owner)
    {
        var member = new ExpeditionMember
        {
            IsOnline = true,
            Name = character.Name,
            Level = character.Level,
            HeirLevel = character.HeirLevel,
            Role = (byte)(owner ? 255 : 0),
            Memo = "",
            Position = new Vector3(character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z),
            ZoneId = character.Transform.ZoneId,
            FactionId = character.Faction.Id,
            Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3],
            ExpeditionId = expedition.Id,
            CharacterId = character.Id,
            LastWorldLeaveTime = DateTime.UtcNow,
            WeeklyContributionPeriodStart = ServerCalendar.WeekStartMondayUtc
        };

        return member;
    }

    public void SendExpeditions(Character character)
    {
        Expedition[] expeditions;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_expeditionsSync)
            expeditions = _expeditions.Values.ToArray();
        SendExpeditionList(character, expeditions);
        character.SendPacket(new SCExpeditionRolePolicyListPacket([]));
    }

    public FactionsEnum GetExpeditionOfCharacter(uint characterId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_expeditionsSync)
        {
            foreach (var guild in _expeditions.Values)
            {
                lock (guild.SyncRoot)
                    if (guild.GetMember(characterId) != null)
                        return guild.Id;
            }
            return default;
        }
    }
}
