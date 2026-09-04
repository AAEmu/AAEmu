using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class SpecialtyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        var itemManager = Mock.Of<IItemManager>();
        var skillManager = Mock.Of<ISkillManager>();
        var zoneManager = Mock.Of<IZoneManager>();

        var mailManager = Mock.Of<IMailManager>();

        var saleStore = Mock.Of<ISpecialtySaleStore>();
        var manager = new SpecialtyManager(
            itemManager.Object,
            skillManager.Object,
            zoneManager.Object,
            mailManager.Object,
            new SpecialtySaleCommitter(saleStore.Object));

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(itemManager);
        Mock.VerifyNoOtherCalls(skillManager);
        Mock.VerifyNoOtherCalls(zoneManager);
        Mock.VerifyNoOtherCalls(mailManager);
        Mock.VerifyNoOtherCalls(saleStore);
    }

    [Test]
    public async Task UsesSpecialtyPriceList_OnlyForDedicatedSpecialtyBuyer()
    {
        var specialtyBuyer = new NpcTemplate { Specialty = true };
        var cargoVendor = new NpcTemplate { TradeGoodBuy = true };
        var dualRole = new NpcTemplate { Specialty = true, TradeGoodBuy = true };

        await Assert.That(SpecialtyManager.UsesSpecialtyPriceList(specialtyBuyer)).IsTrue();
        await Assert.That(SpecialtyManager.UsesSpecialtyPriceList(cargoVendor)).IsFalse();
        await Assert.That(SpecialtyManager.UsesSpecialtyPriceList(dualRole)).IsFalse();
    }

    [Test]
    public async Task RequiresProductionContext_DistinguishesSpecialtyPacksFromCargo()
    {
        var specialtyPack = new BackpackTemplate
        {
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 1
        };
        var cargo = new BackpackTemplate
        {
            BackpackType = BackpackType.TradeGoods,
            FreshnessGroupId = 1
        };

        await Assert.That(SpecialtyPackMaterializer.RequiresProductionContext(specialtyPack)).IsTrue();
        await Assert.That(SpecialtyPackMaterializer.RequiresProductionContext(cargo)).IsFalse();
    }

    [Test]
    public async Task SelectSaleSkill_UsesCargoTransactionSkillForTradeGoods()
    {
        var specialtySkill = new SkillTemplate { Id = SkillsEnum.SellBackpack, ConsumeLaborPower = 70 };
        var cargoSkill = new SkillTemplate { Id = SkillsEnum.SellTradeGood, ConsumeLaborPower = 175 };
        var specialtyPack = new BackpackTemplate { BackpackType = BackpackType.TradePack };
        var cargo = new BackpackTemplate { BackpackType = BackpackType.TradeGoods };

        await Assert.That(SpecialtyManager.SelectSaleSkill(specialtyPack, specialtySkill, cargoSkill))
            .IsSameReferenceAs(specialtySkill);
        await Assert.That(SpecialtyManager.SelectSaleSkill(cargo, specialtySkill, cargoSkill))
            .IsSameReferenceAs(cargoSkill);
    }

    [Test]
    public async Task DecodeMailInterestPercent_UsesTenthsOfAPercent()
    {
        await Assert.That(SpecialtyManager.DecodeMailInterestPercent(20)).IsEqualTo(2d);
    }

    [Test]
    [Arguments(8, 0.8d)]
    [Arguments(5, 0.5d)]
    [Arguments(0, 0d)]
    public async Task DecodeSellerShare_UsesTenths(int contentValue, double expected)
    {
        await Assert.That(SpecialtyManager.DecodeSellerShare(contentValue)).IsEqualTo(expected);
    }

    [Test]
    public async Task PrependCurrentSellQuote_KeepsEquippedPackInDemandList()
    {
        var quotes = new List<SpecialtyQuote>
        {
            new() { ItemId = 31830 },
            new() { ItemId = 31831 },
            new() { ItemId = 31832 },
            new() { ItemId = 31833 }
        };

        var found = SpecialtyManager.PrependCurrentSellQuote(quotes, 31832);

        await Assert.That(found).IsTrue();
        await Assert.That(quotes[0].ItemId).IsEqualTo(31832u);
        await Assert.That(quotes[1].ItemId).IsEqualTo(31830u);
        await Assert.That(quotes[2].ItemId).IsEqualTo(31831u);
        await Assert.That(quotes[3].ItemId).IsEqualTo(31832u);
        await Assert.That(quotes[4].ItemId).IsEqualTo(31833u);
    }

    [Test]
    [Arguments(1u, 0u)]
    [Arguments(2u, 1u)]
    [Arguments(3u, 2u)]
    [Arguments(4u, 4u)]
    [Arguments(5u, 0u)]
    public async Task GetTradeGoodCategoryId_MapsOnlyLandRegions(uint regionId, uint expectedCategoryId)
    {
        await Assert.That(SpecialtyManager.GetTradeGoodCategoryId(regionId) ?? 0).IsEqualTo(expectedCategoryId);
    }

    [Test]
    [Arguments(0u, 1300u)]
    [Arguments(30u, 1300u)]
    [Arguments(31u, 1100u)]
    [Arguments(80u, 1100u)]
    [Arguments(81u, 900u)]
    [Arguments(120u, 900u)]
    [Arguments(121u, 700u)]
    [Arguments(199u, 700u)]
    [Arguments(200u, 700u)]
    public async Task SelectTradeGoodPriceIndex_UsesInclusiveThresholds(uint stock, uint expectedIndex)
    {
        var priceIndices = new List<TradeGoodPriceIndex>
        {
            new() { Stock = -1, PriceIndex = 700, Charge = 1000 },
            new() { Stock = 30, PriceIndex = 1300, Charge = 1000 },
            new() { Stock = 80, PriceIndex = 1100, Charge = 1000 },
            new() { Stock = 120, PriceIndex = 900, Charge = 1000 },
            new() { Stock = 199, PriceIndex = 700, Charge = 1000 }
        };

        var result = SpecialtyManager.SelectTradeGoodPriceIndex(priceIndices, stock);

        await Assert.That(result.PriceIndex).IsEqualTo(expectedIndex);
    }

    [Test]
    public async Task LoadTradeGoodData_BuildsDisabledAndEnabledQuotesFromAuthoritativeRows()
    {
        using var connection = CreateTradeGoodDatabase();
        var item = new BackpackTemplate
        {
            Id = 43323,
            Price = 200000,
            Refund = 200000,
            MaxCount = 1
        };
        var manager = CreateManager(item);

        manager.LoadTradeGoodData(connection);
        var tradeGood = new TradeGood
        {
            ItemId = item.Id,
            OutputCount = 5,
            Ratio = 1000,
            Profit = 0,
            Item = item
        };

        var disabled = manager.BuildBuyQuote(tradeGood, 0);
        var enabled = manager.BuildBuyQuote(tradeGood, 1);

        await Assert.That(disabled.ItemId).IsEqualTo(43323u);
        await Assert.That(disabled.Refund).IsEqualTo(260000ul);
        await Assert.That(disabled.NoEventRefund).IsEqualTo(200000ul);
        await Assert.That(disabled.Stock).IsEqualTo(0u);
        await Assert.That(disabled.CanProduce).IsFalse();
        await Assert.That(enabled.Stock).IsEqualTo(1u);
        await Assert.That(enabled.CanProduce).IsTrue();
    }

    [Test]
    public async Task RecordTradeGoodDelivery_ProducesAuthoredBatchesAndKeepsRemainders()
    {
        using var connection = CreateTradeGoodDatabase();
        var manager = CreateManager(
            CreateCargoItem(),
            itemTags: new Dictionary<uint, uint>
            {
                [31832] = 3361,
                [31894] = 3362,
                [49064] = 3363
            });
        manager.LoadTradeGoodData(connection);

        var produced = 0u;
        for (var i = 0; i < 101; i++)
            produced += manager.RecordTradeGoodDelivery(8, 1, 31832);
        for (var i = 0; i < 61; i++)
            produced += manager.RecordTradeGoodDelivery(8, 1, 31894);
        for (var i = 0; i < 21; i++)
            produced += manager.RecordTradeGoodDelivery(8, 1, 49064);

        await Assert.That(produced).IsEqualTo(10u);
        await Assert.That(manager.GetTradeGoodCargoStock(8, 12)).IsEqualTo(10u);
        await Assert.That(manager.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(1u);
        await Assert.That(manager.GetTradeGoodMaterialStock(8, 3362)).IsEqualTo(1u);
        await Assert.That(manager.GetTradeGoodMaterialStock(8, 3363)).IsEqualTo(1u);
        await Assert.That(manager.GetTradeGoodCargoStock(9, 12)).IsEqualTo(0u);

        await Assert.That(manager.TryConsumeTradeGoodCargo(8, 12)).IsTrue();
        await Assert.That(manager.GetTradeGoodCargoStock(8, 12)).IsEqualTo(9u);
    }

    [Test]
    public async Task RecordTradeGoodDelivery_DoesNotCountUnknownItemsOrIncompleteRecipes()
    {
        using var connection = CreateTradeGoodDatabase();
        var manager = CreateManager(
            CreateCargoItem(),
            itemTags: new Dictionary<uint, uint> { [31832] = 3361 });
        manager.LoadTradeGoodData(connection);

        var unknownProduced = manager.RecordTradeGoodDelivery(8, 1, 99999);
        for (var i = 0; i < 50; i++)
            manager.RecordTradeGoodDelivery(8, 1, 31832);

        await Assert.That(unknownProduced).IsEqualTo(0u);
        await Assert.That(manager.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(50u);
        await Assert.That(manager.GetTradeGoodCargoStock(8, 12)).IsEqualTo(0u);
    }

    [Test]
    public async Task AddTradeGoodMaterials_DefaultsToOneCompleteAuthoredBatch()
    {
        using var connection = CreateTradeGoodDatabase();
        var manager = CreateManager(CreateCargoItem());
        manager.LoadTradeGoodData(connection);

        var result = manager.AddTradeGoodMaterials(8, 1, null);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.TradeGoodId).IsEqualTo(12u);
        await Assert.That(result.Produced).IsEqualTo(5u);
        await Assert.That(result.CargoStock).IsEqualTo(5u);
        await Assert.That(result.Materials.Select(x => x.Stock)).IsEquivalentTo(new uint[] { 0, 0, 0 });
    }

    [Test]
    public async Task AddTradeGoodMaterials_OneAmountIncrementsEveryMaterial()
    {
        using var connection = CreateTradeGoodDatabase();
        var manager = CreateManager(CreateCargoItem());
        manager.LoadTradeGoodData(connection);

        var result = manager.AddTradeGoodMaterials(8, 1, [2]);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Produced).IsEqualTo(0u);
        await Assert.That(result.Materials.Select(x => x.Stock)).IsEquivalentTo(new uint[] { 2, 2, 2 });
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingCategoryRelationship()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegoods SET tradegood_category_id = 2");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingMaterialRecipe()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "DELETE FROM tradegood_materials");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsZeroMaterialRequirement()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegood_materials SET count = 0 WHERE id = 1201");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsNegativeOutputCount()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegoods SET count = -1 WHERE id = 12");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsNegativeMaterialRequirement()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegood_materials SET count = -1 WHERE id = 1201");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsDuplicateMaterialTag()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "INSERT INTO tradegood_materials VALUES (1204, 12, 3361, 1)");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingCargoItemTemplate()
    {
        using var connection = CreateTradeGoodDatabase();

        await Assert.That(() => CreateManager(null).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingFallbackPriceIndex()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "DELETE FROM tradegood_priceindices WHERE stock < 0");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsZeroPriceIndexCharge()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegood_priceindices SET charge = 0 WHERE stock = 30");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingContentConfig()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "DELETE FROM content_configs");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingPurchaseSkill()
    {
        using var connection = CreateTradeGoodDatabase();

        await Assert.That(() => CreateManager(CreateCargoItem(), includePurchaseSkill: false).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsMissingCargoSaleSkill()
    {
        using var connection = CreateTradeGoodDatabase();

        await Assert.That(() => CreateManager(CreateCargoItem(), includeSaleSkill: false).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadFreshnessData_ValidatesTemplateRelationships()
    {
        using var connection = CreateFreshnessDatabase();
        var manager = CreateFreshnessManager(new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7
        });

        manager.LoadFreshnessData(connection);

        await Assert.That(manager).IsNotNull();
    }

    [Test]
    public async Task LoadFreshnessData_RejectsMissingReferencedGroup()
    {
        using var connection = CreateFreshnessDatabase();
        var manager = CreateFreshnessManager(new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 99
        });

        await Assert.That(() => manager.LoadFreshnessData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadFreshnessData_RejectsNonIncreasingThresholds()
    {
        using var connection = CreateFreshnessDatabase();
        Execute(connection, "UPDATE freshness_group_items SET time = 21600 WHERE id = 702");

        await Assert.That(() => CreateFreshnessManager().LoadFreshnessData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadFreshnessData_AcceptsAuthoredZeroSellerShare()
    {
        using var connection = CreateFreshnessDatabase();
        Execute(connection, "UPDATE freshness_group_items SET seller_share_ratio = 0 WHERE id = 701");

        CreateFreshnessManager().LoadFreshnessData(connection);
    }

    [Test]
    public async Task LoadFreshnessData_RejectsNegativeSellerShare()
    {
        using var connection = CreateFreshnessDatabase();
        Execute(connection, "UPDATE freshness_group_items SET seller_share_ratio = -1 WHERE id = 701");

        await Assert.That(() => CreateFreshnessManager().LoadFreshnessData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0L, 701u)]
    [Arguments(21599L, 701u)]
    [Arguments(21600L, 701u)]
    [Arguments(21601L, 702u)]
    [Arguments(43200L, 702u)]
    [Arguments(43201L, 703u)]
    [Arguments(999999L, 703u)]
    public async Task SelectFreshnessRow_UsesInclusiveUpperThresholdAndFinalRow(
        long elapsedSeconds,
        uint expectedRowId)
    {
        var rows = new List<FreshnessGroupItem>
        {
            new() { Id = 701, FreshnessGroupId = 7, TimeSeconds = 21600, RewardRate = 1030, SellerShareRatio = 6 },
            new() { Id = 702, FreshnessGroupId = 7, TimeSeconds = 43200, RewardRate = 1000, SellerShareRatio = 6 },
            new() { Id = 703, FreshnessGroupId = 7, TimeSeconds = 86400, RewardRate = 780, SellerShareRatio = 8 }
        };

        var result = SpecialtyManager.SelectFreshnessRow(rows, elapsedSeconds);

        await Assert.That(result.Id).IsEqualTo(expectedRowId);
    }

    [Test]
    public async Task TrySelectFreshnessRow_RequiresValidPersistedStateAndUsesElapsedAge()
    {
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var template = new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7
        };
        var backpack = new Backpack(1, template, 1);
        backpack.InitializeFreshness(now.AddSeconds(-21600), 22);
        var groups = new Dictionary<uint, List<FreshnessGroupItem>>
        {
            [7] =
            [
                new() { Id = 701, FreshnessGroupId = 7, TimeSeconds = 21600, RewardRate = 1030 },
                new() { Id = 702, FreshnessGroupId = 7, TimeSeconds = 43200, RewardRate = 1000 }
            ]
        };

        var selected = SpecialtyManager.TrySelectFreshnessRow(backpack, groups, now, out var row);
        var future = new Backpack(2, template, 1);
        future.InitializeFreshness(now.AddSeconds(1), 22);
        var selectedFuture = SpecialtyManager.TrySelectFreshnessRow(future, groups, now, out _);

        await Assert.That(selected).IsTrue();
        await Assert.That(row.Id).IsEqualTo(701u);
        await Assert.That(selectedFuture).IsFalse();
    }

    [Test]
    public async Task LoadSpecialtySaleData_LoadsAuthoredSkillAndContentSettings()
    {
        using var connection = CreateSpecialtySaleDatabase();

        var manager = CreateSpecialtySaleManager();
        manager.LoadSpecialtySaleData(connection);

        await Assert.That(manager).IsNotNull();
    }

    [Test]
    public async Task BuildSellQuote_WithoutActiveEvent_UsesOnlyRatioAdjustedPrice()
    {
        using var connection = CreateSpecialtySaleDatabase();
        var manager = CreateSpecialtySaleManager();
        manager.LoadSpecialtySaleData(connection);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };
        var bundleItem = new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        };

        var quote = manager.BuildSellQuote(bundleItem, 8);

        await Assert.That(quote.NoEventRefund).IsEqualTo(0ul);
        await Assert.That(quote.Refund).IsEqualTo(353863ul);
        await Assert.That(quote.Ratio).IsEqualTo(130u);
    }

    [Test]
    public async Task EnsurePackPersisted_DirtyPack_PersistsBeforeSale()
    {
        var itemManager = Mock.Of<IItemManager>();
        var pack = new Item();
        itemManager.TryPersistItem(pack).Returns(true);

        var result = SpecialtyManager.EnsurePackPersisted(itemManager.Object, pack);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task EnsurePackPersisted_DirtyPackPersistenceFails_RejectsSale()
    {
        var itemManager = Mock.Of<IItemManager>();
        var pack = new Item();

        var result = SpecialtyManager.EnsurePackPersisted(itemManager.Object, pack);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task EnsurePackPersisted_CleanPack_DoesNotWriteAgain()
    {
        var itemManager = Mock.Of<IItemManager>();
        var pack = new Item { IsDirty = false };

        var result = SpecialtyManager.EnsurePackPersisted(itemManager.Object, pack);

        await Assert.That(result).IsTrue();
        Mock.VerifyNoOtherCalls(itemManager);
    }

    [Test]
    public async Task LoadSpecialtySaleData_RejectsMissingNamedContentSetting()
    {
        using var connection = CreateSpecialtySaleDatabase();
        Execute(connection, "DELETE FROM content_configs WHERE id = 207");

        await Assert.That(() => CreateSpecialtySaleManager().LoadSpecialtySaleData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtySaleData_RejectsMissingSaleSkill()
    {
        using var connection = CreateSpecialtySaleDatabase();

        await Assert.That(() => CreateSpecialtySaleManager(includeSaleSkill: false).LoadSpecialtySaleData(connection))
            .Throws<InvalidDataException>();
    }

    private static BackpackTemplate CreateCargoItem() => new()
    {
        Id = 43323,
        Price = 200000,
        Refund = 200000,
        MaxCount = 1,
        BackpackType = BackpackType.TradeGoods
    };

    private static SpecialtyManager CreateManager(
        BackpackTemplate item,
        bool includePurchaseSkill = true,
        bool includeSaleSkill = true,
        IReadOnlyDictionary<uint, uint> itemTags = null)
    {
        var itemManager = Mock.Of<IItemManager>();
        if (item != null)
        {
            itemManager.GetTemplate(item.Id).Returns(item);
            itemManager.GetShopPrice(item.Id, ShopCurrencyType.Money).Returns(item.Price);
        }
        if (itemTags != null)
        {
            foreach (var (itemId, tagId) in itemTags)
                itemManager.HasItemTag(itemId, tagId).Returns(true);
        }
        var skillManager = Mock.Of<ISkillManager>();
        skillManager.GetSkillTemplate(SkillsEnum.UseTradeGoodStore).Returns(new SkillTemplate
        {
            Id = SkillsEnum.UseTradeGoodStore,
            MaxRange = 4
        });
        if (includeSaleSkill)
        {
            skillManager.GetSkillTemplate(SkillsEnum.SellTradeGood).Returns(new SkillTemplate
            {
                Id = SkillsEnum.SellTradeGood,
                MaxRange = 4,
                ConsumeLaborPower = 175,
                ActabilityGroupId = 31
            });
        }
        if (includePurchaseSkill)
        {
            skillManager.GetSkillTemplate(SkillsEnum.BuyTradeGood).Returns(new SkillTemplate
            {
                Id = SkillsEnum.BuyTradeGood,
                MaxRange = 4,
                ConsumeLaborPower = 75,
                ActabilityGroupId = 31
            });
        }
        return new SpecialtyManager(
            itemManager.Object,
            skillManager.Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter());
    }

    private static SpecialtyManager CreateFreshnessManager(params BackpackTemplate[] backpackTemplates)
    {
        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetAllItems().Returns(backpackTemplates.Cast<ItemTemplate>().ToList());
        return new SpecialtyManager(
            itemManager.Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter());
    }

    private static SpecialtyManager CreateSpecialtySaleManager(bool includeSaleSkill = true)
    {
        var skillManager = Mock.Of<ISkillManager>();
        if (includeSaleSkill)
        {
            skillManager.GetSkillTemplate(SkillsEnum.SellBackpack).Returns(new SkillTemplate
            {
                Id = SkillsEnum.SellBackpack,
                MaxRange = 4,
                ConsumeLaborPower = 70,
                ActabilityGroupId = 31
            });
        }
        return new SpecialtyManager(
            Mock.Of<IItemManager>().Object,
            skillManager.Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter());
    }

    private static SpecialtySaleCommitter CreateSaleCommitter() =>
        new(Mock.Of<ISpecialtySaleStore>().Object);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection CreateTradeGoodDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE tradegood_categories (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE tradegoods (
                id INTEGER PRIMARY KEY,
                item_id INTEGER NOT NULL,
                count INTEGER NOT NULL,
                ratio INTEGER NOT NULL,
                profit INTEGER NOT NULL,
                tradegood_category_id INTEGER NOT NULL,
                disp_order INTEGER NOT NULL);
            CREATE TABLE tradegood_materials (
                id INTEGER PRIMARY KEY,
                tradegood_id INTEGER NOT NULL,
                tag_id INTEGER NOT NULL,
                count INTEGER NOT NULL);
            CREATE TABLE tradegood_priceindices (stock INTEGER NOT NULL, price_index INTEGER NOT NULL, charge INTEGER NOT NULL);
            CREATE TABLE unit_reqs (id INTEGER PRIMARY KEY, owner_type TEXT NOT NULL, owner_id INTEGER NOT NULL, enable TEXT NOT NULL);
            CREATE TABLE enum_content_configs (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE content_configs (id INTEGER NOT NULL, value INTEGER NOT NULL);
            CREATE TABLE item_prices (item_id INTEGER NOT NULL, currency_id INTEGER NOT NULL, price INTEGER NOT NULL, refund INTEGER NOT NULL);

            INSERT INTO tradegood_categories VALUES (1, 'Nuia');
            INSERT INTO tradegoods VALUES (12, 43323, 5, 1000, 0, 1, 103);
            INSERT INTO tradegood_materials VALUES (1201, 12, 3361, 50);
            INSERT INTO tradegood_materials VALUES (1202, 12, 3362, 30);
            INSERT INTO tradegood_materials VALUES (1203, 12, 3363, 10);
            INSERT INTO tradegood_priceindices VALUES (-1, 700, 1000);
            INSERT INTO tradegood_priceindices VALUES (30, 1300, 1000);
            INSERT INTO tradegood_priceindices VALUES (80, 1100, 1000);
            INSERT INTO tradegood_priceindices VALUES (120, 900, 1000);
            INSERT INTO tradegood_priceindices VALUES (199, 700, 1000);
            INSERT INTO unit_reqs VALUES (56852, 'Skill', 36490, 't');
            INSERT INTO unit_reqs VALUES (56853, 'Skill', 36491, 't');
            INSERT INTO unit_reqs VALUES (56854, 'Skill', 36492, 't');
            INSERT INTO enum_content_configs VALUES (190, 'tradegoods_on_sell_level_limit');
            INSERT INTO enum_content_configs VALUES (194, 'tradegoods_on_buy_level_limit');
            INSERT INTO enum_content_configs VALUES (206, 'tradegoods_mail_interest');
            INSERT INTO content_configs VALUES (190, 50);
            INSERT INTO content_configs VALUES (194, 50);
            INSERT INTO content_configs VALUES (206, 20);
            INSERT INTO item_prices VALUES (43323, 0, 200000, 200000);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteConnection CreateFreshnessDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE freshness_group_items (
                id INTEGER PRIMARY KEY,
                freshness_group_id INTEGER NOT NULL,
                time INTEGER NOT NULL,
                reward_rate INTEGER NOT NULL,
                seller_share_ratio INTEGER NULL);

            INSERT INTO freshness_group_items VALUES (701, 7, 21600, 1030, 6);
            INSERT INTO freshness_group_items VALUES (702, 7, 43200, 1000, 6);
            INSERT INTO freshness_group_items VALUES (703, 7, 86400, 780, 8);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteConnection CreateSpecialtySaleDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE unit_reqs (id INTEGER PRIMARY KEY, owner_type TEXT NOT NULL, owner_id INTEGER NOT NULL, enable TEXT NOT NULL);
            CREATE TABLE enum_content_configs (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE content_configs (id INTEGER NOT NULL, value INTEGER NOT NULL);

            INSERT INTO unit_reqs VALUES (49624, 'Skill', 18458, 't');
            INSERT INTO enum_content_configs VALUES (8, 'max_specialty_price_ratio');
            INSERT INTO enum_content_configs VALUES (9, 'min_specialty_price_ratio');
            INSERT INTO enum_content_configs VALUES (18, 'adjust_ratio_per_trade');
            INSERT INTO enum_content_configs VALUES (60, 'seller_share_ratio');
            INSERT INTO enum_content_configs VALUES (87, 'sell_backpack_level_limit');
            INSERT INTO enum_content_configs VALUES (196, 'specialty_price_tradegoods_count');
            INSERT INTO enum_content_configs VALUES (197, 'specialty_price_recover_rate');
            INSERT INTO enum_content_configs VALUES (207, 'specialty_mail_interest');
            INSERT INTO enum_content_configs VALUES (210, 'specialty_goods_ratio_count');
            INSERT INTO content_configs VALUES (8, 130);
            INSERT INTO content_configs VALUES (9, 50);
            INSERT INTO content_configs VALUES (18, 250);
            INSERT INTO content_configs VALUES (60, 8);
            INSERT INTO content_configs VALUES (87, 50);
            INSERT INTO content_configs VALUES (196, 1);
            INSERT INTO content_configs VALUES (197, 25);
            INSERT INTO content_configs VALUES (207, 20);
            INSERT INTO content_configs VALUES (210, 4);
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
