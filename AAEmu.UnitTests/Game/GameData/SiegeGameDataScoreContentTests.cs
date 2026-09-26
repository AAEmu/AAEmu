using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The siege score reads three content_configs win points and the alliance roles from
/// siege_factions / siege_faction_troops. Both are required content: a database missing either must fail the
/// load rather than settle a siege against a guess.
/// </summary>
[NotInParallel] // the win-point tests each seed the process-wide ContentConfigGameData singleton
public class SiegeGameDataScoreContentTests
{
    [Test]
    public async Task Load_ReadsTheAllianceRolesAndDerivesTheRaider()
    {
        var data = Loaded(SiegeFactionRows);

        await Assert.That(data.FactionRoles.RaiderFactionId).IsEqualTo(114u);
        await Assert.That(data.FactionRoles.CanDefend(148)).IsTrue();
        await Assert.That(data.FactionRoles.CanDefend(149)).IsTrue();
        await Assert.That(data.FactionRoles.CanDefend(114)).IsFalse();
    }

    [Test]
    public async Task Load_RefusesATroopRowForAnAllianceTheRosterDoesNotList()
    {
        var rows = SiegeFactionRows +
                   "INSERT INTO siege_faction_troops VALUES (7, 200, 'f');";

        var ex = Assert.Throws<InvalidOperationException>(() => Loaded(rows));
        await Assert.That(ex.Message).Contains("siege_factions does not list");
    }

    [Test]
    public async Task Load_RefusesARosterWithNoRaider()
    {
        // Both alliances can hold ground, so nothing can ever be the outlaw side.
        var rows = """
                   INSERT INTO siege_factions VALUES (148, 50);
                   INSERT INTO siege_factions VALUES (149, 50);
                   INSERT INTO siege_faction_troops VALUES (1, 148, 'f');
                   INSERT INTO siege_faction_troops VALUES (2, 149, 'f');
                   """;

        var ex = Assert.Throws<InvalidOperationException>(() => Loaded(rows));
        await Assert.That(ex.Message).Contains("expected exactly one raider");
    }

    [Test]
    public async Task PostLoad_ReadsTheWinPoints()
    {
        var data = Loaded(SiegeFactionRows);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededConfigs(
            (SiegeContentConfigKeys.DefenseWinPoint, 1000),
            (SiegeContentConfigKeys.OffenseWinPoint, 100),
            (SiegeContentConfigKeys.OutlawWinPoint, 100)));

        data.PostLoad();

        await Assert.That(data.WinPoints.Defense).IsEqualTo(1000u);
        await Assert.That(data.WinPoints.Offense).IsEqualTo(100u);
        await Assert.That(data.WinPoints.Outlaw).IsEqualTo(100u);
    }

    [Test]
    public async Task PostLoad_RefusesAMissingWinPoint()
    {
        var data = Loaded(SiegeFactionRows);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededConfigs(
            (SiegeContentConfigKeys.OffenseWinPoint, 100),
            (SiegeContentConfigKeys.OutlawWinPoint, 100)));

        var ex = Assert.Throws<InvalidOperationException>(() => data.PostLoad());
        await Assert.That(ex.Message).Contains(SiegeContentConfigKeys.DefenseWinPoint);
    }

    [Test]
    public async Task PostLoad_RefusesAWinPointThatIsNotPositive()
    {
        var data = Loaded(SiegeFactionRows);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededConfigs(
            (SiegeContentConfigKeys.DefenseWinPoint, 0),
            (SiegeContentConfigKeys.OffenseWinPoint, 100),
            (SiegeContentConfigKeys.OutlawWinPoint, 100)));

        var ex = Assert.Throws<InvalidOperationException>(() => data.PostLoad());
        await Assert.That(ex.Message).Contains("must be positive");
    }

    [Test]
    public async Task PostLoad_RefusesANegativeWinPoint()
    {
        var data = Loaded(SiegeFactionRows);
        using var _ = new SingletonScope<ContentConfigGameData>(SeededConfigs(
            (SiegeContentConfigKeys.DefenseWinPoint, -1),
            (SiegeContentConfigKeys.OffenseWinPoint, 100),
            (SiegeContentConfigKeys.OutlawWinPoint, 100)));

        var ex = Assert.Throws<InvalidOperationException>(() => data.PostLoad());
        await Assert.That(ex.Message).Contains("must be positive");
    }

    /// <summary>The shipped siege_factions / siege_faction_troops rows: one raider, two alliances that hold and attack.</summary>
    private const string SiegeFactionRows = """
                                            INSERT INTO siege_factions VALUES (114, 15);
                                            INSERT INTO siege_factions VALUES (148, 50);
                                            INSERT INTO siege_factions VALUES (149, 50);
                                            INSERT INTO siege_faction_troops VALUES (1, 114, 't');
                                            INSERT INTO siege_faction_troops VALUES (2, 148, 'f');
                                            INSERT INTO siege_faction_troops VALUES (3, 148, 't');
                                            INSERT INTO siege_faction_troops VALUES (4, 149, 'f');
                                            INSERT INTO siege_faction_troops VALUES (5, 149, 't');
                                            """;

    private static SiegeGameData Loaded(string extraRows)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            // The tables the rest of SiegeGameData.Load reads, empty: this fixture is about the faction rows.
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
                CREATE TABLE siege_plans (id INTEGER, zone_group_id INTEGER, week_start TEXT);
                CREATE TABLE housings (id INTEGER, guard_tower_setting_id INTEGER);
                CREATE TABLE housing_build_steps (housing_id INTEGER, skill_id INTEGER);
                CREATE TABLE skill_effects (skill_id INTEGER, effect_id INTEGER);
                CREATE TABLE effects (id INTEGER, actual_type TEXT, actual_id INTEGER);
                CREATE TABLE special_effects (id INTEGER, special_effect_type_id INTEGER);
                CREATE TABLE siege_factions (faction_id INTEGER, member_count INTEGER);
                CREATE TABLE siege_faction_troops (id INTEGER, faction_id INTEGER, is_offense TEXT);
                CREATE TABLE siege_extortion_ratios (
                    id INTEGER PRIMARY KEY, faction_id INTEGER NOT NULL, dominion_count INTEGER NOT NULL, ratio INTEGER NOT NULL);
                CREATE TABLE doodad_func_dominion_tax_in_kinds (
                    id INTEGER PRIMARY KEY, item_id INTEGER, count INTEGER, tooltip_text TEXT, next_phase INTEGER);
                -- The dominion-tax catalogs are fail-loud when empty, so this fixture seeds one
                -- well-formed row of each. It asserts nothing about them.
                INSERT INTO siege_extortion_ratios (id, faction_id, dominion_count, ratio) VALUES (1, 114, 2, 10);
                INSERT INTO doodad_func_dominion_tax_in_kinds (id, item_id, count, tooltip_text, next_phase)
                VALUES (1, 26880, 220, '', 31185);
                {extraRows}
                """;
            command.ExecuteNonQuery();
        }

        var data = new SiegeGameData();
        data.Load(connection);
        return data;
    }

    private static ContentConfigGameData SeededConfigs(params (string Name, long Value)[] rows)
    {
        var configs = new ContentConfigGameData();
        foreach (var row in rows)
            configs.SetForTest(row.Name, row.Value);
        return configs;
    }
}
