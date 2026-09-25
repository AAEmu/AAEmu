using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

using TUnit.Mocks;

using Character = AAEmu.Game.Models.Game.Char.Character;
using Expedition = AAEmu.Game.Models.Game.Expeditions.Expedition;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The phase tick against a settlement: a siege settles when its period ends, a settlement that cannot be
/// written leaves the period alone so it is retried, and a cycle already on record is not decided again.
/// </summary>
[NotInParallel] // seeds the process-wide SiegeGameData / ContentConfigGameData / WorldManager singletons
public class SiegeManagerSettlementTests : IDisposable
{
    private const ushort ZoneGroup = 33;
    private const uint Defender = 148;
    private const uint Attacker = 149;
    private const uint Raider = 114;

    private readonly SingletonScope<SiegeGameData> _siegeData;
    private readonly SingletonScope<ContentConfigGameData> _configs;
    private readonly SingletonScope<WorldManager> _world;
    private readonly SiegeGameData _data;

    private readonly FakeDominionManager _dominions = new();
    private readonly FakeSiegeScoreStore _scores = new();
    private readonly SiegeManager _manager;

    public SiegeManagerSettlementTests()
    {
        // The win points are read when the siege content post-loads, so the config rows are in place first.
        _configs = new SingletonScope<ContentConfigGameData>(SeededConfigs());
        _data = LoadedGameData(siegeOpenOverNow: false);
        _siegeData = new SingletonScope<SiegeGameData>(_data);
        _world = new SingletonScope<WorldManager>(new WorldManager(null, null, null, null, null));
        _manager = new SiegeManager(new TaskManager(Mock.Of<ITickManager>().Object), _dominions, _scores);
    }

    public void Dispose() => _world.Dispose();

