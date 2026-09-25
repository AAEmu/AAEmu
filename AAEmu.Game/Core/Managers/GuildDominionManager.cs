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
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Guild claim path for zone groups with no <c>siege_zones</c> row (Exeloch / Sungold, 54 / 56).
/// Own MySQL table; same <see cref="DominionData"/> / <see cref="SCDominionDataPacket"/> as Hero
/// claims. Territory buildings still use shipped <c>dominion_housings</c> — there is no separate
/// housing registry for these zones.
/// </summary>
public class GuildDominionManager(IExpeditionManager expeditionManager, IGameDataManager gameDataManager) : Singleton<GuildDominionManager>, IGuildDominionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Unused beyond the constructor - ordering-only dependency so ManagerOrchestrator runs GameDataManager.Load()
    // (which populates SiegeGameData/HousingGameData) before this one. Same pattern DominionManager itself uses
    // and needed for the same reason (BuildTerritoryData/GetDesignByItemId read those game-data singletons).
    private readonly IGameDataManager _gameDataManager = gameDataManager;

    private Dictionary<ushort, DominionData> _guildDominions = [];
    private Dictionary<ushort, uint> _guardTowerSettingIdByZone = [];

    public IEnumerable<DominionData> GuildDominions => _guildDominions.Values;

    public DominionData GetByZoneId(ushort zoneId) => _guildDominions.GetValueOrDefault(zoneId);

    public DominionData GetDominionAtPosition(ushort zoneId, float x, float y)
    {
        if (!_guildDominions.TryGetValue(zoneId, out var dominion))
            return null;

        var dx = x - dominion.X;
        var dy = y - dominion.Y;
        var radius = dominion.TerritoryData?.RadiusDominion ?? 0;
        return dx * dx + dy * dy <= (float)radius * radius ? dominion : null;
    }

    public void Load()
    {
        _guildDominions = [];
        _guardTowerSettingIdByZone = [];

        // Guild claims use the same DominionData tax fields and initial-rate path as faction claims.
        // Require the same W08B content rows instead of letting one claim store run on a fallback.
        HeroContentConfig.RequireDominionTaxBounds(out _, out _);
        HeroContentConfig.RequireDominionTaxLimit();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM guild_dominions";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var zoneId = (ushort)reader.GetInt32(reader.GetOrdinal("zone_id"));
            var guardTowerSettingId = (uint)reader.GetInt32(reader.GetOrdinal("guard_tower_setting_id"));

            var expeditionId = (uint)reader.GetInt32(reader.GetOrdinal("expedition_id"));

            var dominion = new DominionData
            {
                ZoneId = zoneId,
                ExpeditionId = expeditionId,
                OwningFactionId = 0,
                FactionId = ResolveOwningFaction(expeditionId),
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
                TerritoryData = DominionManager.BuildTerritoryData(guardTowerSettingId),
                SiegeTimers = new DominionSiegeTimers
                {
                    Durations = [0, 0, 0, 0, 0],
                    Started = DateTime.MinValue,
                    Fixed = DateTime.MinValue,
                    Bdm = 0,
                    SiegePeriod = (byte)reader.GetInt32(reader.GetOrdinal("siege_period")),
                    UnkData = DominionManager.EmptyUnkData(),
                    Unk2Data = DominionManager.EmptyUnkData()
                },
                NonPvPStart = reader.GetDateTime(reader.GetOrdinal("non_pvp_start")),
                NonPvPDuration = (ushort)reader.GetInt32(reader.GetOrdinal("non_pvp_duration"))
            };

            _guildDominions[zoneId] = dominion;
            _guardTowerSettingIdByZone[zoneId] = guardTowerSettingId;
        }

        Logger.Info("Loaded {0} guild dominions", _guildDominions.Count);
    }

    /// <summary>Same resolution DominionManager.ResolveOwningFaction(uint) uses - duplicated (not shared) since it's a short, simple instance method with its own expeditionManager dependency.</summary>
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

    public void SendAllDominionsTo(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        foreach (var dominion in _guildDominions.Values)
            character.SendPacket(new SCDominionDataPacket(dominion, false, true));
    }

    private static void UpsertRow(DominionData dominion, uint guardTowerSettingId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            REPLACE INTO guild_dominions
                (zone_id, expedition_id, house, guard_tower_setting_id, guard_tower_step, castle_tier, tax_rate, x, y, z,
                 cur_house_tax_money, cur_hunt_tax_money, peace_tax_money, cur_house_tax_aa_point, peace_tax_aa_point,
                 last_paid_time, last_siege_end_time, reign_start_time, last_tax_rate_changed_time, siege_period,
                 non_pvp_start, non_pvp_duration)
            VALUES
                (@zoneId, @expeditionId, @house, @guardTowerSettingId, @guardTowerStep, @castleTier, @taxRate, @x, @y, @z,
                 @curHouseTaxMoney, @curHuntTaxMoney, @peaceTaxMoney, @curHouseTaxAaPoint, @peaceTaxAaPoint,
                 @lastPaidTime, @lastSiegeEndTime, @reignStartTime, @lastTaxRateChangedTime, @siegePeriod,
                 @nonPvPStart, @nonPvPDuration)
            """;
        command.Parameters.AddWithValue("@zoneId", dominion.ZoneId);
        command.Parameters.AddWithValue("@expeditionId", dominion.ExpeditionId);
        command.Parameters.AddWithValue("@house", dominion.House);
        command.Parameters.AddWithValue("@guardTowerSettingId", guardTowerSettingId);
        command.Parameters.AddWithValue("@guardTowerStep", 0);
        command.Parameters.AddWithValue("@castleTier", 0);
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
    /// Guild claim for a zone group without a <c>siege_zones</c> row. Same lodestone build-step, buff,
    /// broadcast and save plumbing as <see cref="DominionManager.Declare"/>; the claim lands in the guild
    /// store, and there is no territory agent because these zones have no <c>dominion_merchant_id</c>.
    /// The skill-driven declare path must call this store so its "already claimed" guard sees guild claims.
    /// </summary>
    public DominionData Declare(ushort zoneId, uint expeditionId, House lodestone, Models.Game.Char.Character declarer)
    {
        if (_guildDominions.ContainsKey(zoneId))
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
            OwningFactionId = 0,
            FactionId = DominionManager.ResolveOwningFaction(declarer) is var declarerFaction && declarerFaction != FactionsEnum.Invalid
                ? declarerFaction
                : ResolveOwningFaction(expeditionId),
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
            TerritoryData = DominionManager.BuildTerritoryData(guardTowerSettingId),
            SiegeTimers = new DominionSiegeTimers
            {
                Bdm = 0,
                Durations = [0, 0, 0, 0, 0],
                Fixed = DateTime.MinValue,
                Started = DateTime.MinValue,
                SiegePeriod = DominionManager.ScheduledSiegePeriod(zoneId, now),
                UnkData = DominionManager.EmptyUnkData(),
                Unk2Data = DominionManager.EmptyUnkData()
            },
            NonPvPDuration = 0,
            NonPvPStart = now
        };

        _guildDominions[zoneId] = dominion;
        _guardTowerSettingIdByZone[zoneId] = guardTowerSettingId;
        UpsertRow(dominion, guardTowerSettingId);
        DominionManager.NotifyZoneDominionClaimed(dominion, lodestone.Transform.ZoneId);

        if (declarer != null)
        {
            lodestone.OwnerId = declarer.Id;
            lodestone.CoOwnerId = declarer.Id;
            lodestone.AccountId = declarer.AccountId;
            lodestone.BroadcastPacket(new SCHouseDataPacket([lodestone]), true);
            if (WorldIntegration.ZoneAuthority)
                HousingZoneBridge.NotifyZoneHouseCreated(lodestone);
        }

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

        if (dominion.TerritoryData?.Id2 is { } initialBuffId and > 0)
            lodestone.Buffs.AddBuff(initialBuffId, lodestone);

        // Declare notices are the skill's own plot / world_message_effects. Do not invent a second string.
        WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(dominion, true, true));

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

    /// <summary>GM/testing counterpart to DominionManager.ClaimTerritory, for the guild-owned zones only.</summary>
    public DominionData ClaimTerritory(ushort zoneId, Models.Game.Expeditions.Expedition expedition, Models.Game.Char.Character declarer)
    {
        if (expedition == null || _guildDominions.ContainsKey(zoneId))
            return null;

        var lodestone = DominionManager.FindLodestoneInZoneGroup(zoneId);
        if (lodestone == null)
            return null;

        return Declare(zoneId, (uint)expedition.Id, lodestone, declarer);
    }

    /// <summary>GM/testing tool - see DominionManager.UnclaimTerritory's doc comment for the full behavior this mirrors (minus Territory Agent NPC cleanup, which never applies to guild zones - see Declare's doc comment).</summary>
    public bool UnclaimTerritory(ushort zoneId)
    {
        if (!_guildDominions.TryGetValue(zoneId, out var dominion))
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
            // Same contract as DominionManager.UnclaimTerritory: the row is removed below, a missing House
            // would leave the lodestone built and owned with nothing tracking the claim.
            Logger.Error(
                "UnclaimTerritory: House {0} for zone {1} not found via HousingManager.GetHouseById - " +
                "the dominion record is being removed but the House itself CANNOT be reset (owner/model will " +
                "stay stuck in its last claimed state). This is a data-integrity risk, needs manual DB fixup.",
                dominion.House, zoneId);
        }

        _guildDominions.Remove(zoneId);
        _guardTowerSettingIdByZone.Remove(zoneId);

        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM guild_dominions WHERE zone_id = @zoneId";
            command.Parameters.AddWithValue("@zoneId", zoneId);
            command.Prepare();
            command.ExecuteNonQuery();
        }

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
                UnkData = DominionManager.EmptyUnkData(),
                Unk2Data = DominionManager.EmptyUnkData()
            },
            NonPvPStart = DateTime.MinValue,
            NonPvPDuration = 0
        };
        WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(cleared, true, true));
        DominionManager.NotifyZoneDominionDeleted(zoneId, rawZoneId);

        Logger.Info("Guild dominion zone {0} unclaimed via GM tool (was Expedition {1})", zoneId, dominion.ExpeditionId);
        return true;
    }

    /// <summary>Guild-system counterpart to DominionManager.RelayAllToZone - re-announces claim state to a (re)loaded Zone. No Territory Agent NPC re-ensure here, see Declare's doc comment for why guild zones never have one.</summary>
    public void RelayAllToZone(uint rawZoneId)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        foreach (var dominion in _guildDominions.Values)
        {
            var house = HousingManager.Instance.GetHouseById(dominion.House);
            if (house?.Transform == null || house.Transform.ZoneId != rawZoneId)
                continue;

            DominionManager.NotifyZoneDominionClaimed(dominion, rawZoneId);
        }
    }

    public void UpdateTaxRate(GameConnection connection, ushort zoneId, int taxRate)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        if (!_guildDominions.TryGetValue(zoneId, out var dominion))
            return;

        if (!HeroContentConfig.TryGetDominionTaxBounds(out var taxMin, out var taxMax)
            || !DominionClaimRules.IsTaxRateAllowed(taxRate, taxMin, taxMax))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        // Guild-leader-only, same gate as declare / unclaim on this path.
        if (character.Expedition == null || (uint)character.Expedition.Id != dominion.ExpeditionId
            || character.Id != character.Expedition.OwnerId)
        {
            character.SendErrorMessage(ErrorMessageType.NoPerm);
            return;
        }

        dominion.TaxRate = taxRate;
        dominion.LastTaxRateChangedTime = DateTime.UtcNow;

        using (var mysqlConnection = MySQL.CreateConnection())
        using (var command = mysqlConnection.CreateCommand())
        {
            command.CommandText = "UPDATE guild_dominions SET tax_rate = @taxRate, last_tax_rate_changed_time = @changed WHERE zone_id = @zoneId";
            command.Parameters.AddWithValue("@taxRate", dominion.TaxRate);
            command.Parameters.AddWithValue("@changed", dominion.LastTaxRateChangedTime);
            command.Parameters.AddWithValue("@zoneId", zoneId);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        WorldManager.Instance.BroadcastPacketToServer(new SCDominionTaxRatePacket(zoneId, dominion.TaxRate));
    }
}
