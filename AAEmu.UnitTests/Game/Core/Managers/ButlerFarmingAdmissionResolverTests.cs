using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;

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
            CREATE TABLE butler_specialty_trades (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL,
                craft_id INTEGER NOT NULL, delivery_min_time INTEGER NOT NULL, delivery_max_time INTEGER NOT NULL,
                consume_production_cost INTEGER NOT NULL);
            CREATE TABLE specialty_npcs (id INTEGER PRIMARY KEY, name TEXT NOT NULL, npc_id INTEGER NOT NULL,
                specialty_bundle_id INTEGER NOT NULL, zone_group_id INTEGER NOT NULL);
            CREATE TABLE doodad_func_bind_butlers (id INTEGER PRIMARY KEY);
            """);
    }

    [Test]
    public async Task ResolveHarvest_SubtractsActiveAreaFromVerifiedStoredGardenCapacity()
    {
        var resolver = CreateResolver(out var butler);
        butler.ApplyStoredItem(new ButlerStoredItem((byte)SlotType.Inventory, 10));
        butler.ApplyHarvestJob(new ButlerHarvestJob(1, 100, 1, 1, 10, 0));
        butler.ApplySpecialtyTradeJob(new ButlerSpecialtyTradeJob(2, 17971, 1, 5, 7002, 10, 20));
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
        await Assert.That(admission.HasActiveSpecialtyTradeJob).IsTrue();
    }

    [Test]
    public async Task ResolveSpecialtyTrade_UsesRowIdAndPersistedExpansionCapacity()
    {
        var resolver = CreateResolver(out var butler);
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        await Assert.That(resolver.TryResolveSpecialtyTrade(character, butler, 1, 5,
            out var baseAdmission)).IsTrue();
        await Assert.That(baseAdmission.Trade.CraftId).IsEqualTo(7001u);
        await Assert.That(baseAdmission.AvailableSpecialtyTradeSlots).IsEqualTo(2u);

        butler.ApplyPermanentData(ButlerFarmingService.SpecialtyTradeSlotExpansionPermanentDataKey, 1);
        butler.ApplySpecialtyTradeJob(new ButlerSpecialtyTradeJob(1, 17971, 1, 5, 7002, 10, 20));
        await Assert.That(resolver.TryResolveSpecialtyTrade(character, butler, 2, 6,
            out var expandedAdmission)).IsTrue();
        await Assert.That(expandedAdmission.AvailableSpecialtyTradeSlots).IsEqualTo(2u);
        await Assert.That(resolver.TryResolveSpecialtyTrade(character, butler, 1, 5, out _)).IsFalse();
        await Assert.That(resolver.TryResolveSpecialtyTrade(character, butler, 2, 5, out _)).IsFalse();
    }

    [Test]
    public async Task ResolveSpecialtyTrade_RefusesADestinationOnAnotherContinentThanTheBoundHouse()
    {
        // Same two trades, same house: only the continent each destination group reports changes.
        var resolver = CreateResolver(out var butler, new Dictionary<uint, uint> { [5] = 4, [6] = 4 });
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        var admitted = resolver.TryResolveSpecialtyTrade(character, butler, 1, 5, out _, out var failure);

        await Assert.That(admitted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.OriginRegionMismatch);
    }

    [Test]
    public async Task ResolveSpecialtyTrade_ReportsTheRegionRefusalAheadOfSlotExhaustion()
    {
        // Both are refusals, and the reason the client sees has to be the one that closes the
        // origin hole rather than the one that happens to be checked first.
        var resolver = CreateResolver(out var butler, new Dictionary<uint, uint> { [5] = 4, [6] = 3 });
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };
        butler.ApplySpecialtyTradeJob(new ButlerSpecialtyTradeJob(2, 17972, 2, 6, 7002, 10, 20));

        var admitted = resolver.TryResolveSpecialtyTrade(character, butler, 1, 5, out _, out var failure);

        await Assert.That(admitted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.OriginRegionMismatch);
    }

    [Test]
    public async Task ResolveSpecialtyTrade_RefusesWhenTheHouseContinentCannotBeResolved()
    {
        // A missing row refuses instead of admitting blind: 0 is not a continent.
        var resolver = CreateResolver(out var butler, new Dictionary<uint, uint> { [5] = 3, [6] = 3 }, houseContinentId: 0);
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        var admitted = resolver.TryResolveSpecialtyTrade(character, butler, 1, 5, out _, out var failure);

        await Assert.That(admitted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.OriginRegionMismatch);
    }

    [Test]
    public async Task IsSameOriginRegion_NeverTreatsAnUnresolvedContinentAsAMatch()
    {
        await Assert.That(ButlerSpecialtyTradeRules.IsSameOriginRegion(3, 3)).IsTrue();
        await Assert.That(ButlerSpecialtyTradeRules.IsSameOriginRegion(3, 4)).IsFalse();
        await Assert.That(ButlerSpecialtyTradeRules.IsSameOriginRegion(0, 3)).IsFalse();
        await Assert.That(ButlerSpecialtyTradeRules.IsSameOriginRegion(3, 0)).IsFalse();
    }

    [Test]
    public async Task ResolveGardenStorage_RejectsAStoredItemOutsideTheNativeButlerBagNamespace()
    {
        var resolver = CreateResolver(out var butler);
        butler.ApplyStoredItem(new ButlerStoredItem((byte)SlotType.Equipment, 10));

        await Assert.That(resolver.TryResolveGardenStorage(butler, out _)).IsFalse();
    }

    [Test]
    public async Task ResolveNextGardenSlotExpansion_UsesSavedExpansionCountThenChecksRequiredLevel()
    {
        var resolver = CreateResolver(out var butler);
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        butler.ApplyPermanentData(ButlerProgression.CumulativeExperiencePermanentDataKey, 100);
        await Assert.That(resolver.TryResolveNextGardenSlotExpansion(character, butler, out var first)).IsTrue();
        await Assert.That(first.Expansion.TotalExpandSlotCount).IsEqualTo(1u);
        await Assert.That(first.Expansion.Level).IsEqualTo(10u);

        butler.ApplyPermanentData(ButlerFarmingService.HarvestSlotExpansionPermanentDataKey, 1);
        await Assert.That(resolver.TryResolveNextGardenSlotExpansion(character, butler, out _)).IsFalse();

        butler.ApplyPermanentData(ButlerFarmingService.HarvestSlotExpansionPermanentDataKey, uint.MaxValue);
        await Assert.That(resolver.TryResolveNextGardenSlotExpansion(character, butler, out _)).IsFalse();
    }

    [Test]
    public async Task ResolveNextSpecialtyTradeSlotExpansion_UsesCountThenRequiredLevel()
    {
        var resolver = CreateResolver(out var butler);
        var character = new Character(new UnitCustomModelParams()) { Id = 1 };

        await Assert.That(resolver.TryResolveNextSpecialtyTradeSlotExpansion(character, butler,
            out var first)).IsTrue();
        await Assert.That(first.Expansion.TotalExpandSlotCount).IsEqualTo(1u);
        await Assert.That(first.Expansion.Level).IsEqualTo(1u);

        butler.ApplyPermanentData(ButlerFarmingService.SpecialtyTradeSlotExpansionPermanentDataKey, 1);
        await Assert.That(resolver.TryResolveNextSpecialtyTradeSlotExpansion(character, butler,
            out _)).IsFalse();

        butler.ApplyPermanentData(ButlerProgression.CumulativeExperiencePermanentDataKey, 100);
        await Assert.That(resolver.TryResolveNextSpecialtyTradeSlotExpansion(character, butler,
            out var second)).IsTrue();
        await Assert.That(second.Expansion.TotalExpandSlotCount).IsEqualTo(2u);
        await Assert.That(second.Expansion.Level).IsEqualTo(11u);

        butler.ApplyPermanentData(ButlerFarmingService.SpecialtyTradeSlotExpansionPermanentDataKey, uint.MaxValue);
        await Assert.That(resolver.TryResolveNextSpecialtyTradeSlotExpansion(character, butler,
            out _)).IsFalse();
    }

    private ButlerFarmingAdmissionResolver CreateResolver(out CharacterButler butler) =>
        CreateResolver(out butler, new Dictionary<uint, uint> { [5] = 3, [6] = 3 });

    private ButlerFarmingAdmissionResolver CreateResolver(
        out CharacterButler butler,
        IReadOnlyDictionary<uint, uint> groupContinents,
        uint houseContinentId = HouseContinentId)
    {
        Execute(
            """
            INSERT INTO butlers VALUES (1, 'Farmhand', 2418, 1, 10000, 0, 80, 20000, 4232, 1, 200, 2);
            INSERT INTO butler_levels VALUES (1, 1, 1, 100, 1, 0, NULL, 2, 1);
            -- Synthetic level-11 row reproduces the former failure between content expansion levels 10 and 20.
            INSERT INTO butler_levels VALUES (2, 1, 11, 200, 2, 100, NULL, 3, 1);
            INSERT INTO butler_func_garden_expand_slots VALUES (1, 1, 1, 0, 0, 0);
            INSERT INTO butler_func_garden_expand_slots VALUES (2, 1, 10, 1, 49000, 3);
            INSERT INTO butler_func_garden_expand_slots VALUES (3, 1, 20, 2, 49000, 5);
            INSERT INTO butler_func_trade_expand_slots VALUES (1, 1, 1, 1, 49000, 2);
            INSERT INTO butler_func_trade_expand_slots VALUES (2, 1, 11, 2, 49000, 2);
            INSERT INTO butler_func_trade_expand_slots VALUES (3, 1, 21, 3, 49000, 3);
            INSERT INTO butler_func_trade_expand_slots VALUES (4, 1, 31, 4, 49000, 3);
            INSERT INTO butler_func_trade_expand_slots VALUES (5, 1, 41, 5, 49000, 6);
            INSERT INTO butler_func_trade_expand_slots VALUES (6, 1, 51, 6, 49000, 10);
            INSERT INTO butler_harvest_grades VALUES (1, 1, 'Basic');
            INSERT INTO butler_harvests VALUES (100, 700, 1000, 50, 1, 1, 10, 1, 0, NULL, 1, 0);
            INSERT INTO specialty_npcs VALUES (1, 'Trade', 17971, 10, 5);
            INSERT INTO specialty_npcs VALUES (2, 'Trade2', 17972, 11, 6);
            INSERT INTO butler_specialty_trades VALUES (1, 17971, 7001, 100, 150, 7);
            INSERT INTO butler_specialty_trades VALUES (2, 17972, 7001, 100, 150, 7);
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

        var craft = new Craft
        {
            Id = 7001,
            SkillId = 8001,
            CraftProducts = [new CraftProduct { CraftId = 7001, ItemId = 7002, Amount = 1, Rate = 100 }],
            CraftMaterials = [new CraftMaterial { CraftId = 7001, ItemId = 7003, Amount = 1 }]
        };
        var skill = new SkillTemplate { Id = 8001, ConsumeLaborPower = 1 };
        var craftManager = new CraftManagerStub(craft);
        var skillManager = Mock.Of<ISkillManager>();
        skillManager.GetSkillTemplate(8001).Returns(skill);

        butler = new CharacterButler(1);
        butler.Apply(new CharacterButlerRecord(1, 1, string.Empty, 100, 0, 100));
        butler.ApplyPermanentData(ButlerProgression.CumulativeExperiencePermanentDataKey, 0);
        return new ButlerFarmingAdmissionResolver(
            data,
            itemId => itemId == 700
                ? new ButlerGardenTemplate(700, 1, 1, 100, false)
                : null,
            itemManager.Object,
            craftManager,
            skillManager.Object,
            CreateHousing(HouseZoneId, houseId: 1),
            CreateZoneManager(houseContinentId, groupContinents));
    }

    private const uint HouseZoneId = 129;
    private const uint HouseContinentId = 3;

    /// <summary>
    /// A housing manager that answers one house in <paramref name="zoneId"/>, the way the live
    /// resolver reads a bound farmhand's continent.
    /// </summary>
    private static Func<IHousingManager> CreateHousing(uint zoneId, uint houseId)
    {
        var house = new House { Id = houseId };
        house.Transform.KeepZoneQuietly(zoneId);
        var housing = Mock.Of<IHousingManager>();
        housing.GetHouseById(houseId).Returns(house);
        return () => housing.Object;
    }

    /// <summary>
    /// A zone manager whose bound house zone and destination groups report the given continents,
    /// which is the pair <c>zone_groups.target_id</c> supplies in production.
    /// </summary>
    private static Func<IZoneManager> CreateZoneManager(
        uint houseContinentId, IReadOnlyDictionary<uint, uint> destinationContinents)
    {
        var zones = Mock.Of<IZoneManager>();
        zones.GetTargetIdByZoneId(HouseZoneId).Returns(houseContinentId);
        foreach (var (groupId, continentId) in destinationContinents)
            zones.GetZoneGroupById(groupId)
                .Returns(new ZoneGroup { Id = groupId, TargetId = continentId });
        return () => zones.Object;
    }

    private sealed class CraftManagerStub(Craft craft) : ICraftManager
    {
        public void Load()
        {
        }

        public bool TryGetCraft(uint craftId, out Craft found)
        {
            found = craftId == craft.Id ? craft : null;
            return found != null;
        }

        public bool IsCraftInPack(uint craftPackId, uint craftId) => false;
        public IReadOnlyCollection<uint> GetCraftIdsForPack(uint craftPackId) => [];

        // Craft metadata surface used by the farmhand admission resolver. This stub deliberately
        // answers "not found" for lines, categories and packs: the farmhand tests only ever resolve
        // a single craft, and loading real metadata here would not change any assertion.
        public bool TryGetCraftLine(uint craftLineId, out CraftLine craftLine)
        {
            craftLine = null;
            return false;
        }

        public IReadOnlyCollection<uint> GetCraftIdsForLine(uint craftLineId) => [];

        public bool TryGetCraftCategory(CraftCategoryLevel level, uint categoryId, out CraftCategory category)
        {
            category = null;
            return false;
        }

        public IReadOnlyCollection<uint> GetCraftIdsForCategory(CraftCategoryLevel level, uint categoryId) => [];

        public bool TryGetCraftPack(uint craftPackId, out CraftPack craftPack)
        {
            craftPack = null;
            return false;
        }

        public IReadOnlyCollection<uint> GetUnresolvedCraftPackIds() => [];
        public IReadOnlyCollection<uint> GetUnresolvedProductPackIds() => [];
        public IReadOnlyCollection<CraftCategoryMismatch> GetCraftCategoryMismatches() => [];
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