    [Test]
    public async Task Tick_SettlesTheSiegeWhenItsPeriodEnds()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 100);

        _manager.Tick();

        await Assert.That(_scores.Settlements.Count).IsEqualTo(1);
        var settled = _scores.Settlements[0];
        await Assert.That(settled.Outcome).IsEqualTo(SiegeOutcome.OffenseBrokeThrough);
        await Assert.That(settled.WinnerFactionId).IsEqualTo(Attacker);
        // The cycle the settlement is filed under is the one the schedule says is running now.
        await Assert.That(settled.CycleWeekStart)
            .IsEqualTo(_data.GetCurrentCycleWeekStart(ZoneGroup, DateTime.UtcNow)!.Value);

        // The siege is over, so the phase may move on.
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(1);
        await Assert.That(_dominions.PeriodUpdates[0].ZoneGroupId).IsEqualTo(ZoneGroup);
        await Assert.That(_dominions.PeriodUpdates[0].Period).IsEqualTo((byte)SiegePeriod.Peace);
    }

    [Test]
    public async Task Tick_HandsTheDominionToTheWinnerAndKeepsItWhenTheDefenceHeld()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 10, defense: 0, offense: 20);

        _manager.Tick();

        await Assert.That(_dominions.Applied.Count).IsEqualTo(1);
        var applied = _dominions.Applied[0];
        await Assert.That(applied.Record.Outcome).IsEqualTo(SiegeOutcome.DefenseHeld);
        // A defended siege records no winner: the owner simply kept what it had.
        await Assert.That(applied.Record.WinnerFactionId).IsEqualTo(0u);
        await Assert.That(applied.Record.DefenderFactionId).IsEqualTo(Defender);
    }

    [Test]
    public async Task Tick_GivesTheDominionToTheRaiderWhenItDestroyedEnoughOfTheTower()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 100, defense: 0, offense: 0);

        _manager.Tick();

        await Assert.That(_dominions.Applied[0].Record.Outcome).IsEqualTo(SiegeOutcome.OutlawBrokeThrough);
        await Assert.That(_dominions.Applied[0].Record.WinnerFactionId).IsEqualTo(Raider);
    }

    [Test]
    public async Task Tick_LeavesThePeriodAloneWhenTheSettlementCannotBeWritten()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 100);
        _scores.FailNextSettle = true;

        _manager.Tick();

        // Nothing was settled and the phase was not advanced, so the next tick sees the same transition and
        // tries again - a settlement that failed is deferred, never lost.
        await Assert.That(_scores.Settlements.Count).IsEqualTo(0);
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(0);
        await Assert.That(_dominions.Applied.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Tick_RetriesTheSettlementOnTheNextPass()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 100);
        _scores.FailNextSettle = true;

        _manager.Tick();
        _manager.Tick();

        await Assert.That(_scores.SettleAttempts).IsEqualTo(2);
        await Assert.That(_scores.Settlements.Count).IsEqualTo(1);
        await Assert.That(_scores.Settlements[0].WinnerFactionId).IsEqualTo(Attacker);
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Tick_ConvergesOnTheOutcomeAlreadyOnRecord()
    {
        // A World that restarted between the write and the phase update settles the same cycle again with
        // counters that have since been zeroed. The recorded outcome is the one applied, so the dominion does
        // not quietly revert to the defender.
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.OnRecord.Add(new SiegeSettlementRecord
        {
            ZoneGroupId = ZoneGroup,
            CycleWeekStart = _data.GetCurrentCycleWeekStart(ZoneGroup, DateTime.UtcNow)!.Value,
            SettledAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Score = SiegeScoreState.Empty(ZoneGroup),
            Outcome = SiegeOutcome.OffenseBrokeThrough,
            DefenderFactionId = Defender,
            WinnerFactionId = Attacker,
            Reason = "recorded by the previous boot",
        });
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 0);

        _manager.Tick();

        await Assert.That(_dominions.Applied[0].Record.Outcome).IsEqualTo(SiegeOutcome.OffenseBrokeThrough);
        await Assert.That(_dominions.Applied[0].Record.WinnerFactionId).IsEqualTo(Attacker);
        // The cycle was already on record, so the phase may move on.
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Tick_DoesNotSettleACycleThatNeverReachedTheSiege()
    {
        // ReadyToSiege straight back to peace is not a siege that was fought, so it produces no winner.
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.ReadyToSiege);
        _scores.Give(ZoneGroup, outlaw: 100, defense: 0, offense: 100);

        _manager.Tick();

        await Assert.That(_scores.SettleAttempts).IsEqualTo(0);
        await Assert.That(_dominions.Applied.Count).IsEqualTo(0);
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Tick_DoesNotSettleADominionWithNoOutcomeToDecide()
    {
        // A defender the siege alliances do not include cannot be settled, and must not be given to anyone.
        _dominions.Add(ZoneGroup, 4242, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 100);

        _manager.Tick();

        await Assert.That(_scores.SettleAttempts).IsEqualTo(0);
        await Assert.That(_dominions.Applied.Count).IsEqualTo(0);
        await Assert.That(_dominions.PeriodUpdates.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AwardScore_AddsToTheStoredCounters()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 40);

        using var scope = new SingletonScope<SiegeGameData>(LoadedGameData(siegeOpenOverNow: true));
        var manager = new SiegeManager(new TaskManager(Mock.Of<ITickManager>().Object), _dominions, _scores);

        var state = manager.AwardScore(ZoneGroup, SiegeScoreSide.Offense, 15);

        await Assert.That(state).IsNotNull();
        await Assert.That(state!.OffensePoint).IsEqualTo(55u);
    }

    [Test]
    public async Task AwardScore_RefusesOutsideASiege()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 40);

        // This fixture's siege window ended ten minutes ago, so the schedule says the siege is over however
        // the dominion still remembers it.
        await Assert.That(_manager.AwardScore(ZoneGroup, SiegeScoreSide.Offense, 15)).IsNull();
        await Assert.That(_scores.Awards).IsEqualTo(0);
    }

    [Test]
    public async Task AwardScore_RefusesAZeroAward()
    {
        _dominions.Add(ZoneGroup, Defender, period: (byte)SiegePeriod.Siege);
        _scores.Give(ZoneGroup, outlaw: 0, defense: 0, offense: 40);

        using var scope = new SingletonScope<SiegeGameData>(LoadedGameData(siegeOpenOverNow: true));
        var manager = new SiegeManager(new TaskManager(Mock.Of<ITickManager>().Object), _dominions, _scores);

        await Assert.That(manager.AwardScore(ZoneGroup, SiegeScoreSide.Offense, 0)).IsNull();
        await Assert.That(_scores.Awards).IsEqualTo(0);
    }

    [Test]
    public async Task AwardScore_RefusesAZoneGroupWithNoSiegeSchedule()
    {
        _dominions.Add(999, Defender, period: (byte)SiegePeriod.Siege);

        using var scope = new SingletonScope<SiegeGameData>(LoadedGameData(siegeOpenOverNow: true));
        var manager = new SiegeManager(new TaskManager(Mock.Of<ITickManager>().Object), _dominions, _scores);

        await Assert.That(manager.AwardScore(999, SiegeScoreSide.Offense, 5)).IsNull();
        await Assert.That(_scores.Awards).IsEqualTo(0);
    }

    private static ContentConfigGameData SeededConfigs()
    {
        var configs = new ContentConfigGameData();
        configs.SetForTest(SiegeContentConfigKeys.DefenseWinPoint, 1000);
        configs.SetForTest(SiegeContentConfigKeys.OffenseWinPoint, 100);
        configs.SetForTest(SiegeContentConfigKeys.OutlawWinPoint, 100);
        return configs;
    }

    /// <summary>
    /// A schedule whose siege window is either the ten minutes around now (<paramref name="siegeOpenOverNow"/>
    /// false, so the current moment is already past it and a Siege period has to settle) or the five minutes
    /// either side of it.
    /// </summary>
    private static SiegeGameData LoadedGameData(bool siegeOpenOverNow)
    {
        var now = DateTime.UtcNow;
        var siegeStart = now.AddMinutes(siegeOpenOverNow ? -5 : -10);
        var cycleStart = siegeStart.Date;
        // A window that ends before now, or one that straddles it. The duration is what makes the difference,
        // so it is set from the same moment the start is.
        var siegeMinutes = siegeOpenOverNow ? 10 : 5;

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE guard_tower_settings (id INTEGER, initial_buff_id INTEGER, max_gates INTEGER,
                                               max_walls INTEGER, radius_declare INTEGER, radius_dominion INTEGER,
                                               radius_offense_hq INTEGER, radius_siege INTEGER);
            CREATE TABLE guard_tower_steps (id INTEGER, guard_tower_setting_id INTEGER, step INTEGER,
                                            num_gates INTEGER, num_walls INTEGER, buff_id INTEGER);
            CREATE TABLE dominion_housings (id INTEGER, group_id INTEGER, housing_id INTEGER,
                                            display_text TEXT, grade INTEGER);
            CREATE TABLE siege_zones (id INTEGER, start_siege_weekday INTEGER, start_siege_hour INTEGER,
                                      start_siege_min INTEGER, siege_days INTEGER, siege_hours INTEGER,
                                      siege_mins INTEGER, zone_group_id INTEGER, reinforce_defense_delay_mins INTEGER,
                                      defense_merchant_id INTEGER, offense_merchant_id INTEGER,
                                      dominion_merchant_id INTEGER, monument_doodad_id INTEGER,
                                      start_hero_volunteer_weekday INTEGER, start_hero_volunteer_hour INTEGER,
                                      start_hero_volunteer_min INTEGER, start_ready_to_siege_weekday INTEGER,
                                      start_ready_to_siege_hour INTEGER, start_ready_to_siege_min INTEGER,
                                      start_declare_dominion_weekday INTEGER, start_declare_dominion_hour INTEGER,
                                      start_declare_dominion_min INTEGER, declare_dominion_days INTEGER,
                                      declare_dominion_hours INTEGER, declare_dominion_mins INTEGER);
            INSERT INTO siege_zones (id, zone_group_id, start_siege_weekday, start_siege_hour, start_siege_min,
                                     siege_mins, start_hero_volunteer_weekday, start_hero_volunteer_hour,
                                     start_ready_to_siege_weekday, start_ready_to_siege_hour)
            VALUES (1, {ZoneGroup}, 0, {siegeStart.Hour}, {siegeStart.Minute}, {siegeMinutes}, 0, 0, 0, 0);
            CREATE TABLE siege_plans (id INTEGER, zone_group_id INTEGER, week_start TEXT);
            INSERT INTO siege_plans (id, zone_group_id, week_start) VALUES (1, {ZoneGroup}, '{cycleStart:yyyy-MM-dd HH:mm:ss}');
            CREATE TABLE housings (id INTEGER, guard_tower_setting_id INTEGER);
            CREATE TABLE housing_build_steps (housing_id INTEGER, skill_id INTEGER);
            CREATE TABLE skill_effects (skill_id INTEGER, effect_id INTEGER);
            CREATE TABLE effects (id INTEGER, actual_type TEXT, actual_id INTEGER);
            CREATE TABLE special_effects (id INTEGER, special_effect_type_id INTEGER);
            CREATE TABLE siege_factions (faction_id INTEGER, member_count INTEGER);
            INSERT INTO siege_factions VALUES ({Raider}, 15);
            INSERT INTO siege_factions VALUES ({Defender}, 50);
            INSERT INTO siege_factions VALUES ({Attacker}, 50);
            CREATE TABLE siege_faction_troops (id INTEGER, faction_id INTEGER, is_offense TEXT);
            INSERT INTO siege_faction_troops VALUES (1, {Raider}, 't');
            INSERT INTO siege_faction_troops VALUES (2, {Defender}, 'f');
            INSERT INTO siege_faction_troops VALUES (3, {Defender}, 't');
            INSERT INTO siege_faction_troops VALUES (4, {Attacker}, 'f');
            INSERT INTO siege_faction_troops VALUES (5, {Attacker}, 't');
            CREATE TABLE siege_extortion_ratios (
                id INTEGER PRIMARY KEY, faction_id INTEGER NOT NULL, dominion_count INTEGER NOT NULL, ratio INTEGER NOT NULL);
            CREATE TABLE doodad_func_dominion_tax_in_kinds (
                id INTEGER PRIMARY KEY, item_id INTEGER, count INTEGER, tooltip_text TEXT, next_phase INTEGER);
            -- The dominion-tax catalogs are fail-loud when empty; this fixture asserts nothing about
            -- them, but the loader still requires one well-formed row of each.
            INSERT INTO siege_extortion_ratios (id, faction_id, dominion_count, ratio) VALUES (1, 114, 2, 10);
            INSERT INTO doodad_func_dominion_tax_in_kinds (id, item_id, count, tooltip_text, next_phase)
            VALUES (1, 26880, 220, '', 31185);
            """;
        command.ExecuteNonQuery();

        var data = new SiegeGameData();
        data.Load(connection);
        data.PostLoad();
        return data;
    }

    private sealed class FakeSiegeScoreStore : ISiegeScoreStore
    {
        private readonly Dictionary<ushort, SiegeScoreState> _states = [];

        public List<SiegeSettlementRecord> Settlements { get; } = [];

        public List<SiegeSettlementRecord> OnRecord { get; } = [];

        public bool FailNextSettle { get; set; }

        public int Awards { get; private set; }

        public int SettleAttempts { get; private set; }

        public void Give(ushort zoneGroupId, uint outlaw, uint defense, uint offense) =>
            _states[zoneGroupId] = new SiegeScoreState
            {
                ZoneGroupId = zoneGroupId,
                OutlawPoint = outlaw,
                DefensePoint = defense,
                OffensePoint = offense,
            };

        public SiegeScoreState Read(ushort zoneGroupId) =>
            _states.TryGetValue(zoneGroupId, out var state) ? state : SiegeScoreState.Empty(zoneGroupId);

        public SiegeScoreState Add(ushort zoneGroupId, SiegeScoreSide side, uint amount)
        {
            Awards++;
            var next = Read(zoneGroupId).Plus(side, amount);
            _states[zoneGroupId] = next;
            return next;
        }

        public void Reset(ushort zoneGroupId) => _states[zoneGroupId] = SiegeScoreState.Empty(zoneGroupId);

        public SiegeSettlementRecord Settle(SiegeSettlementRecord record)
        {
            SettleAttempts++;
            if (FailNextSettle)
            {
                FailNextSettle = false;
                throw new InvalidOperationException("the settlement could not be written");
            }

            var onRecord = OnRecord.Find(settled =>
                settled.ZoneGroupId == record.ZoneGroupId && settled.CycleWeekStart == record.CycleWeekStart);
            if (onRecord != null)
                return onRecord;

            Settlements.Add(record);
            Reset(record.ZoneGroupId);
            return record;
        }
    }

    private sealed class FakeDominionManager : IDominionManager
    {
        private readonly Dictionary<ushort, DominionData> _dominions = [];

        public List<(ushort ZoneGroupId, byte Period)> PeriodUpdates { get; } = [];

        public List<(ushort ZoneGroupId, SiegeSettlementRecord Record)> Applied { get; } = [];

        public void Add(ushort zoneGroupId, uint owningFactionId, byte period)
        {
            _dominions[zoneGroupId] = new DominionData
            {
                ZoneId = zoneGroupId,
                OwningFactionId = owningFactionId,
                FactionId = (FactionsEnum)owningFactionId,
                SiegeTimers = new DominionSiegeTimers { SiegePeriod = period },
            };
        }

        public IEnumerable<DominionData> Dominions => _dominions.Values;

        public DominionData GetByZoneId(ushort zoneId) => _dominions.GetValueOrDefault(zoneId);

        public void UpdateSiegePeriod(ushort zoneId, byte period)
        {
            PeriodUpdates.Add((zoneId, period));
            _dominions[zoneId].SiegeTimers.SiegePeriod = period;
        }

        public void ApplySettlement(ushort zoneId, SiegeSettlementRecord record)
        {
            Applied.Add((zoneId, record));
            var dominion = _dominions[zoneId];
            if (record.WinnerFactionId != 0)
            {
                dominion.OwningFactionId = record.WinnerFactionId;
                dominion.FactionId = (FactionsEnum)record.WinnerFactionId;
                dominion.ExpeditionId = 0;
            }

            dominion.LastSiegeEndTime = record.SettledAtUtc;
            dominion.ReignStartTime = record.SettledAtUtc;
        }

        public DominionData GetDominionAtPosition(ushort zoneId, float x, float y) => null!;

        public void Load() => throw new NotSupportedException();

        public DominionData Declare(ushort zoneId, uint expeditionId, House lodestone, Character declarer) =>
            throw new NotSupportedException();

        public DominionData DeclareForFaction(ushort zoneId, uint owningFactionId, House lodestone,
            Character declarer) => throw new NotSupportedException();

        public void UpdateTaxRate(GameConnection connection, ushort zoneId, int taxRate) =>
            throw new NotSupportedException();

        public void SendAllDominionsTo(GameConnection connection) => throw new NotSupportedException();

        public void PayoutTax() => throw new NotSupportedException();

        public bool ResyncZone(ushort zoneId) => true;

        public bool ResyncZoneWithZeroedTestData(ushort zoneId, int diagnosticPaddingBytes = 0) => true;

        public void RelayAllToZone(uint rawZoneId) => throw new NotSupportedException();

        public bool UnclaimTerritory(ushort zoneId) => throw new NotSupportedException();

        public DominionData ClaimTerritory(ushort zoneId, Expedition expedition, Character declarer) =>
            throw new NotSupportedException();

        public DominionData ClaimTerritoryForFaction(ushort zoneId, FactionsEnum factionId, Character declarer) =>
            throw new NotSupportedException();
    }
}
