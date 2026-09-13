using System.Drawing;
using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public partial class SpecialtyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        var itemManager = Mock.Of<IItemManager>();
        var localizationManager = Mock.Of<ILocalizationManager>();
        var skillManager = Mock.Of<ISkillManager>();
        var zoneManager = Mock.Of<IZoneManager>();

        var mailManager = Mock.Of<IMailManager>();

        var saleStore = Mock.Of<ISpecialtySaleStore>();
        var marketStore = Mock.Of<ISpecialtyMarketStore>();
        var purchaseStore = Mock.Of<ISpecialtyPurchaseStore>();
        var worldManager = Mock.Of<IWorldManager>();
        var taskManager = Mock.Of<ITaskManager>();
        var manager = new SpecialtyManager(
            itemManager.Object,
            localizationManager.Object,
            skillManager.Object,
            zoneManager.Object,
            mailManager.Object,
            new SpecialtySaleCommitter(saleStore.Object),
            marketStore.Object,
            purchaseStore.Object,
            worldManager.Object,
            taskManager.Object,
            TimeProvider.System,
            CreateOptions());

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(itemManager);
        Mock.VerifyNoOtherCalls(localizationManager);
        Mock.VerifyNoOtherCalls(skillManager);
        Mock.VerifyNoOtherCalls(zoneManager);
        Mock.VerifyNoOtherCalls(mailManager);
        Mock.VerifyNoOtherCalls(saleStore);
        Mock.VerifyNoOtherCalls(marketStore);
        Mock.VerifyNoOtherCalls(purchaseStore);
        Mock.VerifyNoOtherCalls(worldManager);
        Mock.VerifyNoOtherCalls(taskManager);
    }

    [Test]
    public void Initialize_DoesNotScheduleTimedRecoveryWhenDisabled()
    {
        var taskManager = Mock.Of<ITaskManager>();
        var manager = CreateManager(null, taskManager: taskManager.Object, options: CreateOptions());

        manager.Initialize();

        taskManager.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>())
            .WasCalled(Times.Never);
    }

    [Test]
    public void Initialize_SchedulesTimedRecoveryAtConfiguredInterval()
    {
        var taskManager = Mock.Of<ITaskManager>();
        taskManager.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>()).Returns(true);
        var manager = CreateManager(
            null,
            taskManager: taskManager.Object,
            options: CreateOptions(enableTimedRecovery: true, recoveryIntervalMinutes: 15.5));

        manager.Initialize();

        var interval = TimeSpan.FromMinutes(15.5);
        taskManager.Schedule(Is<AaEmuTask>(task => task is SpecialtyRatioRegenTask), interval, interval, -1)
            .WasCalled(Times.Once);
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(0.000000000001d)]
    public async Task Initialize_RejectsInvalidTimedRecoveryInterval(double intervalMinutes)
    {
        var manager = CreateManager(
            null,
            options: CreateOptions(enableTimedRecovery: true, recoveryIntervalMinutes: intervalMinutes));

        await Assert.That(manager.Initialize).Throws<InvalidDataException>();
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
    [Arguments(1030u, 1f, 364478L, 371768L)]
    [Arguments(1000u, 1f, 353863L, 360940L)]
    [Arguments(780u, 1f, 276013L, 281533L)]
    [Arguments(1030u, 1.5f, 546718L, 557652L)]
    public async Task CalculateSpecialtyPayout_AppliesFreshnessBeforeInterest(
        uint freshnessRewardRate,
        float eventMultiplier,
        long expectedBeforeInterest,
        long expectedTotal)
    {
        var payout = SpecialtyManager.CalculateSpecialtyPayout(
            272202,
            130,
            freshnessRewardRate,
            eventMultiplier,
            2d);

        await Assert.That(payout.BeforeInterest).IsEqualTo(expectedBeforeInterest);
        await Assert.That(payout.Total).IsEqualTo(expectedTotal);
    }

    [Test]
    public async Task CalculateSpecialtyPayout_RoundsOnlyFinalMonetaryValueForInterest()
    {
        var payout = SpecialtyManager.CalculateSpecialtyPayout(1, 50, 1000, 1f, 100d);

        await Assert.That(payout.BeforeInterest).IsEqualTo(1L);
        await Assert.That(payout.Total).IsEqualTo(1L);
    }

    [Test]
    [Arguments(10000L, 0)]
    [Arguments(12499L, 0)]
    [Arguments(12500L, 1)]
    [Arguments(24999L, 1)]
    [Arguments(37500L, 2)]
    [Arguments(25012499L, 1001)]
    public async Task ConvertMoneyToTradeGoodCoinCount_UsesConfiguredRetailRatio(
        long moneyAmount,
        int expected)
    {
        var result = SpecialtyManager.ConvertMoneyToTradeGoodCoinCount(moneyAmount, 25000);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task RecoverRatioUnits_RoundsFinalCompoundedResultAwayFromZero()
    {
        await Assert.That(SpecialtyManager.RecoverRatioUnits(99, 100, 50, 1)).IsEqualTo(100);
        await Assert.That(SpecialtyManager.RecoverRatioUnits(9000, 13000, 25, 5)).IsEqualTo(12051);
        await Assert.That(SpecialtyManager.RecoverRatioUnits(12999, 13000, 25, 1)).IsEqualTo(13000);
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

        var disabled = manager.BuildBuyQuote(tradeGood, 0, 8);
        var enabled = manager.BuildBuyQuote(tradeGood, 1, 8);

        await Assert.That(disabled.ItemId).IsEqualTo(43323u);
        await Assert.That(disabled.Refund).IsEqualTo(260000ul);
        await Assert.That(disabled.NoEventRefund).IsEqualTo(200000ul);
        await Assert.That(disabled.Stock).IsEqualTo(0u);
        await Assert.That(disabled.CanProduce).IsFalse();
        await Assert.That(enabled.Stock).IsEqualTo(1u);
        await Assert.That(enabled.CanProduce).IsTrue();
    }

    [Test]
    public async Task BuildBuyQuote_MultipliesNormalizedChargeAndRoundsOnlyFinalFormulaResult()
    {
        var manager = CreateManager(CreateCargoItem());
        MarketField<List<TradeGoodPriceIndex>>(manager, "_tradeGoodPriceIndices").Add(
            new TradeGoodPriceIndex { Stock = -1, PriceIndex = 1300, Charge = 800 });
        var tradeGood = new TradeGood
        {
            ItemId = 43323,
            Ratio = 500,
            Profit = 1,
            Item = new BackpackTemplate { Id = 43323, Refund = 100000 }
        };

        var quote = manager.BuildBuyQuote(tradeGood, 1, 8);

        await Assert.That(quote.NoEventRefund).IsEqualTo(100001ul);
        await Assert.That(quote.Refund).IsEqualTo(104001ul);
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
        using var specialty = CreateSpecialtySaleDatabase();
        var manager = CreateCargoMaterialAdminManager();
        manager.LoadTradeGoodData(connection);
        manager.LoadSpecialtySaleData(specialty);

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
        using var specialty = CreateSpecialtySaleDatabase();
        var manager = CreateCargoMaterialAdminManager();
        manager.LoadTradeGoodData(connection);
        manager.LoadSpecialtySaleData(specialty);

        var result = manager.AddTradeGoodMaterials(8, 1, [2]);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Produced).IsEqualTo(0u);
        await Assert.That(result.Materials.Select(x => x.Stock)).IsEquivalentTo(new uint[] { 2, 2, 2 });
        var contributions = MarketState(manager).MaterialContributions;
        await Assert.That(contributions[(8, 3361)][0].ItemId).IsEqualTo(31832u);
        await Assert.That(contributions[(8, 3362)][0].ItemId).IsEqualTo(31894u);
        await Assert.That(contributions[(8, 3363)][0].ItemId).IsEqualTo(49064u);
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
    public async Task LoadSpecialtyEventData_LoadsAuthoredAndUnreferencedDescriptors()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var manager = CreateSpecialtyEventManager();

        manager.LoadSpecialtyEventData(connection);

        var catalog = manager.SpecialtyEventCatalog;
        await Assert.That(catalog.Triggers.Count).IsEqualTo(4);
        await Assert.That(catalog.Events.Count).IsEqualTo(2);
        await Assert.That(catalog.Triggers[1].StartMessage).IsEqualTo("stock start");
        await Assert.That(catalog.Triggers[1].EndMessage).IsEqualTo("stock end");
        await Assert.That(catalog.Triggers[3].MessageScope).IsNull();
        await Assert.That(catalog.Triggers[34].SubjectType)
            .IsEqualTo(SpecialtyEventTriggerSubjectType.QuestContextGroup);
        await Assert.That(catalog.Events[14].TargetItemIds).IsEquivalentTo(new uint[] { 43323 });
        await Assert.That(catalog.Events[30].TargetItemIds).IsEquivalentTo(new uint[] { 31840, 32085 });
        await Assert.That(catalog.Events[30].Trigger.SubjectId).IsEqualTo(6u);
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsMissingTriggerWithoutReplacingSnapshot()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var manager = CreateSpecialtyEventManager();
        manager.LoadSpecialtyEventData(connection);
        var loaded = manager.SpecialtyEventCatalog;
        Execute(connection, "UPDATE specialty_events SET specialty_event_trigger_id = 999 WHERE id = 14");

        await Assert.That(() => manager.LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
        await Assert.That(manager.SpecialtyEventCatalog).IsSameReferenceAs(loaded);
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsUnknownObjectType()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_events SET event_object_type = 'Npc' WHERE id = 14");

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsIntegerOutsideDescriptorRange()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_events SET event_type = 4294967297 WHERE id = 14");

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsMissingItemSet()
    {
        using var connection = CreateSpecialtyEventDatabase();

        await Assert.That(() => CreateSpecialtyEventManager(includeItemSet: false).LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsMissingItemSetMember()
    {
        using var connection = CreateSpecialtyEventDatabase();

        await Assert.That(() => CreateSpecialtyEventManager(includeItemSetMembers: false).LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_UsesLocalizedTriggerMessages()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var localizationManager = new LocalizationManager();
        localizationManager.AddTranslation("specialty_event_triggers", "msg_start", 1, "localized start");
        localizationManager.AddTranslation("specialty_event_triggers", "msg_end", 1, "localized end");
        var manager = CreateSpecialtyEventManager(localizationManager: localizationManager);

        manager.LoadSpecialtyEventData(connection);

        await Assert.That(manager.SpecialtyEventCatalog.Triggers[1].StartMessage).IsEqualTo("localized start");
        await Assert.That(manager.SpecialtyEventCatalog.Triggers[1].EndMessage).IsEqualTo("localized end");
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsEmptyItemSet()
    {
        using var connection = CreateSpecialtyEventDatabase();

        await Assert.That(() => CreateSpecialtyEventManager(includeItemSetItems: false).LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_AcceptsTensionConflictStateZero()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_event_triggers SET trigger_subject_id = 0 WHERE id = 28");
        var manager = CreateSpecialtyEventManager();

        manager.LoadSpecialtyEventData(connection);

        await Assert.That(manager.SpecialtyEventCatalog.Triggers[28].SubjectId).IsEqualTo(0u);
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsNonPositiveZoneConflictEventTime()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_event_triggers SET event_time = 0 WHERE id = 28");

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsMessageLongerThanWireLimit()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, $"UPDATE specialty_event_triggers SET msg_start = '{new string('a', 256)}' WHERE id = 1");

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsLocalizedMessageLongerThanWireLimit()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var localizationManager = new LocalizationManager();
        localizationManager.AddTranslation(
            "specialty_event_triggers",
            "msg_start",
            1,
            new string('a', 256));

        await Assert.That(() => CreateSpecialtyEventManager(localizationManager: localizationManager)
                .LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtyEventData_WithoutActivationDoesNotChangeSellQuote()
    {
        using var saleData = CreateSpecialtySaleDatabase();
        using var eventData = CreateSpecialtyEventDatabase();
        var manager = CreateSpecialtyEventManager();
        manager.LoadSpecialtySaleData(saleData);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };
        var bundleItem = new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        };
        var before = manager.BuildSellQuote(bundleItem, 20);

        manager.LoadSpecialtyEventData(eventData);
        var after = manager.BuildSellQuote(bundleItem, 20);

        await Assert.That(after).IsEquivalentTo(before);
        await Assert.That(after.NoEventRefund).IsEqualTo(0ul);
    }

    [Test]
    public async Task ActiveSaleRate_AdjustsBuyQuoteMultiplicativelyAndExpires()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(connection);
        MarketField<List<TradeGoodPriceIndex>>(manager, "_tradeGoodPriceIndices").Add(
            new TradeGoodPriceIndex { Stock = -1, PriceIndex = 1300, Charge = 1000 });
        var tradeGood = new TradeGood
        {
            ItemId = 43323,
            Ratio = 500,
            Profit = 1,
            Item = new BackpackTemplate { Id = 43323, Refund = 100000 }
        };
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(30), 7, out _, out _);

        var active = manager.BuildBuyQuote(tradeGood, 1, 8);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        taskManager.Scheduled.Single().Task.Execute();
        var expired = manager.BuildBuyQuote(tradeGood, 1, 8);

        await Assert.That(active.NoEventRefund).IsEqualTo(100001ul);
        await Assert.That(active.Refund).IsEqualTo(97500ul);
        await Assert.That(expired.NoEventRefund).IsEqualTo(100001ul);
        await Assert.That(expired.Refund).IsEqualTo(130001ul);
    }

    [Test]
    public async Task ActiveSaleRates_StackMultiplicativelyBeforeFinalRounding()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "INSERT INTO specialty_events VALUES (15, 1, 2, 800, 43323, 'Item', 'second sale rate')");
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtyEventData(connection);
        MarketField<List<TradeGoodPriceIndex>>(manager, "_tradeGoodPriceIndices").Add(
            new TradeGoodPriceIndex { Stock = -1, PriceIndex = 1300, Charge = 1000 });
        var tradeGood = new TradeGood
        {
            ItemId = 43323,
            Ratio = 500,
            Profit = 1,
            Item = new BackpackTemplate { Id = 43323, Refund = 100000 }
        };
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(30), 7, out _, out _);
        manager.TryActivateSpecialtyEvent(15, TimeSpan.FromSeconds(30), 7, out _, out _);

        var quote = manager.BuildBuyQuote(tradeGood, 1, 8);

        await Assert.That(quote.Refund).IsEqualTo(78000ul);
    }

    [Test]
    public async Task ActiveOverchargeRate_AdjustsSellQuoteAndSetsComparisonPrice()
    {
        using var saleData = CreateSpecialtySaleDatabase();
        using var eventData = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtySaleData(saleData);
        manager.LoadSpecialtyEventData(eventData);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromSeconds(30), 7, out _, out _);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };
        var bundleItem = new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        };

        var quote = manager.BuildSellQuote(bundleItem, 20);

        await Assert.That(quote.Refund).IsEqualTo(530794ul);
        await Assert.That(quote.NoEventRefund).IsEqualTo(353863ul);
    }

    [Test]
    public async Task ActiveOverchargeRates_StackMultiplicativelyBeforeFinalRounding()
    {
        using var saleData = CreateSpecialtySaleDatabase();
        using var eventData = CreateSpecialtyEventDatabase();
        Execute(eventData, "INSERT INTO specialty_events VALUES (31, 28, 3, 900, 61, 'ItemSet', 'second overcharge rate')");
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtySaleData(saleData);
        manager.LoadSpecialtyEventData(eventData);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromSeconds(30), 7, out _, out _);
        manager.TryActivateSpecialtyEvent(31, TimeSpan.FromSeconds(30), 7, out _, out _);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };
        var bundleItem = new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        };

        var quote = manager.BuildSellQuote(bundleItem, 20);

        await Assert.That(quote.Refund).IsEqualTo(477714ul);
        await Assert.That(quote.NoEventRefund).IsEqualTo(353863ul);
    }

    [Test]
    public async Task NeutralOverchargeRate_DoesNotSetComparisonPrice()
    {
        using var saleData = CreateSpecialtySaleDatabase();
        using var eventData = CreateSpecialtyEventDatabase();
        Execute(eventData, "UPDATE specialty_events SET event_value = 1000 WHERE id = 30");
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtySaleData(saleData);
        manager.LoadSpecialtyEventData(eventData);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromSeconds(30), 7, out _, out _);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };

        var quote = manager.BuildSellQuote(new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        }, 20);

        await Assert.That(quote.Refund).IsEqualTo(353863ul);
        await Assert.That(quote.NoEventRefund).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(1f, 0)]
    [Arguments(1.5f, 150)]
    [Arguments(0.9f, 90)]
    [Arguments(1.35f, 135)]
    public async Task GetSpecialtyMerchantRatioPercent_UsesTotalEventRate(
        float eventMultiplier,
        int expectedPercent)
    {
        var result = SpecialtyManager.GetSpecialtyMerchantRatioPercent(eventMultiplier);

        await Assert.That(result).IsEqualTo(expectedPercent);
    }

    [Test]
    public async Task LoadSpecialtyEventData_RejectsNonPositivePriceMultiplier()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_events SET event_value = 0 WHERE id = 14");

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TryActivateSpecialtyEvent_WhenDisabled_DoesNotSchedule()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(taskManager: taskManager);
        manager.LoadSpecialtyEventData(connection);

        var result = manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(30), 7, out _, out var error);

        await Assert.That(result).IsFalse();
        await Assert.That(error).Contains("disabled");
        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Scheduled).IsEmpty();
    }

    [Test]
    public async Task TryActivateSpecialtyEvent_SchedulesAndExpiresActivation()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(connection);

        var result = manager.TryActivateSpecialtyEvent(
            14,
            TimeSpan.FromSeconds(30),
            7,
            out var activation,
            out var error);

        await Assert.That(result).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(activation.EventId).IsEqualTo(14u);
        await Assert.That(activation.ActivatedByCharacterId).IsEqualTo(7u);
        await Assert.That(manager.GetActiveSpecialtyEvents()).HasSingleItem();
        var scheduledTask = taskManager.Scheduled.Single().Task;
        await Assert.That(scheduledTask).IsTypeOf<SpecialtyEventExpiryTask>();
        await Assert.That(taskManager.Scheduled.Single().StartTime).IsEqualTo(TimeSpan.FromSeconds(30));

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        scheduledTask.Execute();

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
    }

    [Test]
    public async Task TryActivateSpecialtyEvent_StaleExpiryCannotRemoveReplacement()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(connection);
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(10), 7, out _, out _);
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(20), 8, out _, out _);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        taskManager.Scheduled[0].Task.Execute();

        var active = manager.GetActiveSpecialtyEvents();
        await Assert.That(active).HasSingleItem();
        await Assert.That(active[0].ActivatedByCharacterId).IsEqualTo(8u);
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled[0].Task);
    }

    [Test]
    public async Task TryDeactivateSpecialtyEvent_RemovesActivationAndCancelsExpiry()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtyEventData(connection);
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(30), 7, out _, out _);

        var result = manager.TryDeactivateSpecialtyEvent(14, out var activation, out var error);

        await Assert.That(result).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(activation.EventId).IsEqualTo(14u);
        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled.Single().Task);
    }

    [Test]
    public async Task Initialize_ZoneConflictWarActivatesAuthoredEventUntilStateExit()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var zoneManager = new TestZoneManager(8, 20);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            zoneManager: zoneManager);
        manager.LoadSpecialtyEventData(connection);

        manager.Initialize();
        zoneManager.RaiseStateChanged(20, ZoneConflictType.Tension, ZoneConflictType.War);

        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 30 });
        await Assert.That(taskManager.Scheduled).HasSingleItem();
        await Assert.That(taskManager.Scheduled[0].StartTime).IsEqualTo(TimeSpan.FromSeconds(5400));

        zoneManager.RaiseStateChanged(20, ZoneConflictType.War, ZoneConflictType.Peace);

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled[0].Task);
    }

    [Test]
    public async Task Initialize_ReconcilesMatchingZoneConflictStateAtStartup()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, "UPDATE specialty_event_triggers SET trigger_subject_id = 0 WHERE id = 28");
        var taskManager = new RecordingTaskManager();
        var conflict = new ZoneConflict(new ZoneGroup { Id = 20 }) { ZoneGroupId = 20 };
        var zoneManager = new TestZoneManager(8, 20)
        {
            Conflicts = [conflict]
        };
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            zoneManager: zoneManager);
        manager.LoadSpecialtyEventData(connection);

        manager.Initialize();

        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 30 });
    }

    [Test]
    public async Task Initialize_ReconcilesAgainWhenZoneStateChangesDuringStartupScan()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var conflict = new TestZoneConflict(20, ZoneConflictType.War);
        var zoneManager = new TestZoneManager(8, 20)
        {
            Conflicts = [conflict]
        };
        taskManager.BeforeSchedule = () =>
        {
            conflict.ChangeState(ZoneConflictType.Peace);
            zoneManager.RaiseStateChanged(20, ZoneConflictType.War, ZoneConflictType.Peace);
        };
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            zoneManager: zoneManager);
        manager.LoadSpecialtyEventData(connection);

        manager.Initialize();

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Scheduled).HasSingleItem();
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled[0].Task);
    }

    [Test]
    public async Task Initialize_SchedulesReferencedStockTriggerAfterAuthoredInterval()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(enableEvents: true, taskManager: taskManager);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 0);

        manager.Initialize();

        var scheduled = taskManager.Scheduled.Single(x => x.Task is SpecialtyStockEventCheckTask);
        await Assert.That(scheduled.StartTime).IsEqualTo(TimeSpan.FromSeconds(43000));
        await Assert.That(scheduled.RepeatInterval).IsEqualTo(TimeSpan.FromSeconds(43000));
    }

    [Test]
    [Arguments(159u, 0, false)]
    [Arguments(160u, 0, true)]
    [Arguments(200u, 123, true)]
    [Arguments(200u, 124, false)]
    [Arguments(201u, 0, false)]
    public async Task CheckStockEventTrigger_UsesExplicitStockBandAndPerMilleRoll(
        uint stock,
        int roll,
        bool expectedActive)
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(enableEvents: true, taskManager: taskManager);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, stock);

        manager.CheckStockEventTrigger(1, roll);

        await Assert.That(manager.GetActiveSpecialtyEvents().Any(x => x.EventId == 14))
            .IsEqualTo(expectedActive);
    }

    [Test]
    public async Task Initialize_RestoresStockCheckCadenceAfterRestart()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore();
        var timeProvider = new FakeTimeProvider();
        var firstTasks = new RecordingTaskManager();
        var first = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: firstTasks,
            timeProvider: timeProvider,
            marketStore: store);
        first.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(first, 0);
        first.Initialize();

        timeProvider.Advance(TimeSpan.FromSeconds(1000));
        var restartedTasks = new RecordingTaskManager();
        var restarted = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: restartedTasks,
            timeProvider: timeProvider,
            marketStore: store);
        restarted.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(restarted, 0);
        SetMarketState(restarted, store.Load());

        restarted.Initialize();

        var scheduled = restartedTasks.Scheduled.Single(x => x.Task is SpecialtyStockEventCheckTask);
        await Assert.That(scheduled.StartTime).IsEqualTo(TimeSpan.FromSeconds(42000));
    }

    [Test]
    public async Task Initialize_RestoresActiveStockEventForRemainingDuration()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore();
        var timeProvider = new FakeTimeProvider();
        var firstTasks = new RecordingTaskManager();
        var first = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: firstTasks,
            timeProvider: timeProvider,
            marketStore: store);
        first.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(first, 160);
        first.CheckStockEventTrigger(1, 0);

        timeProvider.Advance(TimeSpan.FromSeconds(600));
        var restartedTasks = new RecordingTaskManager();
        var restarted = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: restartedTasks,
            timeProvider: timeProvider,
            marketStore: store);
        restarted.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(restarted, 160);
        SetMarketState(restarted, store.Load());

        restarted.Initialize();

        await Assert.That(restarted.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });
        var expiry = restartedTasks.Scheduled.Single(x => x.Task is SpecialtyEventExpiryTask);
        await Assert.That(expiry.StartTime).IsEqualTo(TimeSpan.FromSeconds(3000));

        timeProvider.Advance(TimeSpan.FromSeconds(3000));
        expiry.Task.Execute();
        await Assert.That(restarted.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(store.Load().StockEventActivations).IsEmpty();
    }

    [Test]
    public async Task Initialize_ReplacesPreviouslyScheduledStockCheck()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            marketStore: store);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 0);

        manager.Initialize();
        var first = taskManager.Scheduled.Single(x => x.Task is SpecialtyStockEventCheckTask).Task;
        manager.Initialize();

        await Assert.That(taskManager.Scheduled.Count(x => x.Task is SpecialtyStockEventCheckTask)).IsEqualTo(2);
        await Assert.That(taskManager.Cancelled).Contains(first);

        var revision = store.Load().Revision;
        first.Execute();
        await Assert.That(store.Load().Revision).IsEqualTo(revision);
        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
    }

    [Test]
    public async Task MarketConflict_ReconcilesPersistedStockEventRuntime()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore();
        var taskManager = new RecordingTaskManager();
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            timeProvider: timeProvider,
            marketStore: store);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 0);
        manager.Initialize();

        var external = store.Load();
        var startedAt = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        external.StockEventActivations[14] = new SpecialtyStockEventActivation(startedAt, startedAt + 3600);
        external.Revision++;
        store.Commit(new SpecialtyMarketWrite(store.Load(), external));

        await Assert.That(() => manager.RunStockEventCheck(1, 999))
            .Throws<SpecialtyMarketConflictException>();

        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });
        await Assert.That(taskManager.Scheduled.Count(x => x.Task is SpecialtyEventExpiryTask)).IsEqualTo(1);
        await Assert.That(taskManager.Scheduled.Count(x => x.Task is SpecialtyStockEventCheckTask)).IsEqualTo(2);
    }

    [Test]
    public async Task StockEventCheck_ActivatesForAuthoredDurationAndExpires()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        Execute(eventData, "UPDATE specialty_event_triggers SET event_rate = 1000 WHERE id = 1");
        var taskManager = new RecordingTaskManager();
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 160);
        manager.Initialize();
        var checkTask = taskManager.Scheduled.Single(x => x.Task is SpecialtyStockEventCheckTask).Task;

        checkTask.Execute();

        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });
        var expiry = taskManager.Scheduled.Single(x => x.Task is SpecialtyEventExpiryTask);
        await Assert.That(expiry.StartTime).IsEqualTo(TimeSpan.FromSeconds(3600));

        MarketState(manager).CargoStock[(8, 12)] = 0;
        checkTask.Execute();
        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });

        timeProvider.Advance(TimeSpan.FromSeconds(3600));
        expiry.Task.Execute();

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
    }

    [Test]
    public async Task StockEventExpiry_CommitFailureIsReconciledByNextCheckAndCanActivateAgain()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore();
        var tasks = new RecordingTaskManager();
        var clock = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true, taskManager: tasks, timeProvider: clock, marketStore: store);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 160);
        manager.CheckStockEventTrigger(1, 0);
        var oldExpiry = tasks.Scheduled.Single().Task;
        clock.Advance(TimeSpan.FromHours(1));
        store.CommitFailure = new IOException("Transient expiry failure");

        await Assert.That(() => oldExpiry.Execute()).Throws<IOException>();
        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(store.Load().StockEventActivations.Count).IsEqualTo(1);

        store.CommitFailure = null;
        manager.CheckStockEventTrigger(1, 999); // Losing roll must still retry expiry cleanup.
        await Assert.That(store.Load().StockEventActivations).IsEmpty();
        manager.CheckStockEventTrigger(1, 0);
        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });
        oldExpiry.Execute(); // A late retry of the old token must not expire the new activation.
        await Assert.That(store.Load().StockEventActivations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StockEventActivation_CommitFailureCancelsExpiryAndDoesNotPublish()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var store = new InMemoryMarketStore { CommitFailure = new IOException("Commit failed") };
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            marketStore: store);
        manager.LoadSpecialtyEventData(eventData);
        ConfigureStockEventMarket(manager, 160);

        await Assert.That(() => manager.CheckStockEventTrigger(1, 0)).Throws<IOException>();

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled.Single().Task);
        await Assert.That(store.Load().StockEventActivations).IsEmpty();
    }

    [Test]
    public async Task StockEventExpiry_DoesNotRemoveManualActivationOrApplyEventTwice()
    {
        using var eventData = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(eventData);
        var tradeGood = ConfigureStockEventMarket(manager, 160);
        manager.CheckStockEventTrigger(1, 0);
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromHours(2), 7, out _, out _);

        MarketField<List<TradeGoodPriceIndex>>(manager, "_tradeGoodPriceIndices").Add(
            new TradeGoodPriceIndex { Stock = -1, PriceIndex = 700, Charge = 1000 });
        var quote = manager.BuildBuyQuote(tradeGood, 160, 8);
        timeProvider.Advance(TimeSpan.FromSeconds(3600));
        taskManager.Scheduled[0].Task.Execute();

        await Assert.That(quote.Refund).IsEqualTo(105000ul);
        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 14 });
    }

    [Test]
    [Arguments("UPDATE specialty_event_triggers SET trigger_value_1 = 201 WHERE id = 1")]
    [Arguments("UPDATE specialty_event_triggers SET trigger_value_2 = 0 WHERE id = 1")]
    [Arguments("UPDATE specialty_event_triggers SET check_time = 0 WHERE id = 1")]
    [Arguments("UPDATE specialty_event_triggers SET event_rate = 1001 WHERE id = 1")]
    [Arguments("UPDATE specialty_event_triggers SET event_time = 0 WHERE id = 1")]
    public async Task LoadSpecialtyEventData_RejectsInvalidStockTriggerPolicy(string update)
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, update);

        await Assert.That(() => CreateSpecialtyEventManager().LoadSpecialtyEventData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task AutomaticZoneConflictEvent_ExpiresAtAuthoredSafetyCap()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            timeProvider: timeProvider);
        manager.LoadSpecialtyEventData(connection);
        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.War);

        timeProvider.Advance(TimeSpan.FromSeconds(5400));
        taskManager.Scheduled.Single().Task.Execute();

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
    }

    [Test]
    public async Task ZoneConflictTransition_SwapsWarEventForPeaceEvent()
    {
        using var connection = CreateSpecialtyEventDatabase();
        Execute(connection, """
            INSERT INTO specialty_event_triggers VALUES
                (33, 20, 4, 0, 0, 4260, 0, 4200, 2, 'peace start', 'peace end', 7, 'EnumHonorPointWarState');
            INSERT INTO specialty_events VALUES
                (40, 33, 3, 900, 61, 'ItemSet', 'peace overcharge rate');
            """);
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(enableEvents: true, taskManager: taskManager);
        manager.LoadSpecialtyEventData(connection);

        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.War);
        var warEvents = manager.GetActiveSpecialtyEvents().Select(x => x.EventId).ToArray();
        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.Peace);
        var peaceEvents = manager.GetActiveSpecialtyEvents().Select(x => x.EventId).ToArray();

        await Assert.That(warEvents).IsEquivalentTo(new uint[] { 30 });
        await Assert.That(peaceEvents).IsEquivalentTo(new uint[] { 40 });
        await Assert.That(taskManager.Scheduled[1].StartTime).IsEqualTo(TimeSpan.FromSeconds(4200));
        await Assert.That(taskManager.Cancelled).Contains(taskManager.Scheduled[0].Task);
    }

    [Test]
    public async Task AutomaticStateExit_DoesNotRemoveManualActivation()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(enableEvents: true, taskManager: taskManager);
        manager.LoadSpecialtyEventData(connection);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromHours(2), 7, out _, out _);
        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.War);

        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.Peace);

        var active = manager.GetActiveSpecialtyEvents();
        await Assert.That(active).HasSingleItem();
        await Assert.That(active[0].ActivatedByCharacterId).IsEqualTo(7u);
        await Assert.That(manager.TryDeactivateSpecialtyEvent(30, out _, out _)).IsTrue();
        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
    }

    [Test]
    public async Task ManualExpiry_DoesNotRemoveAutomaticActivationOrApplyEventTwice()
    {
        using var saleData = CreateSpecialtySaleDatabase();
        using var eventData = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var timeProvider = new FakeTimeProvider();
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            timeProvider: timeProvider);
        manager.LoadSpecialtySaleData(saleData);
        manager.LoadSpecialtyEventData(eventData);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromSeconds(30), 7, out _, out _);
        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.War);
        var item = new BackpackTemplate { Id = 31840, Refund = 100000 };

        var overlappingQuote = manager.BuildSellQuote(new SpecialtyBundleItem
        {
            ItemId = item.Id,
            Profit = 55639,
            Ratio = 3095,
            Item = item
        }, 20);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        taskManager.Scheduled[0].Task.Execute();

        await Assert.That(overlappingQuote.Refund).IsEqualTo(530794ul);
        await Assert.That(manager.GetActiveSpecialtyEvents().Select(x => x.EventId))
            .IsEquivalentTo(new uint[] { 30 });
        await Assert.That(manager.TryDeactivateSpecialtyEvent(30, out _, out var error)).IsFalse();
        await Assert.That(error).Contains("manually active");
    }

    [Test]
    public async Task ReconcileZoneConflictEvents_WhenDisabledDoesNothing()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var manager = CreateSpecialtyEventManager(taskManager: taskManager);
        manager.LoadSpecialtyEventData(connection);

        manager.ReconcileZoneConflictEvents(20, ZoneConflictType.War);

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
        await Assert.That(taskManager.Scheduled).IsEmpty();
    }

    [Test]
    public async Task GetActiveSpecialtyEventIds_FiltersByZoneAndQuotedItem()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtyEventData(connection);
        manager.TryActivateSpecialtyEvent(14, TimeSpan.FromSeconds(30), 7, out _, out _);
        manager.TryActivateSpecialtyEvent(30, TimeSpan.FromSeconds(30), 7, out _, out _);

        var cargoEvents = manager.GetActiveSpecialtyEventIds(8, new uint[] { 43323 });
        var specialtyEvents = manager.GetActiveSpecialtyEventIds(20, new uint[] { 31840 });
        var wrongZoneEvents = manager.GetActiveSpecialtyEventIds(8, new uint[] { 31840 });

        await Assert.That(cargoEvents).IsEquivalentTo(new uint[] { 14 });
        await Assert.That(specialtyEvents).IsEquivalentTo(new uint[] { 30 });
        await Assert.That(wrongZoneEvents).IsEmpty();
    }

    [Test]
    [NotInParallel]
    public async Task SpecialtyEventCommands_ActivateDumpAndDeactivateEvent()
    {
        using var connection = CreateSpecialtyEventDatabase();
        var taskManager = new RecordingTaskManager();
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetAllCharacters().Returns([]);
        var manager = CreateSpecialtyEventManager(
            enableEvents: true,
            taskManager: taskManager,
            worldManager: worldManager.Object);
        manager.LoadSpecialtyEventData(connection);
        var character = new Character(new UnitCustomModelParams()) { Id = 7 };
        var output = Mock.Of<IMessageOutput>();
        var setCommand = new SetSpecialtyEvent(manager);

        setCommand.Execute(character, ["14", "on", "30"], output.Object);
        new DumpSpecialtyEvents(manager).Execute(character, [], output.Object);

        await Assert.That(manager.GetActiveSpecialtyEvents()).HasSingleItem();
        output.SendMessage(
                ChatType.System,
                Is<string>(message => message.Contains("14:") && message.Contains("active until")),
                Any<Color?>())
            .WasCalled(Times.Once);

        setCommand.Execute(character, ["14", "off"], output.Object);

        await Assert.That(manager.GetActiveSpecialtyEvents()).IsEmpty();
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
    [Arguments(150)]
    [Arguments(195)]
    public async Task LoadTradeGoodData_RejectsMissingStockLimit(int contentId)
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, $"DELETE FROM content_configs WHERE id = {contentId}");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments("DELETE FROM content_configs WHERE id = 205")]
    [Arguments("UPDATE content_configs SET value = 0 WHERE id = 205")]
    public async Task LoadTradeGoodData_RejectsInvalidCoinConversionRatio(string update)
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, update);

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadTradeGoodData_RejectsPriceIndexAtStockLimit()
    {
        using var connection = CreateTradeGoodDatabase();
        Execute(connection, "UPDATE tradegood_priceindices SET stock = 200 WHERE stock = 199");

        await Assert.That(() => CreateManager(CreateCargoItem()).LoadTradeGoodData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(197, 0)]
    [Arguments(197, 101)]
    [Arguments(210, 0)]
    public async Task LoadSpecialtySaleData_RejectsInvalidMarketCycleValues(int contentId, int value)
    {
        using var connection = CreateSpecialtySaleDatabase();
        Execute(connection, $"UPDATE content_configs SET value = {value} WHERE id = {contentId}");

        await Assert.That(() => CreateSpecialtySaleManager().LoadSpecialtySaleData(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadSpecialtySaleData_UsesPositiveGoodsRatioCountWithoutRequiringUnusedCargoCount()
    {
        using var connection = CreateSpecialtySaleDatabase();
        Execute(connection, "DELETE FROM content_configs WHERE id = 196; UPDATE content_configs SET value = 5 WHERE id = 210");

        var manager = CreateSpecialtySaleManager();
        manager.LoadSpecialtySaleData(connection);

        await Assert.That(manager).IsNotNull();
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

    private static SpecialtyManager CreateCargoMaterialAdminManager()
    {
        var manager = CreateManager(CreateCargoItem(), itemTags: new Dictionary<uint, uint>
        {
            [31832] = 3361,
            [31833] = 3361,
            [31894] = 3362,
            [49064] = 3363
        });
        var mappings = MarketField<Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>>>(manager, "_specialtyBundleItemsMapped");
        foreach (var itemId in new uint[] { 31832, 31833, 31894, 49064 })
        {
            var bundle = CreateMarketBundle(itemId);
            mappings.Add(itemId, new() { [bundle.SpecialtyBundleId] = bundle });
        }
        return manager;
    }

    private static SpecialtyManager CreateManager(
        BackpackTemplate item,
        bool includePurchaseSkill = true,
        bool includeSaleSkill = true,
        IReadOnlyDictionary<uint, uint> itemTags = null,
        ISpecialtyMarketStore marketStore = null,
        IZoneManager zoneManager = null,
        bool ambiguousMaterialTags = false,
        ITaskManager taskManager = null,
        IWorldManager worldManager = null,
        TimeProvider timeProvider = null,
        IOptions<AppConfiguration> options = null)
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
        if (ambiguousMaterialTags)
            itemManager.HasItemTag(31832, 3362).Returns(true);
        var skillManager = Mock.Of<ISkillManager>();
        skillManager.GetSkillTemplate(SkillsEnum.UseTradeGoodStore).Returns(new SkillTemplate
        {
            Id = SkillsEnum.UseTradeGoodStore,
            MaxRange = 4
        });
        if (includeSaleSkill)
        {
            skillManager.GetSkillTemplate(SkillsEnum.SellBackpack).Returns(new SkillTemplate
            {
                Id = SkillsEnum.SellBackpack,
                MaxRange = 4,
                ConsumeLaborPower = 70,
                ActabilityGroupId = 31
            });
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
            new LocalizationManager(),
            skillManager.Object,
            zoneManager ?? Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter(),
            marketStore ?? CreateMarketStore(),
            Mock.Of<ISpecialtyPurchaseStore>().Object,
            worldManager ?? Mock.Of<IWorldManager>().Object,
            taskManager ?? Mock.Of<ITaskManager>().Object,
            timeProvider ?? TimeProvider.System,
            options ?? CreateOptions());
    }

    private static SpecialtyManager CreateFreshnessManager(params BackpackTemplate[] backpackTemplates)
    {
        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetAllItems().Returns(backpackTemplates.Cast<ItemTemplate>().ToList());
        return new SpecialtyManager(
            itemManager.Object,
            new LocalizationManager(),
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter(),
            CreateMarketStore(),
            Mock.Of<ISpecialtyPurchaseStore>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaskManager>().Object,
            TimeProvider.System,
            CreateOptions());
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
            new LocalizationManager(),
            skillManager.Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter(),
            CreateMarketStore(),
            Mock.Of<ISpecialtyPurchaseStore>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaskManager>().Object,
            TimeProvider.System,
            CreateOptions());
    }

    private static SpecialtyManager CreateSpecialtyEventManager(
        bool includeItemSet = true,
        bool includeItemSetMembers = true,
        bool includeItemSetItems = true,
        bool enableEvents = false,
        ITaskManager taskManager = null,
        IWorldManager worldManager = null,
        TimeProvider timeProvider = null,
        ILocalizationManager localizationManager = null,
        IZoneManager zoneManager = null,
        ISpecialtyMarketStore marketStore = null)
    {
        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetTemplate(43323).Returns(new BackpackTemplate { Id = 43323 });
        if (includeItemSetMembers)
        {
            itemManager.GetTemplate(31840).Returns(new BackpackTemplate { Id = 31840 });
            itemManager.GetTemplate(32085).Returns(new BackpackTemplate { Id = 32085 });
        }
        if (includeItemSet)
        {
            itemManager.GetItemSet(61).Returns(new ItemSet
            {
                Id = 61,
                Items = includeItemSetItems
                    ? new Dictionary<uint, ItemSetItem>
                    {
                        [1] = new() { Id = 1, ItemSetId = 61, ItemId = 31840 },
                        [2] = new() { Id = 2, ItemSetId = 61, ItemId = 32085 }
                    }
                    : new Dictionary<uint, ItemSetItem>()
            });
        }

        var skillManager = Mock.Of<ISkillManager>();
        skillManager.GetSkillTemplate(SkillsEnum.SellBackpack).Returns(new SkillTemplate
        {
            Id = SkillsEnum.SellBackpack,
            MaxRange = 4,
            ConsumeLaborPower = 70,
            ActabilityGroupId = 31
        });
        IZoneManager configuredZoneManager;
        if (zoneManager != null)
        {
            configuredZoneManager = zoneManager;
        }
        else
        {
            var zoneManagerMock = Mock.Of<IZoneManager>();
            zoneManagerMock.GetZoneGroupById(8).Returns(new ZoneGroup { Id = 8, FactionChatRegionId = 2 });
            zoneManagerMock.GetZoneGroupById(20).Returns(new ZoneGroup { Id = 20, FactionChatRegionId = 2 });
            configuredZoneManager = zoneManagerMock.Object;
        }

        return new SpecialtyManager(
            itemManager.Object,
            localizationManager ?? new LocalizationManager(),
            skillManager.Object,
            configuredZoneManager,
            Mock.Of<IMailManager>().Object,
            CreateSaleCommitter(),
            marketStore ?? CreateMarketStore(),
            Mock.Of<ISpecialtyPurchaseStore>().Object,
            worldManager ?? Mock.Of<IWorldManager>().Object,
            taskManager ?? Mock.Of<ITaskManager>().Object,
            timeProvider ?? TimeProvider.System,
            CreateOptions(enableEvents: enableEvents));
    }

    private static TradeGood ConfigureStockEventMarket(SpecialtyManager manager, uint stock)
    {
        var tradeGood = new TradeGood
        {
            Id = 12,
            ItemId = 43323,
            OutputCount = 5,
            Ratio = 1000,
            TradeGoodCategoryId = 1,
            Item = new BackpackTemplate { Id = 43323, Refund = 200000 }
        };
        MarketField<Dictionary<uint, TradeGoodCategory>>(manager, "_tradeGoodCategories")[1] =
            new TradeGoodCategory { Id = 1, Name = "Nuia" };
        MarketField<Dictionary<uint, List<TradeGood>>>(manager, "_tradeGoodsByCategory")[1] = [tradeGood];
        MarketField<Dictionary<(uint CategoryId, uint ItemId), TradeGood>>(
            manager,
            "_tradeGoodsByCategoryAndItem")[(1, 43323)] = tradeGood;
        MarketState(manager).CargoStock[(8, 12)] = stock;
        return tradeGood;
    }

    private static void SetMarketState(SpecialtyManager manager, SpecialtyMarketState state) =>
        typeof(SpecialtyManager).GetField("_market", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, state);

    private sealed class TestZoneManager(params uint[] zoneGroupIds) : IZoneManager
    {
        private readonly Dictionary<uint, ZoneGroup> _groups = zoneGroupIds.ToDictionary(
            x => x,
            x => new ZoneGroup { Id = x });

        public event Action<ushort, ZoneConflictType, ZoneConflictType> ZoneConflictStateChanged;

        public ZoneConflict[] Conflicts { get; init; } = [];

        public void RaiseStateChanged(
            ushort zoneGroupId,
            ZoneConflictType previousState,
            ZoneConflictType currentState) =>
            ZoneConflictStateChanged?.Invoke(zoneGroupId, previousState, currentState);

        public void Load()
        {
        }

        public ZoneConflict[] GetConflicts() => Conflicts;
        public Zone GetZoneById(uint zoneId) => null;
        public Zone GetZoneByKey(uint zoneKey) => null;
        public ZoneGroup GetZoneGroupById(uint zoneId) => _groups.GetValueOrDefault(zoneId);
        public List<uint> GetZoneKeysInZoneGroupById(uint zoneGroupId) => [];
        public uint GetTargetIdByZoneId(uint zoneId) => 0;
        public System.Numerics.Vector2 GetZoneOriginCell(uint zoneId) => default;
        public System.Numerics.Vector3 ConvertToWorldCoordinates(uint zoneId, System.Numerics.Vector3 point) => point;
        public System.Numerics.Vector3 ConvertToLocalCoordinates(uint zoneId, System.Numerics.Vector3 point) => point;
        public bool DoodadHasMatchingClimate(AAEmu.Game.Models.Game.DoodadObj.Doodad doodad) => false;
        public List<Climate> GetClimatesByZone(Zone zone) => [];
    }

    private sealed class TestZoneConflict : ZoneConflict
    {
        public TestZoneConflict(ushort zoneGroupId, ZoneConflictType initialState)
            : base(new ZoneGroup { Id = zoneGroupId })
        {
            ZoneGroupId = zoneGroupId;
            CurrentZoneState = initialState;
        }

        public void ChangeState(ZoneConflictType state) => CurrentZoneState = state;
    }

    private static IOptions<AppConfiguration> CreateOptions(
        bool enableTimedRecovery = false,
        double recoveryIntervalMinutes = 60.0,
        bool enableEvents = false) =>
        Options.Create(new AppConfiguration
        {
            Specialty = new SpecialtyConfig
            {
                EnableTimedRatioRecovery = enableTimedRecovery,
                RatioRecoveryIntervalMinutes = recoveryIntervalMinutes,
                EnableEvents = enableEvents
            }
        });

    private sealed class RecordingTaskManager : ITaskManager
    {
        public List<(AaEmuTask Task, TimeSpan? StartTime, TimeSpan? RepeatInterval)> Scheduled { get; } = [];
        public List<AaEmuTask> Cancelled { get; } = [];
        public Action BeforeSchedule { get; set; }

        public bool Cancel(AaEmuTask task)
        {
            Cancelled.Add(task);
            return true;
        }

        public void Initialize()
        {
        }

        public void Start()
        {
        }

        public System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;

        public bool Schedule(
            AaEmuTask task,
            TimeSpan? startTime = null,
            TimeSpan? repeatInterval = null,
            int count = -1)
        {
            var beforeSchedule = BeforeSchedule;
            BeforeSchedule = null;
            beforeSchedule?.Invoke();
            Scheduled.Add((task, startTime, repeatInterval));
            return true;
        }

        public bool CronSchedule(AaEmuTask task, string cronExpression, TimeSpan? startDelay = null, int count = -1) =>
            throw new NotSupportedException();
    }

    private static SpecialtySaleCommitter CreateSaleCommitter() =>
        new(Mock.Of<ISpecialtySaleStore>().Object);

    private static ISpecialtyMarketStore CreateMarketStore()
    {
        var store = Mock.Of<ISpecialtyMarketStore>();
        store.Commit(Any<SpecialtyMarketWrite>()).Callback((SpecialtyMarketWrite _) => { });
        return store.Object;
    }

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
            INSERT INTO enum_content_configs VALUES (195, 'tradegoods_stock_limit');
            INSERT INTO enum_content_configs VALUES (205, 'tradegoods_coin_per_gold_ratio');
            INSERT INTO enum_content_configs VALUES (206, 'tradegoods_mail_interest');
            INSERT INTO enum_content_configs VALUES (150, 'goods_stock_limit');
            INSERT INTO content_configs VALUES (190, 50);
            INSERT INTO content_configs VALUES (194, 50);
            INSERT INTO content_configs VALUES (195, 200);
            INSERT INTO content_configs VALUES (205, 25000);
            INSERT INTO content_configs VALUES (206, 20);
            INSERT INTO content_configs VALUES (150, 1500);
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
            INSERT INTO enum_content_configs VALUES (150, 'goods_stock_limit');
            INSERT INTO enum_content_configs VALUES (196, 'specialty_price_tradegoods_count');
            INSERT INTO enum_content_configs VALUES (197, 'specialty_price_recover_rate');
            INSERT INTO enum_content_configs VALUES (207, 'specialty_mail_interest');
            INSERT INTO enum_content_configs VALUES (210, 'specialty_goods_ratio_count');
            INSERT INTO content_configs VALUES (8, 130);
            INSERT INTO content_configs VALUES (9, 50);
            INSERT INTO content_configs VALUES (18, 250);
            INSERT INTO content_configs VALUES (60, 8);
            INSERT INTO content_configs VALUES (87, 50);
            INSERT INTO content_configs VALUES (150, 1500);
            INSERT INTO content_configs VALUES (196, 1);
            INSERT INTO content_configs VALUES (197, 25);
            INSERT INTO content_configs VALUES (207, 20);
            INSERT INTO content_configs VALUES (210, 4);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteConnection CreateSpecialtyEventDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE quest_context_groups (id INTEGER PRIMARY KEY);
            CREATE TABLE specialty_event_triggers (
                id INTEGER PRIMARY KEY,
                zone_group_id INTEGER NOT NULL,
                trigger_type INTEGER NOT NULL,
                trigger_value_1 INTEGER NOT NULL,
                trigger_value_2 INTEGER NOT NULL,
                check_time INTEGER NOT NULL,
                event_rate INTEGER NOT NULL,
                event_time INTEGER NOT NULL,
                msg_type INTEGER NULL,
                msg_start TEXT NOT NULL,
                msg_end TEXT NOT NULL,
                trigger_subject_id INTEGER NULL,
                trigger_subject_type TEXT NULL);
            CREATE TABLE specialty_events (
                id INTEGER PRIMARY KEY,
                specialty_event_trigger_id INTEGER NOT NULL,
                event_type INTEGER NOT NULL,
                event_value INTEGER NOT NULL,
                event_object_id INTEGER NULL,
                event_object_type TEXT NULL,
                tooltip_text TEXT NULL);

            INSERT INTO quest_context_groups VALUES (77);
            INSERT INTO specialty_event_triggers VALUES
                (1, 8, 1, 160, 200, 43000, 124, 3600, 2, 'stock start', 'stock end', 43323, 'Item'),
                (3, 8, 1, 200, 200, 43000, 0, 3600, NULL, '', '', 43323, 'Item'),
                (28, 20, 4, 0, 0, 5460, 0, 5400, 2, 'war start', 'war end', 6, 'EnumHonorPointWarState'),
                (34, 8, 2, 0, 0, 60, 0, 60, NULL, '', '', 77, 'QuestContextGroup');
            INSERT INTO specialty_events VALUES
                (14, 1, 2, 750, 43323, 'Item', 'sale rate'),
                (30, 28, 3, 1500, 61, 'ItemSet', 'overcharge rate');
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
