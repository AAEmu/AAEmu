using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Expeditions;
using AAEmu.Game.Utils.DB;

using NLog;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Core.Managers;

public class ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager, IWorldManager worldManager, IChatManager chatManager) : Singleton<ExpeditionManager>, IExpeditionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    //private ExpeditionConfig _config;
    private Regex _nameRegex;

    private Dictionary<FactionsEnum, Expedition> _expeditions;

    /// <summary>
    /// Guild War economy, read from compact.sqlite3's content_configs (kind_id = 25), named through
    /// enum_content_configs - same pattern as AuctionFeeSchedule.
    /// TODO: expedition_war_duration and expedition_war_duration_for_protection use different units
    /// (milliseconds vs seconds) - do not assume they match if adding more duration configs here.
    /// </summary>
    private readonly Dictionary<string, long> _warConfig = [];

    public IEnumerable<Expedition> Expeditions { get => _expeditions.Values; }

    private long WarConfig(string name, long fallback) => _warConfig.GetValueOrDefault(name, fallback);

    /// <summary>Testing override for the war duration in minutes (set via the /gwtime GM command).
    /// 0 = use expedition_war_duration from config (1h on retail).</summary>
    public static int WarDurationTestMinutes { get; set; }

    private void LoadWarEconomyConfig()
    {
        _warConfig.Clear();

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
            _warConfig[reader.GetString("name")] = reader.GetInt32("value");

        Logger.Info($"Loaded {_warConfig.Count} guild war economy values");
    }

    private Expedition Create(string name, Character owner)
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)expeditionIdManager.GetNextId(),
            MotherId = owner.Faction.Id,
            Name = name,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            UnitOwnerType = 0,
            PoliticalSystem = 1,
            Created = DateTime.UtcNow,
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
        _nameRegex = new Regex(AppConfiguration.Instance.Expedition.NameRegex, RegexOptions.Compiled);
        LoadWarEconomyConfig();

        using (var connection = MySQL.CreateConnection())
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
                            Notice = reader.GetString("notice"),
                            ResidenceHouseId = reader.GetUInt32("residence_house_id"),
                            Interest = reader.GetInt16("interest"),
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
                TaskManager.Instance.Schedule(new ExpeditionWarEndTask(expedition.Id), remaining);
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

        lock (member)
        {
            var newTotal = (long)member.ContributionPoint + amount;
            var weeklyDelta = addToWeeklyTotal && amount > 0 ? amount : 0;
            var newWeeklyTotal = (long)member.WeeklyContributionPoint + weeklyDelta;
            if (newTotal is < 0 or > uint.MaxValue || newWeeklyTotal > uint.MaxValue)
                return false;

            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE expedition_members SET contribution_point = @contribution_point, weekly_contribution_point = @weekly_contribution_point WHERE character_id = @character_id AND expedition_id = @expedition_id";
            command.Parameters.AddWithValue("@contribution_point", (uint)newTotal);
            command.Parameters.AddWithValue("@weekly_contribution_point", (uint)newWeeklyTotal);
            command.Parameters.AddWithValue("@character_id", member.CharacterId);
            command.Parameters.AddWithValue("@expedition_id", member.ExpeditionId);
            if (command.ExecuteNonQuery() != 1)
                return false;

            member.ContributionPoint = (uint)newTotal;
            member.WeeklyContributionPoint = (uint)newWeeklyTotal;
        }

        character.SendPacket(new SCAddContributionPointPacket(unchecked((uint)amount), member.ContributionPoint));
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        // The guild overview panel and the prestige-shop affordability check both read the pooled
        // TotalContributionPoint carried on SCExpeditionDescPacket, not the per-member delta above -
        // without this resend they keep showing/checking against a stale (often zero) total.
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));
        return true;
    }

    /// <summary>
    /// Sets the recruitment-board interest bitmask shown as icons in the info panel.
    /// </summary>
    public void SetInterest(Character character, short interest)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        expedition.Interest = interest;
        Save(expedition);
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));
    }

    /// <summary>
    /// Sets the guild-notice text shown in the info panel. Any guild member may set it - no
    /// role-policy flag exists for this specifically, matching the member-gate used for Guild
    /// Residence placement.
    /// </summary>
    public void SetNotice(Character character, string notice)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        expedition.Notice = notice ?? string.Empty;
        Save(expedition);
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));
    }

    /// <summary>
    /// Adds guild exp and auto-advances the guild's level as far as expedition_levels allows without
    /// requiring an item (see ExpeditionLevelGameData). A level gated behind an item stops the auto
    /// climb and waits for TryLevelUp.
    /// </summary>
    public bool AddExp(Expedition expedition, uint amount)
    {
        if (expedition == null || amount == 0)
            return false;

        var gameData = ExpeditionLevelGameData.Instance;
        var newExpLong = (long)expedition.Exp + amount;
        var maxLevelData = gameData.GetLevel(gameData.MaxLevel);
        if (maxLevelData != null && newExpLong > maxLevelData.TotalExp)
            newExpLong = maxLevelData.TotalExp;
        if (newExpLong > uint.MaxValue)
            newExpLong = uint.MaxValue;

        var newExp = (uint)newExpLong;
        if (newExp == expedition.Exp)
            return false;

        expedition.Exp = newExp;
        expedition.Level = gameData.GetAutoLevelForExp(expedition.Level, expedition.Exp);

        expedition.SendPacket(new SCExpeditionExpAddPacket(amount));
        // Always resend the full descriptor, not just on a level change - the client's displayed
        // exp/level can only be trusted to refresh via SCExpeditionDescPacket.
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));

        Save(expedition);
        return true;
    }

    /// <summary>
    /// Handles an explicit CSExpeditionLevelUpPacket - confirms a level gated behind
    /// expedition_levels.require_item_id, consuming the item from the requesting member's own
    /// inventory. Gated on the Expel permission, same "guild management" precedent already used for
    /// Kick/ChangeExpeditionRolePolicy - there's no dedicated policy flag for this yet.
    /// </summary>
    public bool TryLevelUp(Character character)
    {
        var expedition = character.Expedition;
        var member = expedition?.GetMember(character);
        if (member == null)
            return false;

        if (!expedition.GetPolicyByRole(member.Role).Expel)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        if (!ExpeditionLevelGameData.Instance.TryGetLevelUpRequirement(expedition.Level, expedition.Exp, out var requirement))
            return false;

        if (requirement.RequireItemId != 0)
        {
            if (!character.Inventory.CheckItems(SlotType.Inventory, requirement.RequireItemId, requirement.RequireItemAmount))
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                return false;
            }

            var consumed = character.Inventory.Bag.ConsumeItem(
                ItemTaskType.ExpeditionCreation, requirement.RequireItemId, requirement.RequireItemAmount, null);
            if (consumed != requirement.RequireItemAmount)
                return false;
        }

        expedition.Level = requirement.Id;
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));
        Save(expedition);
        return true;
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
        var expedition = character.Expedition;
        if (expedition == null)
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: {0} has no expedition (buffId={1}, targetGrade={2})", character.Name, buffId, targetGrade);
            return false;
        }

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

        if (grade.Contribution > 0 && !TryChangeContributionPoints(character, -grade.Contribution, false))
        {
            Logger.Warn("ExpeditionBuffGrade purchase rejected: buffId={0} grade={1} costs {2} contribution, character {3} could not pay",
                buffId, targetGrade, grade.Contribution, character.Name);
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return false;
        }

        if (grade.ItemId != 0)
        {
            if (!character.Inventory.CheckItems(SlotType.Inventory, grade.ItemId, grade.Count))
            {
                if (grade.Contribution > 0)
                    TryChangeContributionPoints(character, grade.Contribution, false); // refund the point spend above
                character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                return false;
            }
            character.Inventory.Bag.ConsumeItem(ItemTaskType.StoreBuy, grade.ItemId, grade.Count, null);
        }

        expedition.PurchasedBuffGrades[buffId] = targetGrade;
        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "REPLACE INTO expedition_buff_purchases (expedition_id, expedition_buff_id, grade) VALUES (@expedition_id, @buff_id, @grade)";
            command.Parameters.AddWithValue("@expedition_id", expedition.Id);
            command.Parameters.AddWithValue("@buff_id", buffId);
            command.Parameters.AddWithValue("@grade", targetGrade);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        // Order matters: Buffs must go out before Changed, or the client's change-notification handler
        // re-reads its buff cache before this purchase has updated it, showing the old grade.
        expedition.SendPacket(new SCExpeditionBuffsPacket((uint)expedition.Id, expedition.PurchasedBuffGrades));
        expedition.SendPacket(new SCExpeditionBuffChangedPacket((int)expedition.Id, (int)buffId, currentGrade, targetGrade));
        expedition.ApplyBuffBonusesToAllOnline();
        Logger.Info("Expedition buff purchase: {0}'s guild ({1}) bought buff {2} grade {3}", character.Name, expedition.Name, buffId, targetGrade);
        return true;
    }

    /// <summary>Sends the guild's current full prestige-shop buff state to one client - handles CSExpeditionBuffPacket's "view" request and should also fire on Expedition join/login, mirroring SendExpeditionInfo.</summary>
    public void SendExpeditionBuffs(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null)
            return;

        character.SendPacket(new SCExpeditionBuffsPacket((uint)expedition.Id, expedition.PurchasedBuffGrades));
    }

    public Expedition GetExpedition(FactionsEnum id)
    {
        if (_expeditions.TryGetValue(id, out var expedition))
            return expedition;
        return null;
    }

    public void CreateExpedition(string name, GameConnection connection)
    {
        var owner = connection.ActiveChar;
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

        foreach (var exp in _expeditions.Values)
            if (name.Equals(exp.Name))
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameExist);
                return;
            }

        // ----------------- Conditions, can change this...
        var team = teamManager.GetActiveTeamByUnit(owner.Id);
        if (team == null)// || !team.IsParty)
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

        if (validMembers.Count < AppConfiguration.Instance.Expedition.Create.PartyMemberCount)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        if (owner.Money < AppConfiguration.Instance.Expedition.Create.Cost)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
            return;
        }

        owner.Money -= AppConfiguration.Instance.Expedition.Create.Cost;
        owner.SendPacket(
            new SCItemTaskSuccessPacket(
                ItemTaskType.ExpeditionCreation,
                [
                    new MoneyChange(-AppConfiguration.Instance.Expedition.Create.Cost)
                ],
                [])
        );
        // -----------------

        var expedition = Create(name, owner);
        _expeditions.Add(expedition.Id, expedition);

        owner.Expedition = expedition;
        SaveCharacterExpeditionNow(owner);
        WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(owner.ObjId, 0, (int)expedition.Id);

        owner.SendPacket(
            new SCFactionCreatedPacket(expedition, owner.ObjId, [(owner.ObjId, owner.Id, owner.Name)])
        );

        var expeditionsSnapshot = _expeditions.Values.ToArray();
        owner.SendPacket(new SCExpeditionListPacket(expeditionsSnapshot));
        owner.BroadcastPacket(
            new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, (uint)expedition.Id, false),
            true
        );
        // unitId=0 sentinel = "this is about you" - primes the owner's own MyExpeditionId cache right on founding.
        owner.SendPacket(
            new SCUnitExpeditionChangedPacket(0, owner.Id, "", owner.Name, 0, (uint)expedition.Id, false));

        // Every other already-connected character only got the id->name table for guilds that existed at
        // their own login (SendExpeditions, called from CSSelectCharacterPacket). Without this, a guild
        // created mid-session has no name any currently-online client can look up, so its members' nameplate
        // tags render blank until everyone who was already online relogs.
        foreach (var online in worldManager.GetAllCharacters())
        {
            if (online.Id != owner.Id)
                online.SendPacket(new SCExpeditionListPacket(expeditionsSnapshot));
        }

        chatManager.GetGuildChat(expedition).JoinChannel(owner);
        SendExpeditionInfo(owner);
        // owner.Save(); // Moved to SaveMananger

        foreach (var m in validMembers)
        {
            if (m.Character.Id == owner.Id)
                continue;

            var invited = m.Character;
            var newMember = GetMemberFromCharacter(expedition, invited, false);

            invited.Expedition = expedition;
            SaveCharacterExpeditionNow(invited);
            WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(invited.ObjId, 0, (int)expedition.Id);
            expedition.Members.Add(newMember);

            invited.BroadcastPacket(
                new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false),
                true);
            // unitId=0 sentinel = "this is about you" - primes this founding member's own MyExpeditionId cache.
            invited.SendPacket(
                new SCUnitExpeditionChangedPacket(0, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false));
            SendExpeditionInfo(invited);
            expedition.OnCharacterLogin(invited);
        }
        Save(expedition);
    }

    public void Invite(GameConnection connection, string invitedName)
    {
        var inviter = connection.ActiveChar;

        var inviterMember = inviter.Expedition?.GetMember(inviter);
        if (inviterMember == null)
        {
            Logger.Info("Invite: {0} rejected - not a member of any Expedition", inviter.Name);
            return;
        }

        var policy = inviter.Expedition.GetPolicyByRole(inviterMember.Role);
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
        if (invited == null)
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

        Logger.Info("Invite: {0} sending SCExpeditionInvitationPacket to {1} (inviter.Id={2}, expedition={3}/{4})",
            inviter.Name, invited.Name, inviter.Id, (uint)inviter.Expedition.Id, inviter.Expedition.Name);
        invited.SendPacket(
            new SCExpeditionInvitationPacket(inviter.Id, inviter.Name, (uint)inviter.Expedition.Id,
                inviter.Expedition.Name)
        );
    }

    public void ReplyInvite(GameConnection connection, FactionsEnum id1, uint id2, bool reply)
    {
        var invited = connection.ActiveChar;
        if (!reply)
            return;

        var expedition = _expeditions[id1];
        var newMember = GetMemberFromCharacter(expedition, invited, false);

        invited.Expedition = expedition;
        SaveCharacterExpeditionNow(invited);
        WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(invited.ObjId, 0, (int)expedition.Id);
        expedition.Members.Add(newMember);

        invited.BroadcastPacket(
            new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false),
            true);
        // unitId=0 sentinel = "this is about you" - primes the accepting member's own MyExpeditionId cache.
        invited.SendPacket(
            new SCUnitExpeditionChangedPacket(0, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false));
        SendExpeditionInfo(invited);
        expedition.OnCharacterLogin(invited);
        Save(expedition);
    }

    public void ChangeExpeditionRolePolicy(GameConnection connection, ExpeditionRolePolicy policy)
    {
        var expedition = _expeditions[policy.ExpeditionId];

        var characterMember = expedition.GetMember(connection.ActiveChar);
        if (characterMember == null) return;

        if (!expedition.GetPolicyByRole(characterMember.Role).Expel) return;

        var currentPolicy = expedition.GetPolicyByRole(policy.Role);
        currentPolicy.Name = policy.Name;
        currentPolicy.Invite = policy.Invite;
        currentPolicy.JoinSiege = policy.JoinSiege;
        currentPolicy.Promote = policy.Promote;
        currentPolicy.Expel = policy.Expel;

        expedition.SendPacket(new SCExpeditionRolePolicyChangedPacket(policy, true));
        Save(expedition);
    }

    /// <summary>
    /// Removes a character from their Guild
    /// </summary>
    /// <param name="character"></param>
    public static void Leave(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null) return;

        expedition.RemoveMember(expedition.GetMember(character));
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
        SaveCharacterExpeditionNow(character);
        WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(character.ObjId, (int)expedition.Id, 0);
        character.BroadcastPacket(changedPacket, true);
        expedition.SendPacket(changedPacket);
        // unitId=0 sentinel = "this is about you" - clears the leaving character's own MyExpeditionId cache
        // (without this, their client keeps IsExpedInfoLoaded() pinned to the old, now-stale guild id).
        character.SendPacket(new SCUnitExpeditionChangedPacket(0, character.Id, "", character.Name, (uint)expedition.Id, 0, false));
        Save(expedition);
    }

    public void Kick(GameConnection connection, uint kickedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        var characterMember = expedition?.GetMember(character);
        if (characterMember == null || !expedition.GetPolicyByRole(characterMember.Role).Expel)
            return;

        var kicked = expedition.GetMember(kickedId);
        if (kicked == null)
            return;

        expedition.RemoveMember(kicked);

        var kickedChar = worldManager.GetCharacterById(kickedId);

        var changedPacket = new SCUnitExpeditionChangedPacket(kickedChar?.ObjId ?? 0,
            kicked.CharacterId, character.Name, kicked.Name, (uint)expedition.Id, 0, true);

        if (kickedChar is not null)
        {
            kickedChar.Expedition = null;
            SaveCharacterExpeditionNow(kickedChar);
            WorldIntegration.RelayUnitExpeditionChangedToZone?.Invoke(kickedChar.ObjId, (int)expedition.Id, 0);
            kickedChar.BroadcastPacket(changedPacket, true);
            // unitId=0 sentinel = "this is about you" - clears the kicked character's own MyExpeditionId cache.
            kickedChar.SendPacket(new SCUnitExpeditionChangedPacket(0, kicked.CharacterId, character.Name, kicked.Name, (uint)expedition.Id, 0, true));
        }
        expedition.SendPacket(changedPacket);

        Save(expedition);
    }

    public static void ChangeMemberRole(GameConnection connection, byte newRole, uint changedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        var changerMember = expedition?.GetMember(character);
        if (changerMember == null ||
            changerMember.Role <= newRole ||
            !expedition.GetPolicyByRole(changerMember.Role).Promote)
            return;

        var changedMember = expedition.GetMember(changedId);
        if (changedMember == null)
            return;

        changedMember.Role = newRole;
        // Member LIST row refresh uses the status broadcast; RoleChanged announces the identity separately.
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(changedMember, 0));
        expedition.SendPacket(new SCExpeditionRoleChangedPacket(changedMember.CharacterId, changedMember.Name, newRole));
        Save(expedition);
    }

    public static void ChangeOwner(GameConnection connection, uint newOwnerId)
    {
        var owner = connection.ActiveChar;
        var expedition = owner.Expedition;

        var ownerMember = expedition?.GetMember(owner);
        if (ownerMember == null || ownerMember.Role != 255)
            return;

        var newOwnerMember = expedition.GetMember(newOwnerId);
        if (newOwnerMember == null) return;

        newOwnerMember.Role = 255;
        ownerMember.Role = 0;

        expedition.OwnerId = newOwnerId;

        expedition.SendPacket(
            new SCExpeditionOwnerChangedPacket(
                ownerMember.CharacterId,
                newOwnerMember.CharacterId,
                newOwnerMember.Name
            )
        );
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(ownerMember, 0));
        expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(newOwnerMember, 0));
        Save(expedition);
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

        var targetCharacter = WorldManager.Instance.GetCharacterByObjId(targetObjId);
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

        var targetCharacter = WorldManager.Instance.GetCharacterByObjId(targetObjId);
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
        if (money < requiredMoney || !character.SubtractMoney(SlotType.Inventory, requiredMoney))
        {
            character.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
            return;
        }

        var now = DateTime.UtcNow;
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

        targetExpedition.WarEnemyExpeditionId = (uint)expedition.Id;
        targetExpedition.WarDeclaredAt = now;
        targetExpedition.WarEndsAt = endsAt;
        targetExpedition.WarProtectedUntil = null;
        targetExpedition.WarKillScore = 0;
        targetExpedition.WarKillsByMember.Clear();
        targetExpedition.WarIsDeclarer = false;

        Save(expedition);
        Save(targetExpedition);

        // Broadcast server-wide, not just to the two guilds - bystanders get their own "War declared!"
        // banner, same idiom as EndWar's global result broadcast below.
        var endsAtUnix = Helpers.UnixTime(endsAt);
        var declarePacket = new SCExpeditionWarStatePacket((int)expedition.Id, (int)targetExpedition.Id, true, endsAtUnix, false);
        foreach (var onlineCharacter in WorldManager.Instance.GetAllCharacters())
            onlineCharacter.SendPacket(declarePacket);

        // The guild info panel's own "protectDate" field (SCExpeditionDescPacket) reads WarProtectedUntil/
        // WarEndsAt live off the Expedition now, so both guilds' open info windows pick this up on their
        // next refresh without a dedicated push.
        TaskManager.Instance.Schedule(new ExpeditionWarEndTask(expedition.Id), endsAt - now);

        Logger.Info($"Guild War declared: {expedition.Name} ({expedition.Id}) vs {targetExpedition.Name} ({targetExpedition.Id}), ends {endsAt:u}");
    }

    /// <summary>Testing helper (GM /gwend wipe): clears the war/protection state on both guilds outright -
    /// no rewards, no post-war protection - so a fresh war can be declared immediately. Any pending
    /// ExpeditionWarEndTask harmlessly no-ops (EndWar returns early once WarEndsAt is null).</summary>
    public void WipeWar(FactionsEnum expeditionId)
    {
        if (!_expeditions.TryGetValue(expeditionId, out var expedition))
            return;
        _expeditions.TryGetValue((FactionsEnum)expedition.WarEnemyExpeditionId, out var enemyExpedition);

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
            Save(e);
        }

        expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false, 0, true));
        enemyExpedition?.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false, 0, true));
        Logger.Info($"Guild War WIPED (GM): {expedition.Name} vs {enemyExpedition?.Name ?? enemyId.ToString()}");
    }

    /// <summary>GM /endgp: clear a guild's post-war / Ceasefire protection immediately (by name).
    /// Returns the resolved expedition name, or null if no guild by that name.</summary>
    public string EndGuildProtection(string guildName)
    {
        var expedition = _expeditions.Values.FirstOrDefault(e =>
            string.Equals(e.Name, guildName, System.StringComparison.OrdinalIgnoreCase));
        if (expedition == null)
            return null;

        expedition.WarProtectedUntil = null;
        Save(expedition);
        expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, 0, false, 0, true));
        Logger.Info($"Guild protection cleared (GM /endgp): {expedition.Name}");
        return expedition.Name;
    }

    /// <summary>Fires when a war's scheduled duration runs out (see ExpeditionWarEndTask, re-armed on Load()).</summary>
    public void EndWar(FactionsEnum expeditionId)
    {
        if (!_expeditions.TryGetValue(expeditionId, out var expedition) || expedition.WarEndsAt == null)
            return;

        _expeditions.TryGetValue((FactionsEnum)expedition.WarEnemyExpeditionId, out var enemyExpedition);

        var now = DateTime.UtcNow;
        // Post-war cooldown: only the guild that was DECLARED UPON gets it, and it's ~1h on retail
        // (not the 48h that expedition_war_duration_for_protection would give - that value is the
        // Ceasefire item's duration, a different thing). Overridable via /gwtime for testing.
        var protectMinutes = WarDurationTestMinutes > 0 ? WarDurationTestMinutes : 60;
        var protectedUntil = now.AddMinutes(protectMinutes);
        var ourScore = expedition.WarKillScore;
        var theirScore = enemyExpedition?.WarKillScore ?? 0;

        var declarer = expedition.WarIsDeclarer ? expedition : enemyExpedition;
        var defender = expedition.WarIsDeclarer ? enemyExpedition : expedition;

        expedition.WarEndsAt = null;
        expedition.WarProtectedUntil = expedition.WarIsDeclarer ? null : protectedUntil;
        Save(expedition);

        if (enemyExpedition != null)
        {
            enemyExpedition.WarEndsAt = null;
            enemyExpedition.WarProtectedUntil = enemyExpedition.WarIsDeclarer ? null : protectedUntil;
            Save(enemyExpedition);
        }

        AwardWarResult(expedition, ourScore == theirScore ? 0 : ourScore > theirScore ? 1 : -1);
        if (enemyExpedition != null)
            AwardWarResult(enemyExpedition, ourScore == theirScore ? 0 : theirScore > ourScore ? 1 : -1);

        var enemyId = (int)expedition.WarEnemyExpeditionId;
        var defenderUnix = defender != null ? Helpers.UnixTime(protectedUntil) : 0;

        // TODO: order matters here - the terminated war-state packet must go out BEFORE the final
        // kill-score packet, or the client only shows a generic "tied" banner instead of the real result.
        expedition.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false,
            expedition.WarIsDeclarer ? 0 : defenderUnix, true));
        enemyExpedition?.SendPacket(new SCExpeditionWarStatePacket((int)expedition.Id, enemyId, false,
            enemyExpedition.WarIsDeclarer ? 0 : defenderUnix, true));

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

            foreach (var character in WorldManager.Instance.GetAllCharacters())
                character.SendPacket(resultPacket);

            declarer.SendPacket(new SCExpeditionWarKillScorePacket(declarer, defender));
            defender.SendPacket(new SCExpeditionWarKillScorePacket(defender, declarer));
        }

        Logger.Info($"Guild War ended: {declarer?.Name ?? expedition.Name} (declarer, {declarer?.WarKillScore ?? ourScore} kills) vs {defender?.Name ?? enemyExpedition?.Name ?? "?"} (defender, {defender?.WarKillScore ?? theirScore} kills); {defender?.Name ?? "?"} protected {protectMinutes}min");
    }

    /// <summary>outcome: 1 = win, -1 = loss, 0 = draw. Pays every member (online or not) directly against
    /// expedition_members, mirroring TryChangeContributionPoints' SQL - most members will be offline by
    /// the time a weeks-long war concludes.</summary>
    private void AwardWarResult(Expedition expedition, int outcome)
    {
        var reward = (int)WarConfig(outcome switch
        {
            1 => "expedition_war_reward_for_win",
            -1 => "expedition_war_reward_for_lose",
            _ => "expedition_war_reward_for_draw"
        }, 0);
        if (reward <= 0)
            return;

        foreach (var member in expedition.Members.ToArray())
        {
            lock (member)
            {
                var newTotal = (uint)Math.Clamp((long)member.ContributionPoint + reward, 0, uint.MaxValue);

                using (var connection = MySQL.CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE expedition_members SET contribution_point = @contribution_point WHERE character_id = @character_id AND expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@contribution_point", newTotal);
                    command.Parameters.AddWithValue("@character_id", member.CharacterId);
                    command.Parameters.AddWithValue("@expedition_id", member.ExpeditionId);
                    command.ExecuteNonQuery();
                }

                member.ContributionPoint = newTotal;
            }

            var character = WorldManager.Instance.GetCharacterById(member.CharacterId);
            if (character == null)
                continue;

            character.SendPacket(new SCAddContributionPointPacket(unchecked((uint)reward), member.ContributionPoint));
            expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        }

        expedition.SendPacket(new SCExpeditionDescPacket(expedition));
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

        if (!killerExpedition.IsAtWar || killerExpedition.WarEnemyExpeditionId != (uint)victimExpedition.Id)
        {
            Logger.Debug($"RegisterWarKill: {killer.Name} killed {victim.Name} but no active war between {killerExpedition.Name} and {victimExpedition.Name} (IsAtWar={killerExpedition.IsAtWar}, enemyId={killerExpedition.WarEnemyExpeditionId})");
            return;
        }

        killerExpedition.WarKillScore++;
        killerExpedition.WarKillsByMember.TryGetValue(killer.Id, out var memberKills);
        killerExpedition.WarKillsByMember[killer.Id] = memberKills + 1;
        Save(killerExpedition);
        Logger.Info($"Guild War kill: {killer.Name} ({killerExpedition.Name}) killed {victim.Name} ({victimExpedition.Name}) - score now {killerExpedition.WarKillScore} (this member: {memberKills + 1})");

        // Push the updated scoreboard to both guilds so open scoreboards update without waiting for
        // the client's next poll.
        killerExpedition.SendPacket(new SCExpeditionWarKillScorePacket(killerExpedition, victimExpedition));
        victimExpedition.SendPacket(new SCExpeditionWarKillScorePacket(victimExpedition, killerExpedition));
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

        expedition.WarProtectedUntil = DateTime.UtcNow.AddSeconds(durationSeconds);
        Save(expedition);
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));

        Logger.Info($"Guild War protection: {expedition.Name} protected until {expedition.WarProtectedUntil:u} (via Ceasefire Agreement, used by {character.Name})");
    }

    /// <summary>Backs the guild info panel's "cancel protection" button - CSCancelExpeditionProtectionPacket
    /// was a fully-parsed (empty-body) no-op stub. Owner-only, matching that button's own visibility gate.</summary>
    public void CancelProtection(Character character)
    {
        var expedition = character.Expedition;
        var ownerMember = expedition?.GetMember(character);
        if (ownerMember == null || ownerMember.Role != 255 || !expedition.IsProtected)
            return;

        expedition.WarProtectedUntil = null;
        Save(expedition);
        expedition.SendPacket(new SCExpeditionDescPacket(expedition));

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
        for (var i = guild.Members.Count - 1; i >= 0; i--)
        {
            var c = worldManager.GetCharacterById(guild.Members[i].CharacterId);
            if (c != null)
            {
                if (c.IsOnline)
                    c.SendPacket(new SCExpeditionDismissedPacket((uint)guild.Id, true));
                c.Expedition = null;
                SaveCharacterExpeditionNow(c);
            }
            guild.RemoveMember(guild.Members[i]);
        }
        guild.Name = "$deleted-guild-" + guild.Id;
        guild.OwnerId = 0;
        guild.isDisbanded = true;
        Save(guild);
        return true;
    }

    public static void SendExpeditionInfo(Character character)
    {
        var members = character.Expedition.Members;
        var total = (uint)members.Count;
        var id = character.Expedition.Id;

        Logger.Info("SendExpeditionInfo: {0} -> guild {1} ({2}), {3} members, {4} policies, totalContribution={5} (per-member: {6})",
            character.Name, character.Expedition.Name, id, total, character.Expedition.Policies.Count,
            character.Expedition.TotalContributionPoint,
            string.Join(",", members.Select(m => $"{m.Name}={m.ContributionPoint}")));

        // TODO: send order matters - desc must go out before RolePolicyList/MemberList, or the client's
        // level-up button and role permissions latch against stale cached values.
        character.SendPacket(new SCExpeditionDescPacket(character.Expedition));
        character.SendPacket(new SCExpeditionRolePolicyListPacket(character.Expedition.Policies));

        for (var i = 0; i < members.Count; i += 20)
        {
            var block = members.Skip(i).Take(20).ToList();
            character.SendPacket(new SCExpeditionMemberListPacket((uint)id, block));
        }

        character.SendPacket(new SCExpeditionMemberListEndPacket((int)total, (int)id));

        // Guild War: a client loses X2Faction's war/protection state on disconnect, so a (re)connecting
        // member would see the enemy guild as friendly (green) and be unable to target them. Re-push it
        // here, the same shape DeclareWar/EndWar broadcast - see also ExpeditionWarEndTask re-arm on Load().
        var exp = character.Expedition;
        if (exp.IsAtWar || exp.IsProtected)
        {
            var until = Helpers.UnixTime(exp.WarEndsAt ?? exp.WarProtectedUntil ?? DateTime.UtcNow);
            character.SendPacket(new SCExpeditionWarStatePacket((int)id, (int)exp.WarEnemyExpeditionId, exp.IsAtWar, until, false));
        }
    }

    public static void Save(Expedition expedition)
    {
        using (var connection = MySQL.CreateConnection())
        using (var transaction = connection.BeginTransaction())
        {
            expedition.Save(connection, transaction);
            transaction.Commit();
        }
    }

    /// <summary>
    /// Persists a character's `expedition_id` right away instead of waiting for the periodic
    /// SaveManager tick (default 5 minutes) or a graceful shutdown. Guild join/leave/kick/create/disband
    /// must call this - a non-graceful World stop (crash, Stop-Process, etc.) inside that window would
    /// otherwise leave `expeditions`/`expedition_members` correctly saved (those already save
    /// synchronously) while the character's own `expedition_id` column reverts to whatever it was at
    /// last autosave, desyncing `Character.Expedition` from the real membership on next boot.
    /// </summary>
    private static void SaveCharacterExpeditionNow(Character character)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        character.Save(connection, transaction);
        transaction.Commit();
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
            LastWorldLeaveTime = DateTime.UtcNow
        };

        return member;
    }

    public void SendExpeditions(Character character)
    {
        var expeditions = _expeditions.Values.ToArray();
        character.SendPacket(new SCExpeditionListPacket(expeditions));
        character.SendPacket(new SCExpeditionRolePolicyListPacket([]));
    }

    public FactionsEnum GetExpeditionOfCharacter(uint characterId)
    {
        return (from guild in _expeditions.Values from member in guild.Members where member.CharacterId == characterId select guild.Id).FirstOrDefault();
    }
}
