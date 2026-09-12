using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ButlerFarmingAdmissionResolverTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE butlers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, model_id INTEGER NOT NULL,
                default_garden_slot_count INTEGER, reset_all_actability_cost INTEGER,
                reset_all_actability_currency_id INTEGER, lp_charge_rate INTEGER, max_production_cost INTEGER,
                default_fx_group_id INTEGER NOT NULL, trade_available_level INTEGER NOT NULL,
                overwork_production_cost_mul INTEGER NOT NULL, default_specialty_trade_slot_count INTEGER NOT NULL);
            CREATE TABLE butler_levels (id INTEGER PRIMARY KEY, butler_id INTEGER NOT NULL, level INTEGER NOT NULL,
                max_labor_power INTEGER NOT NULL, max_stat_point INTEGER NOT NULL, total_exp INTEGER NOT NULL,
                effect_desc TEXT, total_garden_count INTEGER NOT NULL, butler_harvest_grade_id INTEGER NOT NULL);
            CREATE TABLE butler_func_garden_expand_slots (id INTEGER PRIMARY KEY, butler_id INTEGER NOT NULL,
                level INTEGER NOT NULL, total_expand_slot_count INTEGER NOT NULL, require_item_id INTEGER NOT NULL,
                require_item_count INTEGER NOT NULL);
            CREATE TABLE butler_func_trade_expand_slots (id INTEGER PRIMARY KEY, butler_id INTEGER NOT NULL,
                level INTEGER NOT NULL, total_expand_slot_count INTEGER NOT NULL, require_item_id INTEGER NOT NULL,
                require_item_count INTEGER NOT NULL);
            CREATE TABLE butler_harvest_grades (id INTEGER PRIMARY KEY, grade INTEGER NOT NULL, "desc" TEXT NOT NULL);
            CREATE TABLE butler_harvests (id INTEGER PRIMARY KEY, item_id INTEGER, growth_time INTEGER, size INTEGER,
                repeat_count INTEGER, actability_group_id INTEGER, consume_lp INTEGER, loot_pack_id INTEGER,
                bonus_ratio INTEGER, bonus_loot_pack_id INTEGER, butler_harvest_grade_id INTEGER NOT NULL,
                is_under_water BOOLEAN);
            CREATE TABLE doodad_func_bind_butlers (id INTEGER PRIMARY KEY);
            """);
    }

    [Test]
    public async Task ResolveHarvest_SubtractsActiveAreaFromVerifiedStoredGardenCapacity()
    {
        var resolver = CreateResolver(out var butler);
        butler.ApplyStoredItem(new ButlerStoredItem((byte)SlotType.Inventory, 10));
        butler.ApplyHarvestJob(new ButlerHarvestJob(1, 100, 1, 1, 10, 0));
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        await Assert.That(resolver.TryResolveGardenStorage(butler, out var storage)).IsTrue();
        await Assert.That(storage.HeldGardenCount).IsEqualTo(1u);
        await Assert.That(storage.CurrentHarvestGrade).IsEqualTo(1u);
        await Assert.That(storage.LandGardenSize).IsEqualTo(100u);
        await Assert.That(storage.ActiveLandGardenSize).IsEqualTo(50u);

        await Assert.That(resolver.TryResolveGarden(700, out var garden)).IsTrue();
        await Assert.That(garden.RequiredHarvestGrade).IsEqualTo(1u);

        await Assert.That(resolver.TryResolveHarvest(character, butler, 100, out var admission)).IsTrue();
        await Assert.That(admission.ButlerLevel.Level).IsEqualTo(1u);
        await Assert.That(admission.AvailableResources.LandGardenSize).IsEqualTo(50u);
        await Assert.That(admission.LaborPowerPerUnit).IsEqualTo(10u);
        await Assert.That(admission.InputItemTaskType).IsEqualTo(ItemTaskType.RequestButlerHarvestRegister);
    }

    [Test]
    public async Task ResolveGardenStorage_RejectsAStoredItemOutsideTheNativeButlerBagNamespace()
    {
        var resolver = CreateResolver(out var butler);
        butler.ApplyStoredItem(new ButlerStoredItem((byte)SlotType.Equipment, 10));

        await Assert.That(resolver.TryResolveGardenStorage(butler, out _)).IsFalse();
    }

    private ButlerFarmingAdmissionResolver CreateResolver(out CharacterButler butler)
    {
        Execute(
            """
            INSERT INTO butlers VALUES (1, 'Farmhand', 2418, 1, 10000, 0, 80, 20000, 4232, 21, 200, 2);
            INSERT INTO butler_levels VALUES (1, 1, 1, 100, 1, 0, NULL, 2, 1);
            INSERT INTO butler_func_garden_expand_slots VALUES (1, 1, 1, 0, 0, 0);
            INSERT INTO butler_harvest_grades VALUES (1, 1, 'Basic');
            INSERT INTO butler_harvests VALUES (100, 700, 1000, 50, 1, 1, 10, 1, 0, NULL, 1, 0);
            """);
        var data = new ButlerGameData();
        data.Load(Connection);

        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetItemByItemId(10).Returns(new Item
        {
            Id = 10,
            OwnerId = 1,
            TemplateId = 700,
            Count = 1,
            SlotType = SlotType.System
        });

        butler = new CharacterButler(1);
        butler.Apply(new CharacterButlerRecord(1, 1, string.Empty, 100, 0, 100));
        butler.ApplyPermanentData(ButlerProgression.CumulativeExperiencePermanentDataKey, 0);
        return new ButlerFarmingAdmissionResolver(
            data,
            itemId => itemId == 700
                ? new ButlerGardenTemplate(700, 1, 1, 100, false)
                : null,
            itemManager.Object);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
