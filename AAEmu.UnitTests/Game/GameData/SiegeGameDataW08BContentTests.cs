using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Dominions;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.GameData;

public class SiegeGameDataW08BContentTests : SqliteTestBase
{
    /// <summary>
    /// <c>SiegeGameData.PostLoad</c> also reads the three siege win points from the content-config
    /// singleton. This fixture only covers the dominion-tax/guard-tower catalogs, so it supplies the
    /// win points rather than asserting on them.
    /// </summary>
    private static ContentConfigGameData SeededWinPoints()
    {
        var configs = new ContentConfigGameData();
        configs.SetForTest(SiegeContentConfigKeys.DefenseWinPoint, 1000);
        configs.SetForTest(SiegeContentConfigKeys.OffenseWinPoint, 100);
        configs.SetForTest(SiegeContentConfigKeys.OutlawWinPoint, 100);
        return configs;
    }

    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE guard_tower_settings (
                id INTEGER PRIMARY KEY, radius_dominion INTEGER NOT NULL, radius_siege INTEGER NOT NULL,
                radius_declare INTEGER NOT NULL, radius_offense_hq INTEGER NOT NULL, comments TEXT,
                initial_buff_id INTEGER NOT NULL, max_gates INTEGER NOT NULL, max_walls INTEGER NOT NULL);
            CREATE TABLE guard_tower_steps (
                id INTEGER PRIMARY KEY, guard_tower_setting_id INTEGER NOT NULL, step INTEGER NOT NULL,
                num_gates INTEGER NOT NULL, num_walls INTEGER NOT NULL, buff_id INTEGER NOT NULL);
            CREATE TABLE siege_extortion_ratios (
                id INTEGER PRIMARY KEY, faction_id INTEGER NOT NULL, dominion_count INTEGER NOT NULL, ratio INTEGER NOT NULL);
            CREATE TABLE doodad_func_dominion_tax_in_kinds (
                id INTEGER PRIMARY KEY, item_id INTEGER, count INTEGER, tooltip_text TEXT, next_phase INTEGER);
            CREATE TABLE dominion_housings (id INTEGER PRIMARY KEY, group_id INTEGER, housing_id INTEGER, display_text TEXT, grade INTEGER);
            CREATE TABLE siege_zones (
                id INTEGER PRIMARY KEY, start_siege_weekday INTEGER, start_siege_hour INTEGER, start_siege_min INTEGER,
                siege_days INTEGER, siege_hours INTEGER, siege_mins INTEGER, zone_group_id INTEGER,
                reinforce_defense_delay_mins INTEGER, defense_merchant_id INTEGER, offense_merchant_id INTEGER,
                dominion_merchant_id INTEGER, monument_doodad_id INTEGER, start_hero_volunteer_weekday INTEGER,
                start_hero_volunteer_hour INTEGER, start_hero_volunteer_min INTEGER, start_ready_to_siege_weekday INTEGER,
                start_ready_to_siege_hour INTEGER, start_ready_to_siege_min INTEGER, start_declare_dominion_weekday INTEGER,
                start_declare_dominion_hour INTEGER, start_declare_dominion_min INTEGER, declare_dominion_days INTEGER,
                declare_dominion_hours INTEGER, declare_dominion_mins INTEGER);
            CREATE TABLE siege_plans (id INTEGER PRIMARY KEY, zone_group_id INTEGER NOT NULL, week_start TEXT NOT NULL);
            CREATE TABLE housings (id INTEGER PRIMARY KEY, guard_tower_setting_id INTEGER);
            CREATE TABLE housing_build_steps (id INTEGER PRIMARY KEY, housing_id INTEGER NOT NULL, skill_id INTEGER NOT NULL);
            CREATE TABLE skill_effects (id INTEGER PRIMARY KEY, skill_id INTEGER NOT NULL, effect_id INTEGER NOT NULL);
            CREATE TABLE effects (id INTEGER PRIMARY KEY, actual_type TEXT, actual_id INTEGER);
            CREATE TABLE special_effects (id INTEGER PRIMARY KEY, special_effect_type_id INTEGER NOT NULL);
            -- The faction-role loader also runs and refuses an empty roster, so seed the minimal
            -- alliance set it needs. This fixture asserts nothing about faction roles.
            CREATE TABLE siege_factions (faction_id INTEGER PRIMARY KEY, member_count INTEGER);
            CREATE TABLE siege_faction_troops (id INTEGER PRIMARY KEY, faction_id INTEGER, is_offense TEXT);
            INSERT INTO siege_factions VALUES (114, 15);
            INSERT INTO siege_factions VALUES (148, 50);
            INSERT INTO siege_factions VALUES (149, 50);
            INSERT INTO siege_faction_troops VALUES (1, 114, 't');
            INSERT INTO siege_faction_troops VALUES (2, 148, 'f');
            INSERT INTO siege_faction_troops VALUES (3, 148, 't');
            INSERT INTO siege_faction_troops VALUES (4, 149, 'f');
            INSERT INTO siege_faction_troops VALUES (5, 149, 't');
            """);
        SeedValidContent();
    }

    [Test]
    public async Task LoadReadsW08BCatalogs()
    {
        var data = new SiegeGameData();
        data.Load(Connection);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededWinPoints());
        data.PostLoad();

        var settings = data.GetGuardTowerSettings(1);
        await Assert.That(settings).IsNotNull();
        await Assert.That(settings.MaxWalls).IsEqualTo((byte)50);
        await Assert.That(data.GetMaxGuardTowerStep(1)).IsEqualTo(1);
        await Assert.That(data.TryGetSiegeExtortionRatio(114, 2, out var ratio)).IsTrue();
        await Assert.That(ratio.Ratio).IsEqualTo(10);
        var inKind = data.RequireDominionTaxInKind(1);
        await Assert.That(inKind.ItemId).IsEqualTo(26880u);
        await Assert.That(inKind.Count).IsEqualTo(220);
    }

    [Test]
    public async Task BuildStepOnlyLodestoneWithSettingZeroIsValid()
    {
        var data = new SiegeGameData();
        data.Load(Connection);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededWinPoints());
        data.PostLoad();

        await Assert.That(data.IsLodestoneTemplate(2)).IsTrue();
        var territory = DominionManager.BuildTerritoryData(0);
        await Assert.That(territory.RadiusDominion).IsEqualTo((ushort)0);
        await Assert.That(territory.MaxWalls).IsEqualTo((byte)0);
        await Assert.That(() => DominionManager.BuildTerritoryData(999)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LoadRejectsValuesThatWouldWrapWhenNarrowed()
    {
        Execute("UPDATE guard_tower_settings SET max_walls = 256 WHERE id = 1;");
        var data = new SiegeGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LoadRejectsDuplicateExtortionKeys()
    {
        Execute("INSERT INTO siege_extortion_ratios (id, faction_id, dominion_count, ratio) VALUES (99, 114, 2, 11);");
        var data = new SiegeGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LoadRejectsStepsAboveTheContentCap()
    {
        Execute("INSERT INTO guard_tower_steps (id, guard_tower_setting_id, step, num_gates, num_walls, buff_id) VALUES (99, 1, 2, 0, 51, 4773);");
        var data = new SiegeGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LoadRejectsAnEmptyW08BCatalog()
    {
        Execute("DELETE FROM siege_extortion_ratios;");
        var data = new SiegeGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StepRulesAreCapsAndFailLoudlyForMissingSteps()
    {
        var step = new GuardTowerStep { GuardTowerSettingId = 1, Step = 1, NumGates = 2, NumWalls = 15, BuffId = 4772 };
        await Assert.That(GuardTowerStepRules.MayPlaceWall(step, 14)).IsTrue();
        await Assert.That(GuardTowerStepRules.MayPlaceWall(step, 15)).IsFalse();
        await Assert.That(GuardTowerStepRules.MayPlaceGate(step, 1)).IsTrue();
        await Assert.That(GuardTowerStepRules.MayPlaceGate(step, 2)).IsFalse();
        await Assert.That(() => GuardTowerStepRules.RequireStep([step], 2)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExtortionLookupRequiresAnExactUniqueRow()
    {
        var rows = new[]
        {
            new SiegeExtortionRatio { FactionId = 114, DominionCount = 2, Ratio = 10 },
            new SiegeExtortionRatio { FactionId = 114, DominionCount = 2, Ratio = 11 }
        };
        await Assert.That(() => SiegeExtortionRules.RequireRatio(rows, 114, 2)).Throws<InvalidOperationException>();
        await Assert.That(SiegeExtortionRules.RequireRatio(rows.Take(1), 114, 2)).IsEqualTo(10);
    }

    private void SeedValidContent()
    {
        Execute(
            """
            INSERT INTO guard_tower_settings VALUES (1, 110, 250, 250, 80, '', 4771, 6, 50);
            INSERT INTO guard_tower_steps VALUES (1, 1, 1, 0, 15, 4772);
            INSERT INTO siege_extortion_ratios VALUES (1, 114, 2, 10);
            INSERT INTO doodad_func_dominion_tax_in_kinds VALUES (1, 26880, 220, '', 31185);
            INSERT INTO dominion_housings VALUES (1, 2, 744, '', 1);
            INSERT INTO siege_zones VALUES (1, 3, 21, 0, 0, 1, 0, 34, 0, 0, 0, 0, 0, 3, 12, 0, 3, 20, 0, 3, 20, 0, 0, 1, 0);
            INSERT INTO siege_plans VALUES (1, 34, '2026-09-01 00:00:00');
            INSERT INTO housings VALUES (1, 1);
            INSERT INTO housings VALUES (2, 0);
            INSERT INTO housing_build_steps VALUES (1, 1, 1);
            INSERT INTO housing_build_steps VALUES (2, 2, 1);
            INSERT INTO skill_effects VALUES (1, 1, 1);
            INSERT INTO effects VALUES (1, 'SpecialEffect', 1);
            INSERT INTO special_effects VALUES (1, 50);
            """);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
