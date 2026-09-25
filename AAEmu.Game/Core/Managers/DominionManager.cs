using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Dominions;
using AAEmu.Game.Models.Game.Heroes;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Dominions;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Live territory (Dominion) claims of the Hero/faction castle system: who owns which zone group, the tax
/// pool, and the weekly siege period. <c>siege_zones</c> / <c>guard_tower_settings</c> /
/// <c>guard_tower_steps</c> are the templates; the save state lives in the <c>dominions</c> table.
/// </summary>
/// <remarks>
/// The claim itself is the lodestone House of the zone group: declaring completes its single build step
/// (the buried lodestone rises), applies <c>guard_tower_settings.initial_buff_id</c>, and spawns the
/// territory agent (<c>siege_zones.dominion_merchant_id</c>). The Zone process keeps no state of its own,
/// so the claim and the agent are re-announced whenever a zone (re)loads.
///
/// <c>guard_tower_steps</c> is a cap/buff table, not a spawn list. Walls and gates are player drawings
/// (skill 41079 + <c>item_housings</c>), not World-authored from that table.
/// </remarks>
public class DominionManager(ITaskManager taskManager, IExpeditionManager expeditionManager, IGameDataManager gameDataManager, IGuildDominionManager guildDominionManager) : Singleton<DominionManager>, IDominionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Ordering-only dependency: SiegeGameData must be loaded before Load() builds TerritoryData.
    private readonly IGameDataManager _gameDataManager = gameDataManager;

    private Dictionary<ushort, DominionData> _dominions = [];
    private readonly IGuildDominionManager _guildDominionManager = guildDominionManager;
    private Dictionary<ushort, uint> _guardTowerSettingIdByZone = [];

    /// <summary>The territory agent NPC per claimed zone group, so a zone reload re-announces the same NPC instead of spawning another.</summary>
    private readonly Dictionary<ushort, Npc> _territoryAgentByZone = [];

    public IEnumerable<DominionData> Dominions => _dominions.Values;

    public DominionData GetByZoneId(ushort zoneId) => _dominions.GetValueOrDefault(zoneId);

    /// <summary>
    /// Seeded Guard Tower in this zone group, if one is spawned. Zone group comes from the house cell,
    /// not a compiled zone→template table.
    /// </summary>
    public static House FindLodestoneInZoneGroup(ushort zoneGroupId) =>
        DominionClaimRules.FindLodestoneInZone(
            HousingManager.Instance.GetAllHouses(),
            zoneGroupId,
            ZoneGroupOf,
            house => SiegeGameData.Instance.IsLodestoneTemplate(house.TemplateId));

    internal static ushort ZoneGroupOf(House house)
    {
        if (house?.Transform == null)
            return 0;
        return (ushort)(ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId ?? 0);
    }

    /// <summary>Current <c>siege_zones</c> / <c>siege_plans</c> period, or Peace when the zone has no schedule.</summary>
    internal static byte ScheduledSiegePeriod(ushort zoneId, DateTime now) =>
        (byte)SiegeManager.Instance.GetScheduledPeriod(zoneId, now);

    private uint ResolveTaxReceiverId(DominionData dominion)
    {
        if (dominion.OwningFactionId != 0)
            return HeroManager.Instance.TopSeatedCharacterId(dominion.OwningFactionId);

        if (dominion.ExpeditionId == 0)
            return 0;

        var expedition = expeditionManager.Expeditions.FirstOrDefault(e => (uint)e.Id == dominion.ExpeditionId);
        return expedition?.OwnerId ?? 0;
    }

    public DominionData GetDominionAtPosition(ushort zoneId, float x, float y)
    {
        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return null;

        var dx = x - dominion.X;
        var dy = y - dominion.Y;
        var radius = dominion.TerritoryData?.RadiusDominion ?? 0;
        return dx * dx + dy * dy <= (float)radius * radius ? dominion : null;
    }

    /// <summary>
    /// Loads the claims. Runs after GameDataManager (constructor dependency) because TerritoryData is built
    /// from guard_tower_settings; the client draws the map circle only when all four radii are non-zero.
    /// </summary>
    public void Load()
    {
        _dominions = [];
        _guardTowerSettingIdByZone = [];

        // These are W08B tax-pool prerequisites. A missing bound/limit is a content error, not a
        // reason to silently pay the whole pool or to accept an unpriced tax rate.
        HeroContentConfig.RequireDominionTaxBounds(out _, out _);
        HeroContentConfig.RequireDominionTaxLimit();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM dominions";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var zoneId = (ushort)reader.GetInt32(reader.GetOrdinal("zone_id"));
            var guardTowerSettingId = (uint)reader.GetInt32(reader.GetOrdinal("guard_tower_setting_id"));

            var expeditionId = (uint)reader.GetInt32(reader.GetOrdinal("expedition_id"));
            var owningFactionId = (uint)reader.GetInt32(reader.GetOrdinal("faction_id"));

            var dominion = new DominionData
            {
                ZoneId = zoneId,
                ExpeditionId = expeditionId,
                OwningFactionId = owningFactionId,
                // Faction-owned rows (owningFactionId != 0) already carry the real alliance id directly - no
                // guild-race derivation needed or wanted. Guild-owned rows (54/56) keep the existing derivation.
                FactionId = owningFactionId != 0 ? (FactionsEnum)owningFactionId : ResolveOwningFaction(expeditionId),
                House = (uint)reader.GetInt32(reader.GetOrdinal("house")),
                TaxRate = reader.GetInt32(reader.GetOrdinal("tax_rate")),
                X = reader.GetFloat(reader.GetOrdinal("x")),
                Y = reader.GetFloat(reader.GetOrdinal("y")),
                Z = reader.GetFloat(reader.GetOrdinal("z")),
                CurHouseTaxMoney = reader.GetInt32(reader.GetOrdinal("cur_house_tax_money")),
                CurHuntTaxMoney = reader.GetInt32(reader.GetOrdinal("cur_hunt_tax_money")),
                PeaceTaxMoney = reader.GetInt32(reader.GetOrdinal("peace_tax_money")),
                CurHouseTaxAaPoint = reader.GetInt32(reader.GetOrdinal("cur_house_tax_aa_point")),
                PeaceTaxAaPoint = reader.GetInt32(reader.GetOrdinal("peace_tax_aa_point")),
                LastPaidTime = reader.GetDateTime(reader.GetOrdinal("last_paid_time")),
                LastSiegeEndTime = reader.GetDateTime(reader.GetOrdinal("last_siege_end_time")),
                ReignStartTime = reader.GetDateTime(reader.GetOrdinal("reign_start_time")),
                LastTaxRateChangedTime = reader.GetDateTime(reader.GetOrdinal("last_tax_rate_changed_time")),
                ObjId = 0,
                TerritoryData = BuildTerritoryData(guardTowerSettingId),
                SiegeTimers = new DominionSiegeTimers
                {
                    Durations = [0, 0, 0, 0, 0],
                    Started = DateTime.MinValue,
                    Fixed = DateTime.MinValue,
                    Bdm = 0,
                    SiegePeriod = (byte)reader.GetInt32(reader.GetOrdinal("siege_period")),
                    UnkData = EmptyUnkData(),
                    Unk2Data = EmptyUnkData()
                },
                NonPvPStart = reader.GetDateTime(reader.GetOrdinal("non_pvp_start")),
                NonPvPDuration = (ushort)reader.GetInt32(reader.GetOrdinal("non_pvp_duration"))
            };

            _dominions[zoneId] = dominion;
            _guardTowerSettingIdByZone[zoneId] = guardTowerSettingId;
        }

        Logger.Info("Loaded {0} dominions", _dominions.Count);

        taskManager.Schedule(new DominionTaxPayoutTask(), TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Guild-claim path (Exeloch/Sungold Fields, zone groups 54/56). <paramref name="expeditionId"/> is the
    /// real ownership; FactionId on the resulting DominionData is only derived for the wire's display purposes.
    /// </summary>
    public DominionData Declare(ushort zoneId, uint expeditionId, House lodestone, Character declarer) =>
        Declare(zoneId, expeditionId, 0, lodestone, declarer);

    /// <summary>
    /// Hero/faction-claim path for a zone group that has a <c>siege_zones</c> row. Ownership is the
    /// declaring Hero's alliance. The caller must already have checked that they hold the seat.
    /// </summary>
    public DominionData DeclareForFaction(ushort zoneId, uint owningFactionId, House lodestone, Character declarer) =>
        Declare(zoneId, 0, owningFactionId, lodestone, declarer);

    private DominionData Declare(ushort zoneId, uint expeditionId, uint owningFactionId, House lodestone, Character declarer)
    {
        if (_dominions.ContainsKey(zoneId))
        {
            declarer?.SendErrorMessage(ErrorMessageType.DominionAlreadyDedclared);
            return null;
        }

        var guardTowerSettingId = lodestone.Template?.GuardTowerSettingId ?? 0;
        var now = DateTime.UtcNow;

        var dominion = new DominionData
        {
            ZoneId = zoneId,
            ExpeditionId = expeditionId,
            OwningFactionId = owningFactionId,
            FactionId = owningFactionId != 0
                ? (FactionsEnum)owningFactionId
                : (ResolveOwningFaction(declarer) is var declarerFaction && declarerFaction != FactionsEnum.Invalid
                    ? declarerFaction
                    : ResolveOwningFaction(expeditionId)),
            House = lodestone.Id,
            TaxRate = HeroContentConfig.InitialDominionTaxRate(),
            X = lodestone.Transform.World.Position.X,
            Y = lodestone.Transform.World.Position.Y,
            Z = lodestone.Transform.World.Position.Z,
            CurHouseTaxMoney = 0,
            CurHuntTaxMoney = 0,
            PeaceTaxMoney = 0,
            CurHouseTaxAaPoint = 0,
            PeaceTaxAaPoint = 0,
            LastPaidTime = now,
            LastSiegeEndTime = now,
            ReignStartTime = now,
            LastTaxRateChangedTime = now,
            ObjId = 0,
            TerritoryData = BuildTerritoryData(guardTowerSettingId),
            SiegeTimers = new DominionSiegeTimers
            {
                Bdm = 0,
                Durations = [0, 0, 0, 0, 0],
                Fixed = DateTime.MinValue,
                Started = DateTime.MinValue,
                SiegePeriod = ScheduledSiegePeriod(zoneId, now),
                UnkData = EmptyUnkData(),
                Unk2Data = EmptyUnkData()
            },
            NonPvPDuration = 0,
            NonPvPStart = now
        };

        _dominions[zoneId] = dominion;
        _guardTowerSettingIdByZone[zoneId] = guardTowerSettingId;
        Insert(dominion, guardTowerSettingId);
        NotifyZoneDominionClaimed(dominion, lodestone.Transform.ZoneId);

        // Guild-facing ownership on the House itself: House.cs:293 treats AccountId<=0 || OwnerId<=0 as "no
        // owner" for display purposes (the seeded lodestone rows start at 0/0). Actual game-logic ownership is
        // Expedition-based via dominion.ExpeditionId above; OwnerId here just needs to point at a current
        // member so the existing Expedition-based AllowedToInteract() check keeps working, matching how other
        // Expedition-gated housing in this codebase is already modeled (see HousingManager.Build's Expedition
        // gate). Known limitation, not fixed here: if this specific character later leaves the guild, this
        // field goes stale until someone re-declares - no ownership-transfer-on-leave exists yet.
        if (declarer != null)
        {
            lodestone.OwnerId = declarer.Id;
            lodestone.CoOwnerId = declarer.Id;
            lodestone.AccountId = declarer.AccountId;
            // Without this, only a fresh zone-enter (which re-sends full House state) picks up the new
            // owner - anyone already observing the lodestone at claim time keeps seeing the stale 0/0
            // "no owner" state until they leave and re-enter. Matches the live-refresh push
            // HousingManager.Sell already does on ownership change (SCHouseDataPacket to observers).
            lodestone.BroadcastPacket(new SCHouseDataPacket([lodestone]), true);
            // SCHouseDataPacket alone updates only the World->Client channel. This architecture's real-time
            // interaction/tooltip queries are answered by the native Zone process against its OWN cached
            // House state (Zone is sim authority - see aaemu-server-overview memory), which never gets told
            // about this change unless we also push it over World->Zone, same as HousingManager.
            // CreateDominionHouse already does for a brand-new House (NotifyZoneHouseCreated right after its
            // own SCHouseDataPacket send). First attempt at this fix only did the SC half - still needed a
            // zone re-entry to show the new owner, exactly because this half was missing.
            if (WorldIntegration.ZoneAuthority)
                HousingZoneBridge.NotifyZoneHouseCreated(lodestone);
        }

        // The risen tower is the lodestone's housing_build_steps completion, not a buff swap. Every
        // guard-tower template has one build step (the buried model) whose skill_id is the declare skill
        // itself, so completing that step here is the intended transition: CurrentStep -1, ModelId to
        // Template.MainModelId, bound doodads created (same as House.CurrentStep's setter).
        if (lodestone.CurrentStep != -1)
        {
            lodestone.AddBuildAction();
            lodestone.BroadcastPacket(
                new SCHouseBuildProgressPacket(
                    lodestone.TlId,
                    lodestone.ModelId,
                    lodestone.AllAction,
                    lodestone.CurrentStep == -1 ? lodestone.AllAction : lodestone.CurrentAction
                ),
                true
            );
            HousingZoneBridge.NotifyZoneHouseBuildState(lodestone);

            if (lodestone.CurrentStep == -1)
            {
                foreach (var doodad in lodestone.AttachedDoodads.ToArray())
                    doodad.Spawn();
            }
        }

        // guard_tower_settings.initial_buff_id marks the claim on the tower. The visual comes from the
        // build step above; the buff does not drive House.ModelId.
        if (dominion.TerritoryData?.Id2 is { } initialBuffId and > 0)
            lodestone.Buffs.AddBuff(initialBuffId, lodestone);

        // Territory agent (siege_zones.dominion_merchant_id). Its npc_spawners rows have no level-file
        // placement, so the zone never asks for it; the claim is the trigger and World authors it.
        // Shared with RelayAllToZone so a zone reconnect re-announces the same NPC.
        EnsureTerritoryAgentNpc(dominion, lodestone);

        // Declare notices are plot 2 / world_message_effects on the declare skill. Do not add a second string.
        WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(dominion, true, true));

        // House fields set above only mark the House dirty; House.Save() otherwise waits for the periodic
        // tick or a graceful shutdown. The dominions row is inserted immediately, so a hard restart in that
        // window would leave a claim whose lodestone reverted to the seeded row. Save now.
        SaveLodestoneNow(lodestone);
        return dominion;
    }

    private static void SaveLodestoneNow(House lodestone)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        lodestone.Save(connection, transaction);
        transaction.Commit();
    }

    public void UpdateTaxRate(GameConnection connection, ushort zoneId, int taxRate)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return;

        if (!HeroContentConfig.TryGetDominionTaxBounds(out var taxMin, out var taxMax)
            || !DominionClaimRules.IsTaxRateAllowed(taxRate, taxMin, taxMax))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        var hasPermission = dominion.OwningFactionId != 0
            ? ResolveOwningFaction(character) == (FactionsEnum)dominion.OwningFactionId && HeroManager.Instance.IsCurrentHero(character)
            : character.Expedition != null && (uint)character.Expedition.Id == dominion.ExpeditionId;
        if (!hasPermission)
        {
            character.SendErrorMessage(ErrorMessageType.NoPerm);
            return;
        }

        dominion.TaxRate = taxRate;
        dominion.LastTaxRateChangedTime = DateTime.UtcNow;

        using (var mysqlConnection = MySQL.CreateConnection())
        using (var command = mysqlConnection.CreateCommand())
        {
            command.CommandText = "UPDATE dominions SET tax_rate = @taxRate, last_tax_rate_changed_time = @changed WHERE zone_id = @zoneId";
            command.Parameters.AddWithValue("@taxRate", dominion.TaxRate);
            command.Parameters.AddWithValue("@changed", dominion.LastTaxRateChangedTime);
            command.Parameters.AddWithValue("@zoneId", zoneId);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        WorldManager.Instance.BroadcastPacketToServer(new SCDominionTaxRatePacket(zoneId, dominion.TaxRate));
    }

    public void PayoutTax()
    {
        var now = DateTime.UtcNow;
        foreach (var dominion in _dominions.Values)
        {
            var weekStart = SiegeGameData.Instance.GetCurrentCycleWeekStart(dominion.ZoneId, now);
            if (!DominionClaimRules.ShouldPayOnSiegeWeek(dominion.LastPaidTime, weekStart))
                continue;

            var total = dominion.CurHouseTaxMoney + dominion.CurHuntTaxMoney + dominion.PeaceTaxMoney;
            var limit = HeroContentConfig.RequireDominionTaxLimit();
            var payable = (int)DominionClaimRules.CapTax(total, limit);
            if (payable <= 0)
            {
                dominion.LastPaidTime = now;
                PersistTaxPool(dominion);
                continue;
            }

            var receiverId = ResolveTaxReceiverId(dominion);
            var receiverName = receiverId != 0 ? NameManager.Instance.GetCharacterName(receiverId) : null;
            if (receiverName == null)
            {
                Logger.Warn("DominionManager.PayoutTax: zone {0} tax receiver not found, leaving {1} in the pool", dominion.ZoneId, total);
                continue;
            }

            var mail = new BaseMail
            {
                MailType = MailType.NationTaxReceipt,
                Title = string.Empty,
                ReceiverName = receiverName
            };
            mail.Header.SenderName = ".nationTax";
            mail.Header.ReceiverId = receiverId;
            mail.Header.Status = MailStatus.Unread;
            mail.Body.Text = string.Empty;
            mail.Body.CopperCoins = payable;
            mail.Body.RecvDate = now;

            var beforeSend = new DominionClaimRules.TaxPool(
                dominion.CurHouseTaxMoney, dominion.CurHuntTaxMoney, dominion.PeaceTaxMoney, dominion.LastPaidTime);
            var settled = DominionClaimRules.SettleTaxPool(beforeSend, payable, now);
            ApplyTaxPool(dominion, settled);

            using (var persist = MailManager.Instance.DeferPersist())
            using (var connection = MySQL.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    PersistTaxPool(dominion, connection, transaction);
                    if (!MailManager.Instance.TryDeliverOn(mail, connection, transaction))
                    {
                        transaction.Rollback();
                        ApplyTaxPool(dominion, beforeSend);
                        Logger.Warn("Dominion tax payout: zone {0} mail failed, left pool {1}", dominion.ZoneId, payable);
                        continue;
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    ApplyTaxPool(dominion, beforeSend);
                    MailManager.Instance.DiscardUnpersisted(mail);
                    Logger.Error(ex, "Dominion tax payout: zone {0} persist failed", dominion.ZoneId);
                    continue;
                }

                MailManager.Instance.PublishDelivered(mail);
            }

            Logger.Info("Dominion tax payout: zone {0}, {1} copper to {2}", dominion.ZoneId, payable, receiverName);
        }
    }

    private static void ApplyTaxPool(DominionData dominion, DominionClaimRules.TaxPool pool)
    {
        dominion.CurHouseTaxMoney = pool.House;
        dominion.CurHuntTaxMoney = pool.Hunt;
        dominion.PeaceTaxMoney = pool.Peace;
        dominion.LastPaidTime = pool.LastPaid;
    }

    private void PersistTaxPool(DominionData dominion)
    {
        using var connection = MySQL.CreateConnection();
        PersistTaxPool(dominion, connection, transaction: null);
    }

    private static void PersistTaxPool(DominionData dominion, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dominions
            SET cur_house_tax_money = @house, cur_hunt_tax_money = @hunt, peace_tax_money = @peace, last_paid_time = @paid
            WHERE zone_id = @zoneId
            """;
        command.Parameters.AddWithValue("@house", dominion.CurHouseTaxMoney);
        command.Parameters.AddWithValue("@hunt", dominion.CurHuntTaxMoney);
        command.Parameters.AddWithValue("@peace", dominion.PeaceTaxMoney);
        command.Parameters.AddWithValue("@paid", dominion.LastPaidTime);
        command.Parameters.AddWithValue("@zoneId", dominion.ZoneId);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    private const int DominionZonePaddingBytes = DominionData.RequiredPaddingBytes;

    internal static void NotifyZoneDominionClaimed(DominionData dominion, uint rawZoneId, int diagnosticPaddingBytes = DominionZonePaddingBytes)
    {
        if (!WorldIntegration.ZoneAuthority || rawZoneId == 0)
            return;

        WorldIntegration.RelayDominionClaimedToZone?.Invoke(rawZoneId, dominion, diagnosticPaddingBytes);
    }

    internal static void NotifyZoneDominionDeleted(ushort zoneGroupId, uint rawZoneId)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        WorldIntegration.RelayDominionDeletedToZone?.Invoke(rawZoneId, zoneGroupId);
    }

    /// <summary>
    /// Zone keeps no claim or agent of its own. Re-announce both when a dedicate (re)joins, same hook as
    /// housing <c>RelayAllToZone</c>.
    /// </summary>
    public void RelayAllToZone(uint rawZoneId)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        foreach (var dominion in _dominions.Values)
        {
            var house = HousingManager.Instance.GetHouseById(dominion.House);
            if (house?.Transform == null || house.Transform.ZoneId != rawZoneId)
                continue;

            NotifyZoneDominionClaimed(dominion, rawZoneId);
            EnsureTerritoryAgentNpc(dominion, house);
        }
    }

    /// <summary>
    /// Spawns the Territory Agent NPC for a claimed Dominion, or - if one already exists for this zone group
    /// (tracked in `_territoryAgentByZone`) - just re-announces the SAME existing World-side object to Zone
    /// instead of creating a second one. This distinction matters specifically because `RelayAllToZone` can now
    /// call this repeatedly across many Zone reloads over a single World session (see that method's doc
    /// comment) - blindly calling `NpcManager.Create()` every time would spawn a new duplicate NPC on top of
    /// the previous one(s) each time Zone reconnects, since the World-side NPC object itself survives Zone
    /// reloads even though Zone's own awareness of it doesn't.
    /// </summary>
    private void EnsureTerritoryAgentNpc(DominionData dominion, House lodestone)
    {
        if (_territoryAgentByZone.TryGetValue(dominion.ZoneId, out var existing) && existing != null)
        {
            if (!WorldIntegration.PublishNpcSpawn(existing))
                WorldIntegration.DeleteNpcMirror(existing, false);
            return;
        }

        var merchantNpcId = SiegeGameData.Instance.GetSiegeZoneSchedule(dominion.ZoneId)?.DominionMerchantId ?? 0;
        if (merchantNpcId <= 0 || !NpcManager.Instance.Exist(merchantNpcId) || lodestone.ParentWorld == null)
            return;

        var merchant = NpcManager.Instance.Create(lodestone.ParentWorld, 0, merchantNpcId);
        if (merchant == null)
            return;

        var origin = lodestone.Transform.World.Position;
        var zoneId = lodestone.Transform.ZoneId;
        var local = ZoneManager.Instance.ConvertToLocalCoordinates(zoneId, origin);
        var pickedStand = TerritoryAgentPlacement.TryPickStand(
            local.X,
            local.Y,
            WorldIntegration.ListZoneSpawnerPlacements(zoneId),
            NpcGameData.Instance.GetSpawnerIds(merchantNpcId),
            out var stand);

        float offsetX;
        float offsetY;
        float probeZ;
        float yaw;
        if (pickedStand)
        {
            var worldStand = ZoneManager.Instance.ConvertToWorldCoordinates(
                zoneId, new Vector3(stand.X, stand.Y, stand.Z));
            offsetX = worldStand.X;
            offsetY = worldStand.Y;
            probeZ = worldStand.Z;
            yaw = stand.ZRot;
            Logger.Info(
                "Territory agent npc={0} zone={1} stand type={2} local=({3:0.##},{4:0.##},{5:0.##})",
                merchantNpcId, zoneId, stand.SpawnerType, stand.X, stand.Y, stand.Z);
        }
        else
        {
            offsetX = origin.X + (lodestone.Template?.AutoZOffsetX ?? 0f);
            offsetY = origin.Y + (lodestone.Template?.AutoZOffsetY ?? 0f);
            probeZ = origin.Z + (lodestone.Template?.AutoZOffsetZ ?? 0f);
            yaw = lodestone.Transform.World.ToRollPitchYawDegrees().Z;
            Logger.Warn(
                "Territory agent npc={0} zone={1} — no npc_spawners.g stand within {2}m; using lodestone origin",
                merchantNpcId, zoneId, TerritoryAgentPlacement.MaxStandDistanceMetres);
        }

        var probe = new Vector3(offsetX, offsetY, probeZ);
        var ground = TerrainFloor.SampleHeightmap(lodestone.ParentWorld, offsetX, offsetY);
        var overWater = TerrainFloor.TryWaterSurface(lodestone.ParentWorld, probe, out var waterZ);
        var offsetZ = TerrainFloor.ChooseUnitFloorZ(probeZ, ground, overWater, waterZ);

        merchant.Transform = lodestone.Transform.CloneDetached(merchant);
        merchant.SetPosition(offsetX, offsetY, offsetZ, 0f, 0f, yaw);
        merchant.IsZoneMirror = true;
        merchant.Spawn();
        if (!WorldIntegration.PublishNpcSpawn(merchant))
            WorldIntegration.DeleteNpcMirror(merchant, false);

        _territoryAgentByZone[dominion.ZoneId] = merchant;
    }

    /// <summary>
    /// GM catch-up: re-sends WZDominionData to the zone and a fresh SCDominionDataPacket to every connected
    /// client for an already-claimed zone group, without an unclaim/reclaim. Both sides are refreshed so a
    /// client holding stale map data does not need a relog or zone re-enter.
    /// </summary>
    public bool ResyncZone(ushort zoneId)
    {
        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return false;

        var house = HousingManager.Instance.GetHouseById(dominion.House);
        if (house?.Transform == null)
            return false;

        WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(dominion, true, true));

        NotifyZoneDominionClaimed(dominion, house.Transform.ZoneId);
        return true;
    }

    public bool ResyncZoneWithZeroedTestData(ushort zoneId, int diagnosticPaddingBytes = 0)
    {
        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return false;

        var house = HousingManager.Instance.GetHouseById(dominion.House);
        if (house?.Transform == null)
            return false;

        var zeroed = new DominionData
        {
            ZoneId = dominion.ZoneId,
            ExpeditionId = dominion.ExpeditionId,
            House = 0,
            TaxRate = 0,
            X = 0,
            Y = 0,
            Z = 0,
            CurHouseTaxMoney = 0,
            CurHuntTaxMoney = 0,
            PeaceTaxMoney = 0,
            CurHouseTaxAaPoint = 0,
            PeaceTaxAaPoint = 0,
            LastPaidTime = DateTime.MinValue,
            LastSiegeEndTime = DateTime.MinValue,
            ReignStartTime = DateTime.MinValue,
            LastTaxRateChangedTime = DateTime.MinValue,
            ObjId = 0,
            TerritoryData = new DominionTerritoryData(), // real (non-null) instance so the wire structure stays the same shape, just zero-valued
            SiegeTimers = new DominionSiegeTimers { SiegePeriod = 0 },
            NonPvPStart = DateTime.MinValue,
            NonPvPDuration = 0
        };

        NotifyZoneDominionClaimed(zeroed, house.Transform.ZoneId, diagnosticPaddingBytes);
        return true;
    }

    /// <summary>
    /// Drops a claim and resets the lodestone. Zone is sent <c>WZDominionDeleted</c> so it does not keep
    /// the old claim until a reload.
    /// </summary>
    public bool UnclaimTerritory(ushort zoneId)
    {
        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return false;

        var house = HousingManager.Instance.GetHouseById(dominion.House);
        var rawZoneId = house?.Transform?.ZoneId ?? 0;
        if (house != null)
        {
            if (_guardTowerSettingIdByZone.TryGetValue(zoneId, out var guardTowerSettingId))
            {
                foreach (var stepRow in SiegeGameData.Instance.GetGuardTowerSteps(guardTowerSettingId))
                {
                    if (stepRow.BuffId != 0)
                        house.Buffs.RemoveBuff(stepRow.BuffId, false);
                }
            }

            if (dominion.TerritoryData?.Id2 is { } initialBuffId and > 0)
                house.Buffs.RemoveBuff(initialBuffId, false);

            house.OwnerId = 0;
            house.CoOwnerId = 0;
            house.AccountId = 0;
            house.NumAction = 0;
            // Setter cleans up AttachedDoodads and swaps ModelId back to the buried step-0 model - same
            // reversal AddBuildAction()/CurrentStep's own doc comment in House.cs describes for the forward
            // direction.
            house.CurrentStep = 0;

            house.BroadcastPacket(new SCHouseDataPacket([house]), true);
            house.BroadcastPacket(
                new SCHouseBuildProgressPacket(house.TlId, house.ModelId, house.AllAction, house.CurrentAction),
                true);

            if (WorldIntegration.ZoneAuthority)
            {
                HousingZoneBridge.NotifyZoneHouseCreated(house);
                HousingZoneBridge.NotifyZoneHouseBuildState(house);
            }

            SaveLodestoneNow(house);
        }
        else
        {
            // The dominions row is still removed below. A missing House here leaves the lodestone built and
            // owned with nothing tracking the claim, so this must not pass silently.
            Logger.Error(
                "UnclaimTerritory: House {0} for zone {1} not found via HousingManager.GetHouseById - " +
                "the dominion record is being removed but the House itself CANNOT be reset (owner/model will " +
                "stay stuck in its last claimed state). This is a data-integrity risk, needs manual DB fixup.",
                dominion.House, zoneId);
        }

        if (_territoryAgentByZone.TryGetValue(zoneId, out var agent) && agent != null)
        {
            WorldIntegration.DeleteNpcMirror(agent, true);
            _territoryAgentByZone.Remove(zoneId);
        }

        _dominions.Remove(zoneId);
        _guardTowerSettingIdByZone.Remove(zoneId);

        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM dominions WHERE zone_id = @zoneId";
            command.Parameters.AddWithValue("@zoneId", zoneId);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        // Tell already-online clients this territory is unclaimed now, same SC-side courtesy Declare()/
        // ResyncZone already extend - real ZoneId kept so the client can match it against the old entry,
        // everything else zeroed/defaulted to a genuinely-blank DominionData.
        var cleared = new DominionData
        {
            ZoneId = zoneId,
            ExpeditionId = 0,
            FactionId = FactionsEnum.Invalid,
            House = 0,
            TaxRate = 0,
            X = 0,
            Y = 0,
            Z = 0,
            CurHouseTaxMoney = 0,
            CurHuntTaxMoney = 0,
            PeaceTaxMoney = 0,
            CurHouseTaxAaPoint = 0,
            PeaceTaxAaPoint = 0,
            LastPaidTime = DateTime.MinValue,
            LastSiegeEndTime = DateTime.MinValue,
            ReignStartTime = DateTime.MinValue,
            LastTaxRateChangedTime = DateTime.MinValue,
            ObjId = 0,
            TerritoryData = new DominionTerritoryData(),
            SiegeTimers = new DominionSiegeTimers
            {
                Durations = [0, 0, 0, 0, 0],
                Started = DateTime.MinValue,
                Fixed = DateTime.MinValue,
                Bdm = 0,
                SiegePeriod = 0,
                UnkData = EmptyUnkData(),
                Unk2Data = EmptyUnkData()
            },
            NonPvPStart = DateTime.MinValue,
            NonPvPDuration = 0
        };
        WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(cleared, true, true));
        NotifyZoneDominionDeleted(zoneId, rawZoneId);

        Logger.Info("Dominion zone {0} unclaimed via GM tool (was Expedition {1}, Faction {2})",
            zoneId, dominion.ExpeditionId, dominion.OwningFactionId);
        return true;
    }

    /// <summary>See IDominionManager's doc comment.</summary>
    public DominionData ClaimTerritory(ushort zoneId, Models.Game.Expeditions.Expedition expedition, Models.Game.Char.Character declarer)
    {
        if (expedition == null || _dominions.ContainsKey(zoneId))
            return null;

        var lodestone = FindLodestoneInZoneGroup(zoneId);
        if (lodestone == null)
            return null;

        return Declare(zoneId, (uint)expedition.Id, lodestone, declarer);
    }

    /// <summary>
    /// GM/testing counterpart to <see cref="ClaimTerritory"/> for the 4 Hero/faction-only territories - claims
    /// directly for a faction, bypassing the normal Hero-eligibility check in DeclareDominion.cs (this is a GM
    /// tool, same trust level as /claimterritory's existing guild path).
    /// </summary>
    public DominionData ClaimTerritoryForFaction(ushort zoneId, FactionsEnum factionId, Models.Game.Char.Character declarer)
    {
        if (factionId == FactionsEnum.Invalid || _dominions.ContainsKey(zoneId))
            return null;

        var lodestone = FindLodestoneInZoneGroup(zoneId);
        if (lodestone == null)
            return null;

        return DeclareForFaction(zoneId, (uint)factionId, lodestone, declarer);
    }

    public void UpdateSiegePeriod(ushort zoneId, byte period)
    {
        if (!_dominions.TryGetValue(zoneId, out var dominion))
            return;

        dominion.SiegeTimers.SiegePeriod = period;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dominions SET siege_period = @period WHERE zone_id = @zoneId";
        command.Parameters.AddWithValue("@period", period);
        command.Parameters.AddWithValue("@zoneId", zoneId);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    public void SendAllDominionsTo(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        foreach (var dominion in _dominions.Values)
            character.SendPacket(new SCDominionDataPacket(dominion, false, true));
    }

    private void Insert(DominionData dominion, uint guardTowerSettingId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            REPLACE INTO dominions
                (zone_id, expedition_id, faction_id, house, guard_tower_setting_id, tax_rate, x, y, z,
                 cur_house_tax_money, cur_hunt_tax_money, peace_tax_money, cur_house_tax_aa_point, peace_tax_aa_point,
                 last_paid_time, last_siege_end_time, reign_start_time, last_tax_rate_changed_time,
                 siege_period, non_pvp_start, non_pvp_duration)
            VALUES
                (@zoneId, @expeditionId, @factionId, @house, @guardTowerSettingId, @taxRate, @x, @y, @z,
                 @curHouseTaxMoney, @curHuntTaxMoney, @peaceTaxMoney, @curHouseTaxAaPoint, @peaceTaxAaPoint,
                 @lastPaidTime, @lastSiegeEndTime, @reignStartTime, @lastTaxRateChangedTime,
                 @siegePeriod, @nonPvPStart, @nonPvPDuration)
            """;
        command.Parameters.AddWithValue("@zoneId", dominion.ZoneId);
        command.Parameters.AddWithValue("@expeditionId", dominion.ExpeditionId);
        command.Parameters.AddWithValue("@factionId", dominion.OwningFactionId);
        command.Parameters.AddWithValue("@house", dominion.House);
        command.Parameters.AddWithValue("@guardTowerSettingId", guardTowerSettingId);
        command.Parameters.AddWithValue("@taxRate", dominion.TaxRate);
        command.Parameters.AddWithValue("@x", dominion.X);
        command.Parameters.AddWithValue("@y", dominion.Y);
        command.Parameters.AddWithValue("@z", dominion.Z);
        command.Parameters.AddWithValue("@curHouseTaxMoney", dominion.CurHouseTaxMoney);
        command.Parameters.AddWithValue("@curHuntTaxMoney", dominion.CurHuntTaxMoney);
        command.Parameters.AddWithValue("@peaceTaxMoney", dominion.PeaceTaxMoney);
        command.Parameters.AddWithValue("@curHouseTaxAaPoint", dominion.CurHouseTaxAaPoint);
        command.Parameters.AddWithValue("@peaceTaxAaPoint", dominion.PeaceTaxAaPoint);
        command.Parameters.AddWithValue("@lastPaidTime", dominion.LastPaidTime);
        command.Parameters.AddWithValue("@lastSiegeEndTime", dominion.LastSiegeEndTime);
        command.Parameters.AddWithValue("@reignStartTime", dominion.ReignStartTime);
        command.Parameters.AddWithValue("@lastTaxRateChangedTime", dominion.LastTaxRateChangedTime);
        command.Parameters.AddWithValue("@siegePeriod", dominion.SiegeTimers.SiegePeriod);
        command.Parameters.AddWithValue("@nonPvPStart", dominion.NonPvPStart);
        command.Parameters.AddWithValue("@nonPvPDuration", dominion.NonPvPDuration);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Resolves the top-level Nuia/Harihara alliance (FactionsEnum.NuiaAlliance/HaranyaAlliance) a declaring
    /// character belongs to, for DominionData.FactionId - see that field's doc comment for why this matters
    /// (the native X2Dominion:GetOwnerFaction gate). Characters carry their own race-based faction
    /// (Character.Faction, e.g. Nuian/Elf/Harani/Firran); FactionManager already resolves that faction's
    /// MotherId up to the alliance root (same pattern already used by ChatManager's nation channels and
    /// CSResurrectCharacterPacket's war-return-point lookup - Character.Faction.MotherId).
    /// </summary>
    /// <summary>Public so DeclareDominion.cs can resolve a Hero's own alliance faction for the new faction-claim path without duplicating this logic.</summary>
    public static FactionsEnum ResolveOwningFaction(Character declarer)
    {
        var raceFaction = declarer?.Faction;
        if (raceFaction == null)
            return FactionsEnum.Invalid;
        return raceFaction.MotherId != FactionsEnum.Invalid ? raceFaction.MotherId : raceFaction.Id;
    }

    /// <summary>
    /// Same resolution as the Character overload above, but for boot-time reload (Load()) where no live
    /// Character is available - falls back to the claiming guild's owner (or, failing that, any member)
    /// via ExpeditionManager's already-loaded roster, using ExpeditionMember.FactionId (each member's own
    /// race-based faction, refreshed on login - see ExpeditionMember.Refresh).
    /// </summary>
    private FactionsEnum ResolveOwningFaction(uint expeditionId)
    {
        var expedition = expeditionManager.Expeditions.FirstOrDefault(e => (uint)e.Id == expeditionId);
        var ownerMember = expedition?.GetMember(expedition.OwnerId) ?? expedition?.Members.FirstOrDefault();
        if (ownerMember == null)
            return FactionsEnum.Invalid;

        var raceFaction = FactionManager.Instance.GetFaction(ownerMember.FactionId);
        return raceFaction != null && raceFaction.MotherId != FactionsEnum.Invalid
            ? raceFaction.MotherId
            : ownerMember.FactionId;
    }

    /// <summary>internal so GuildDominionManager (old castle system) can build the same TerritoryData shape without duplicating this logic - pure data transform, not manager state.</summary>
    internal static DominionTerritoryData BuildTerritoryData(uint guardTowerSettingId)
    {
        if (guardTowerSettingId == 0)
            return new DominionTerritoryData();

        var settings = SiegeGameData.Instance.GetGuardTowerSettings(guardTowerSettingId);
        if (settings == null)
            throw new InvalidOperationException($"Required guard_tower_settings row {guardTowerSettingId} is missing.");

        return new DominionTerritoryData
        {
            Id = settings.Id,
            Id2 = settings.InitialBuffId,
            MaxGates = settings.MaxGates,
            MaxWalls = settings.MaxWalls,
            RadiusDeclare = settings.RadiusDeclare,
            RadiusDominion = settings.RadiusDominion,
            RadiusOffenseHq = settings.RadiusOffenseHq,
            RadiusSiege = settings.RadiusSiege
        };
    }

    /// <summary>internal so GuildDominionManager can reuse - pure data transform, not manager state.</summary>
    internal static DominionUnkData EmptyUnkData() => new()
    {
        Id = 0,
        ObjId = 0,
        X = 0,
        Y = 0,
        Z = 0,
        Ni = 0,
        Nr = 0,
        Limit = 0,
        UnkIds = []
    };
}
