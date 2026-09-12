using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

public class ButlerGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE butlers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                model_id INTEGER NOT NULL,
                default_garden_slot_count INTEGER,
                reset_all_actability_cost INTEGER,
                reset_all_actability_currency_id INTEGER,
                lp_charge_rate INTEGER,
                max_production_cost INTEGER,
                default_fx_group_id INTEGER NOT NULL,
                trade_available_level INTEGER NOT NULL,
                overwork_production_cost_mul INTEGER NOT NULL,
                default_specialty_trade_slot_count INTEGER NOT NULL
            );
            CREATE TABLE butler_levels (
                id INTEGER PRIMARY KEY,
                butler_id INTEGER NOT NULL,
                level INTEGER NOT NULL,
                max_labor_power INTEGER NOT NULL,
                max_stat_point INTEGER NOT NULL,
                total_exp INTEGER NOT NULL,
                effect_desc TEXT,
                total_garden_count INTEGER NOT NULL,
                butler_harvest_grade_id INTEGER NOT NULL
            );
            CREATE TABLE butler_func_garden_expand_slots (
                id INTEGER PRIMARY KEY,
                butler_id INTEGER NOT NULL,
                level INTEGER NOT NULL,
                total_expand_slot_count INTEGER NOT NULL,
                require_item_id INTEGER NOT NULL,
                require_item_count INTEGER NOT NULL
            );
            CREATE TABLE butler_func_trade_expand_slots (
                id INTEGER PRIMARY KEY,
                butler_id INTEGER NOT NULL,
                level INTEGER NOT NULL,
                total_expand_slot_count INTEGER NOT NULL,
                require_item_id INTEGER NOT NULL,
                require_item_count INTEGER NOT NULL
            );
            CREATE TABLE butler_harvest_grades (
                id INTEGER PRIMARY KEY,
                grade INTEGER NOT NULL,
                "desc" TEXT NOT NULL
            );
            CREATE TABLE butler_harvests (
                id INTEGER PRIMARY KEY,
                item_id INTEGER,
                growth_time INTEGER,
                size INTEGER,
                repeat_count INTEGER,
                actability_group_id INTEGER,
                consume_lp INTEGER,
                loot_pack_id INTEGER,
                bonus_ratio INTEGER,
                bonus_loot_pack_id INTEGER,
                butler_harvest_grade_id INTEGER NOT NULL,
                is_under_water BOOLEAN
            );
            CREATE TABLE doodad_func_bind_butlers (id INTEGER PRIMARY KEY);
            """);
    }

    [Test]
    public async Task Load_IndexesFarmhandContentByItsDatabaseKeys()
    {
        Seed();
        var data = new ButlerGameData();

        data.Load(Connection);

        await Assert.That(data.TryGetTemplate(1, out var butler)).IsTrue();
        await Assert.That(butler.Name).IsEqualTo("Farmhand");
        await Assert.That(butler.DefaultGardenSlotCount.GetValueOrDefault()).IsEqualTo(1u);
        await Assert.That(butler.DefaultFxGroupId).IsEqualTo(4232u);

        await Assert.That(data.TryGetLevel(1, 10, out var level)).IsTrue();
        await Assert.That(level.TotalGardenCount).IsEqualTo(2u);
        await Assert.That(level.ButlerHarvestGradeId).IsEqualTo(2u);

        await Assert.That(data.TryGetGardenSlotExpansion(1, 10, out var gardenByLevel)).IsTrue();
        await Assert.That(gardenByLevel.TotalExpandSlotCount).IsEqualTo(1u);
        await Assert.That(data.TryGetGardenSlotExpansionByTotalCount(1, 1, out var gardenByCount)).IsTrue();
        await Assert.That(gardenByCount.Level).IsEqualTo(10u);

        await Assert.That(data.TryGetTradeSlotExpansion(1, 25, out var tradeByLevel)).IsTrue();
        await Assert.That(tradeByLevel.RequireItemCount).IsEqualTo(2u);
        await Assert.That(data.TryGetTradeSlotExpansionByTotalCount(1, 1, out _)).IsTrue();

        await Assert.That(data.TryGetHarvestGrade(2, out var grade)).IsTrue();
        await Assert.That(grade.Description).IsEqualTo("Advanced");
        await Assert.That(data.TryGetHarvest(100, out var harvest)).IsTrue();
        await Assert.That(harvest.ItemId.GetValueOrDefault()).IsEqualTo(10u);
        await Assert.That(harvest.BonusLootPackId).IsNull();
        await Assert.That(harvest.IsUnderWater.GetValueOrDefault()).IsTrue();
        await Assert.That(data.GetHarvests(2).Count).IsEqualTo(1);

        await Assert.That(data.IsBindingDoodadFunc(77)).IsTrue();
        await Assert.That(data.TryGetBindingDoodadFunc(78, out _)).IsFalse();
    }

    [Test]
    public async Task Load_PreservesNullableTemplateColumnsAndReplacesOldContent()
    {
        Execute(
            """
            INSERT INTO butlers VALUES (2, 'Unconfigured', 1, NULL, NULL, NULL, NULL, NULL, 0, 0, 0, 0);
            """);
        var data = new ButlerGameData();

        data.Load(Connection);

        await Assert.That(data.TryGetTemplate(2, out var unconfigured)).IsTrue();
        await Assert.That(unconfigured.DefaultGardenSlotCount).IsNull();
        await Assert.That(unconfigured.ResetAllActabilityCost).IsNull();
        await Assert.That(unconfigured.LpChargeRate).IsNull();

        Execute("DELETE FROM butlers; INSERT INTO butlers VALUES (3, 'Reloaded', 2, 1, 1, 0, 100, 0, 0, 0, 0, 0);");
        data.Load(Connection);

        await Assert.That(data.TryGetTemplate(2, out _)).IsFalse();
        await Assert.That(data.TryGetTemplate(3, out var reloaded)).IsTrue();
        await Assert.That(reloaded.Name).IsEqualTo("Reloaded");
    }

    [Test]
    public async Task Load_ResolvesTheOnlyButlerAndTreatsTheFollowingThresholdAsASentinel()
    {
        Execute(
            """
            INSERT INTO butlers VALUES (1, 'Farmhand', 2418, 1, 10000, 0, 80, 20000, 4232, 21, 200, 2);
            INSERT INTO butler_levels VALUES (1, 1, 39, 4000, 38, 16547070, NULL, 4, 2);
            INSERT INTO butler_levels VALUES (2, 1, 40, 5000, 39, 18545070, NULL, 4, 2);
            INSERT INTO butler_levels VALUES (3, 1, 41, 5000, 40, 21515070, NULL, 4, 2);
            """);
        var data = new ButlerGameData();

        data.Load(Connection);

        await Assert.That(data.TryGetUniqueTemplate(out var template)).IsTrue();
        await Assert.That(template.Id).IsEqualTo(1u);
        await Assert.That(data.TryGetLevelForCumulativeExperience(1, 18545070, out var level)).IsTrue();
        await Assert.That(level.Level).IsEqualTo(40u);
        await Assert.That(data.TryGetLevelForCumulativeExperience(1, ulong.MaxValue, out var capped)).IsTrue();
        await Assert.That(capped.Level).IsEqualTo(40u);
        await Assert.That(data.TryGetMaximumLevel(1, out var maximum)).IsTrue();
        await Assert.That(maximum.Level).IsEqualTo(40u);
        await Assert.That(FeaturesManager.ResolveButlerLevelLimit(data)).IsEqualTo((byte)40);
        await Assert.That(data.TryGetNextLevelExperienceThreshold(1, out var nextThreshold)).IsTrue();
        await Assert.That(nextThreshold).IsEqualTo(21515070L);
        await Assert.That(data.TryGetLevel(1, 41, out var sentinel)).IsTrue();
        await Assert.That(sentinel.Level).IsEqualTo(41u);

        Execute("INSERT INTO butlers VALUES (2, 'Second', 2, 1, 1, 0, 100, 0, 0, 0, 0, 0);");
        data.Load(Connection);
        await Assert.That(data.TryGetUniqueTemplate(out _)).IsFalse();
        await Assert.That(FeaturesManager.ResolveButlerLevelLimit(data)).IsEqualTo((byte)0);
    }

    private void Seed()
    {
        Execute(
            """
            INSERT INTO butlers VALUES (1, 'Farmhand', 2418, 1, 10000, 0, 80, 20000, 4232, 21, 200, 2);
            INSERT INTO butler_levels VALUES (1, 1, 10, 1000, 3, 500, 'Garden expansion', 2, 2);
            INSERT INTO butler_func_garden_expand_slots VALUES (1, 1, 10, 1, 49000, 3);
            INSERT INTO butler_func_trade_expand_slots VALUES (1, 1, 25, 1, 49000, 2);
            INSERT INTO butler_harvest_grades VALUES (2, 2, 'Advanced');
            INSERT INTO butler_harvests VALUES (100, 10, 60, 2, 3, 4, 50, 5, 10, NULL, 2, 1);
            INSERT INTO doodad_func_bind_butlers VALUES (77);
            """);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
