using System.Reflection;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.World.Zones;
using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public partial class SpecialtyManagerTests
{
    [Test]
    [Arguments("delivery", false)]
    [Arguments("delivery", true)]
    [Arguments("materials", false)]
    [Arguments("materials", true)]
    [Arguments("consume", false)]
    [Arguments("consume", true)]
    [Arguments("recovery", false)]
    [Arguments("recovery", true)]
    public async Task MarketMutation_CommitFailure_DoesNotPublishAnyStagedChanges(string operation, bool conflict)
    {
        var store = new InMemoryMarketStore(CreatePersistedMarket());
        var manager = CreateRestoredMarketManager(store);
        var live = MarketState(manager);
        var before = store.Load();
        store.CommitFailure = conflict ? new SpecialtyMarketConflictException() : new IOException("Commit failed");

        if (operation == "materials")
        {
            var result = manager.AddTradeGoodMaterials(8, 1, null);
            await Assert.That(result.Success).IsFalse();
        }
        else
        {
            Action mutate = operation switch
            {
                "delivery" => () => manager.RecordTradeGoodDelivery(8, 1, 31832),
                "consume" => () => manager.TryConsumeTradeGoodCargo(8, 12),
                _ => () => manager.RecoverTimedRatios()
            };
            if (conflict)
                await Assert.That(mutate).Throws<SpecialtyMarketConflictException>();
            else
                await Assert.That(mutate).Throws<IOException>();
        }

        await Assert.That(store.CommitAttempts).IsEqualTo(1);
        if (conflict)
            await Assert.That(MarketState(manager)).IsNotSameReferenceAs(live);
        else
            await Assert.That(MarketState(manager)).IsSameReferenceAs(live);
        await AssertMarketEquals(MarketState(manager), before);
        await AssertMarketEquals(store.Load(), before);
        await AssertMarketEquals(MarketState(CreateRestoredMarketManager(store)), before);
    }

    [Test]
    public async Task RecoverTimedRatios_RecoversEveryPersistedRouteInOneDurableRevision()
    {
        var seed = new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new()
            {
                [31832] = new() { [8] = 10000 },
                [31833] = new() { [8] = 12998 },
                [31894] = new() { [8] = 11000 },
                [49064] = new() { [8] = 13000 }
            },
            Records = new()
            {
                [(31832, 8)] = [new SpecialtyMarketRecord(1000, 10)],
                [(31833, 8)] = [new SpecialtyMarketRecord(1299, 10)],
                [(31894, 8)] = [new SpecialtyMarketRecord(1100, 10)],
                [(49064, 8)] = [new SpecialtyMarketRecord(1300, 10)]
            }
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);

        var changed = manager.RecoverTimedRatios();

        await Assert.That(changed).IsTrue();
        await Assert.That(store.CommitAttempts).IsEqualTo(1);
        var recovered = store.Load();
        await Assert.That(recovered.Revision).IsEqualTo(8L);
        await Assert.That(recovered.PriceRatios[31832][8]).IsEqualTo(10750);
        await Assert.That(recovered.PriceRatios[31833][8]).IsEqualTo(12999);
        await Assert.That(recovered.PriceRatios[31894][8]).IsEqualTo(11500);
        await Assert.That(recovered.PriceRatios[49064][8]).IsEqualTo(13000);
        await Assert.That(recovered.Records[(31832, 8)].Select(x => x.Ratio)).IsEquivalentTo(new[] { 1000, 1075 });
        await Assert.That(recovered.Records[(31833, 8)].Count).IsEqualTo(1);
        await Assert.That(recovered.Records[(31894, 8)].Select(x => x.Ratio)).IsEquivalentTo(new[] { 1100, 1150 });
        await Assert.That(recovered.Records[(49064, 8)].Count).IsEqualTo(1);
        await AssertMarketEquals(MarketState(CreateRestoredMarketManager(store)), recovered);
    }

    [Test]
    public async Task RecoverTimedRatios_NoChangedRouteDoesNotCommit()
    {
        var seed = new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new() { [31832] = new() { [8] = 13000 } },
            Records = new() { [(31832, 8)] = [new SpecialtyMarketRecord(1300, 10)] }
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);
        var live = MarketState(manager);

        var changed = manager.RecoverTimedRatios();

        await Assert.That(changed).IsFalse();
        await Assert.That(store.CommitAttempts).IsEqualTo(0);
        await Assert.That(MarketState(manager)).IsSameReferenceAs(live);
        await AssertMarketEquals(MarketState(manager), seed);
    }

    [Test]
    public async Task RecoverTimedRatios_AdvancesAcrossFinalRoundingBoundary()
    {
        var store = new InMemoryMarketStore(new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new() { [31832] = new() { [8] = 12999 } },
            Records = new() { [(31832, 8)] = [new SpecialtyMarketRecord(1299, 10)] }
        });
        var manager = CreateRestoredMarketManager(store);

        await Assert.That(manager.RecoverTimedRatios()).IsTrue();

        var recovered = store.Load();
        await Assert.That(recovered.Revision).IsEqualTo(8L);
        await Assert.That(recovered.PriceRatios[31832][8]).IsEqualTo(13000);
        await Assert.That(recovered.Records[(31832, 8)].Select(x => x.Ratio))
            .IsEquivalentTo(new[] { 1299, 1300 });
    }

    [Test]
    public async Task RecoverTimedRatios_StacksWithCargoProductionRecovery()
    {
        var seed = new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new()
            {
                [31832] = new() { [8] = 9000 },
                [31894] = new() { [8] = 10000 }
            },
            MaterialContributions = CreateMaterialContributions(
                (8, 3361, 31832, 49),
                (8, 3362, 31894, 30),
                (8, 3363, 49064, 10)),
            Records = new()
            {
                [(31832, 8)] = [new SpecialtyMarketRecord(900, 10)],
                [(31894, 8)] = [new SpecialtyMarketRecord(1000, 10)]
            }
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);

        await Assert.That(manager.RecoverTimedRatios()).IsTrue();
        await Assert.That(manager.RecordTradeGoodDelivery(8, 1, 31832)).IsEqualTo(5u);

        var recovered = store.Load();
        await Assert.That(recovered.Revision).IsEqualTo(9L);
        await Assert.That(recovered.PriceRatios[31832][8]).IsEqualTo(12288);
        await Assert.That(recovered.PriceRatios[31894][8]).IsEqualTo(12466);
    }

    [Test]
    public async Task RecordTradeGoodDelivery_MaterialsAndConvertedCargoSurviveRestart()
    {
        var store = new InMemoryMarketStore();
        var manager = CreateRestoredMarketManager(store);
        var grant = manager.AddTradeGoodMaterials(8, 1, [49, 31, 11]);
        await Assert.That(grant.Success).IsTrue();
        await Assert.That(grant.Produced).IsEqualTo(0u);

        var restarted = CreateRestoredMarketManager(store);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(49u);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3362)).IsEqualTo(31u);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3363)).IsEqualTo(11u);
        await Assert.That(restarted.GetTradeGoodCargoStock(8, 12)).IsEqualTo(0u);
        var previousSnapshot = MarketState(restarted);

        await Assert.That(restarted.RecordTradeGoodDelivery(8, 1, 31832)).IsEqualTo(5u);
        await Assert.That(previousSnapshot.MaterialContributions[(8, 3361)][0].Amount).IsEqualTo(49u);
        await Assert.That(previousSnapshot.CargoStock.Count).IsEqualTo(0);

        var afterConversion = CreateRestoredMarketManager(store);
        await Assert.That(afterConversion.GetTradeGoodCargoStock(8, 12)).IsEqualTo(5u);
        await Assert.That(afterConversion.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(0u);
        await Assert.That(afterConversion.GetTradeGoodMaterialStock(8, 3362)).IsEqualTo(1u);
        await Assert.That(afterConversion.GetTradeGoodMaterialStock(8, 3363)).IsEqualTo(1u);
        await Assert.That(afterConversion.GetTradeGoodCargoStock(9, 12)).IsEqualTo(0u);
        await Assert.That(MarketState(afterConversion).Revision).IsEqualTo(2L);
        await AssertMarketEquals(MarketState(afterConversion), MarketState(restarted));
    }

    [Test]
    public async Task AddTradeGoodMaterials_ConversionAndRemaindersSurviveRestart()
    {
        var store = new InMemoryMarketStore();
        var manager = CreateRestoredMarketManager(store);

        var grant = manager.AddTradeGoodMaterials(8, 1, [101, 61, 21]);

        await Assert.That(grant.Success).IsTrue();
        await Assert.That(grant.Produced).IsEqualTo(10u);
        var restarted = CreateRestoredMarketManager(store);
        await Assert.That(restarted.GetTradeGoodCargoStock(8, 12)).IsEqualTo(10u);
        foreach (var tag in new uint[] { 3361, 3362, 3363 })
            await Assert.That(restarted.GetTradeGoodMaterialStock(8, tag)).IsEqualTo(1u);
        await AssertMarketEquals(MarketState(restarted), MarketState(manager));
    }

    [Test]
    public async Task TryConsumeTradeGoodCargo_DurableClaimRejectsStaleManagerAndEmptyRestart()
    {
        var store = new InMemoryMarketStore(CreatePersistedMarket());
        var manager = CreateRestoredMarketManager(store);
        var staleManager = CreateRestoredMarketManager(store);
        var staleSnapshot = store.Load();

        await Assert.That(manager.TryConsumeTradeGoodCargo(8, 12)).IsTrue();
        await Assert.That(manager.GetTradeGoodCargoStock(8, 12)).IsEqualTo(0u);
        await Assert.That(() => staleManager.TryConsumeTradeGoodCargo(8, 12))
            .Throws<SpecialtyMarketConflictException>();
        await Assert.That(MarketState(staleManager).Revision).IsEqualTo(staleSnapshot.Revision + 1);
        await Assert.That(MarketState(staleManager).CargoStock[(8, 12)]).IsEqualTo(0u);
        await Assert.That(store.Load().CargoStock[(8, 12)]).IsEqualTo(0u);

        var restarted = CreateRestoredMarketManager(store);
        await Assert.That(restarted.GetTradeGoodCargoStock(8, 12)).IsEqualTo(0u);
        var attempts = store.CommitAttempts;
        await Assert.That(restarted.TryConsumeTradeGoodCargo(8, 12)).IsFalse();
        await Assert.That(manager.TryConsumeTradeGoodCargo(8, 12)).IsFalse();
        await Assert.That(staleManager.TryConsumeTradeGoodCargo(8, 12)).IsFalse();
        await Assert.That(store.CommitAttempts).IsEqualTo(attempts);
        await AssertMarketEquals(MarketState(restarted), MarketState(manager));
    }

    [Test]
    public async Task RestoreMarketState_AcceptsRouteServedByZoneAgnosticOutlet()
    {
        var store = new InMemoryMarketStore(CreatePersistedMarket());
        var manager = CreateRestoredMarketManager(store);
        MarketField<Dictionary<uint, SpecialtyNpc>>(manager, "_specialtyNpcs")[1].ZoneGroupId = 0;

        manager.RestoreMarketState();

        await AssertMarketEquals(MarketState(manager), store.Load());
    }

    [Test]
    [Arguments(1, 10000, 1)]
    [Arguments(3, 10000, 3)]
    [Arguments(4, 9750, 0)]
    [Arguments(5, 9750, 1)]
    [Arguments(8, 9500, 0)]
    public async Task PrepareSaleMarketWrite_DecreasesOncePerFourCommittedDeliveries(
        int deliveries,
        int expectedRatioUnits,
        int expectedRemainder)
    {
        var store = new InMemoryMarketStore(new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new() { [31832] = new() { [8] = 10000 } },
            DemandRemainders = new() { [31832] = new() { [8] = 0 } },
            Records = new() { [(31832, 8)] = [new SpecialtyMarketRecord(1000, 10)] }
        });

        for (var delivery = 0; delivery < deliveries; delivery++)
        {
            var manager = CreateRestoredMarketManager(store);
            store.Commit(manager.PrepareSaleMarketWrite(31832, 8));
        }

        var committed = store.Load();
        await Assert.That(committed.PriceRatios[31832][8]).IsEqualTo(expectedRatioUnits);
        await Assert.That(committed.DemandRemainders[31832][8]).IsEqualTo(expectedRemainder);
        await Assert.That(committed.MaterialContributions[(8, 3361)][0].Amount).IsEqualTo((uint)deliveries);
        await Assert.That(committed.Records[(31832, 8)].Last().Ratio).IsEqualTo(expectedRatioUnits / 10);
    }

    [Test]
    public async Task PrepareSaleMarketWrite_RecoveryCompoundsProducedCargoAfterDemandDecrease()
    {
        var seed = new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new()
            {
                [31832] = new() { [8] = 9000 },
                [31894] = new() { [8] = 10000 }
            },
            DemandRemainders = new()
            {
                [31832] = new() { [8] = 3 },
                [31894] = new() { [8] = 0 }
            },
            MaterialContributions = CreateMaterialContributions(
                (8, 3361, 31832, 49),
                (8, 3362, 31894, 31),
                (8, 3363, 49064, 11)),
            Records = new()
            {
                [(31832, 8)] = [new SpecialtyMarketRecord(900, 10)],
                [(31894, 8)] = [new SpecialtyMarketRecord(1000, 10)]
            }
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);

        var write = manager.PrepareSaleMarketWrite(31832, 8);

        await Assert.That(write.Updated.DemandRemainders[31832][8]).IsEqualTo(0);
        await Assert.That(write.Updated.CargoStock[(8, 12)]).IsEqualTo(5u);
        await Assert.That(write.Updated.PriceRatios[31832][8]).IsEqualTo(11991);
        await Assert.That(write.Updated.PriceRatios[31894][8]).IsEqualTo(12288);
        await Assert.That(write.Updated.Records[(31832, 8)].Select(x => x.Ratio))
            .IsEquivalentTo(new[] { 900, 875, 1199 });
        await Assert.That(write.Updated.Records[(31894, 8)].Select(x => x.Ratio))
            .IsEquivalentTo(new[] { 1000, 1228 });
        await AssertMarketEquals(MarketState(manager), seed);
    }

    [Test]
    public async Task BuildSellQuote_UsesFractionalRatioAndIndependentDurableItemStock()
    {
        var store = new InMemoryMarketStore(new SpecialtyMarketState
        {
            Revision = 7,
            PriceRatios = new() { [31832] = new() { [8] = 12051 } },
            DemandRemainders = new() { [31832] = new() { [8] = 3 } },
            MaterialContributions = new()
            {
                [(8, 3361)] =
                [
                    new SpecialtyMaterialContribution(1, 31832, 49),
                    new SpecialtyMaterialContribution(2, 31833, 7)
                ]
            },
            Records = new() { [(31832, 8)] = [new SpecialtyMarketRecord(1205, 10)] }
        });
        var manager = CreateRestoredMarketManager(store);

        var quote = manager.BuildSellQuote(CreateMarketBundle(), 8);

        await Assert.That(quote.Refund).IsEqualTo(120510ul);
        await Assert.That(quote.Ratio).IsEqualTo(120u);
        await Assert.That(quote.Stock).IsEqualTo(49u);
        await Assert.That(manager.BuildSellQuote(CreateMarketBundle(31833), 8).Stock).IsEqualTo(7u);
        await Assert.That(manager.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(56u);
    }

    [Test]
    public async Task RecipeConversion_ConsumesInterleavedItemContributionsInFifoOrderAcrossRestart()
    {
        var seed = new SpecialtyMarketState
        {
            MaterialContributions = new()
            {
                [(8, 3361)] =
                [
                    new SpecialtyMaterialContribution(1, 31832, 30),
                    new SpecialtyMaterialContribution(2, 31833, 30),
                    new SpecialtyMaterialContribution(3, 31832, 5)
                ],
                [(8, 3362)] = [new SpecialtyMaterialContribution(1, 31894, 30)],
                [(8, 3363)] = [new SpecialtyMaterialContribution(1, 49064, 9)]
            }
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);

        await Assert.That(manager.RecordTradeGoodDelivery(8, 1, 49064)).IsEqualTo(5u);

        var restarted = CreateRestoredMarketManager(store);
        var regularQueue = MarketState(restarted).MaterialContributions[(8, 3361)];
        await Assert.That(regularQueue.Count).IsEqualTo(2);
        await Assert.That((regularQueue[0].Sequence, regularQueue[0].ItemId, regularQueue[0].Amount))
            .IsEqualTo((2UL, 31833u, 10u));
        await Assert.That((regularQueue[1].Sequence, regularQueue[1].ItemId, regularQueue[1].Amount))
            .IsEqualTo((3UL, 31832u, 5u));
        await Assert.That(restarted.BuildSellQuote(CreateMarketBundle(31832), 8).Stock).IsEqualTo(5u);
        await Assert.That(restarted.BuildSellQuote(CreateMarketBundle(31833), 8).Stock).IsEqualTo(10u);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3361)).IsEqualTo(15u);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3362)).IsEqualTo(0u);
        await Assert.That(restarted.GetTradeGoodMaterialStock(8, 3363)).IsEqualTo(0u);
    }

    [Test]
    public async Task PrepareSaleMarketWrite_StagesInitialRatioCountHistoryAndConversionWithoutPublishing()
    {
        var seed = new SpecialtyMarketState
        {
            Revision = 7,
            MaterialContributions = CreateMaterialContributions(
                (8, 3361, 31832, 49),
                (8, 3362, 31894, 31),
                (8, 3363, 49064, 11))
        };
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store);
        var live = MarketState(manager);
        var bundle = CreateMarketBundle();

        await Assert.That(manager.BuildSellQuote(bundle, 8).Ratio).IsEqualTo(130u);
        await Assert.That(manager.BuildSellQuote(bundle, 8).Stock).IsEqualTo(49u);
        await Assert.That(manager.BuildSellQuote(bundle, 8).Ratio).IsEqualTo(130u);
        await Assert.That(live.PriceRatios.Count).IsEqualTo(0);
        await Assert.That(live.Records.Count).IsEqualTo(0);
        await AssertMarketEquals(live, seed);

        var write = manager.PrepareSaleMarketWrite(31832, 8);
        var duplicate = manager.PrepareSaleMarketWrite(31832, 8);

        await Assert.That(write.Expected).IsSameReferenceAs(live);
        await Assert.That(write.Updated.Revision).IsEqualTo(8L);
        await Assert.That(write.Updated.PriceRatios[31832][8]).IsEqualTo(13000);
        await Assert.That(write.Updated.DemandRemainders[31832][8]).IsEqualTo(1);
        await Assert.That(write.Updated.Records[(31832, 8)].Count).IsEqualTo(1);
        await Assert.That(write.Updated.Records[(31832, 8)][0].Ratio).IsEqualTo(1300);
        await Assert.That(write.Updated.CargoStock[(8, 12)]).IsEqualTo(5u);
        await Assert.That(write.Updated.MaterialContributions.ContainsKey((8, 3361))).IsFalse();
        await Assert.That(write.Updated.MaterialContributions[(8, 3362)][0].Amount).IsEqualTo(1u);
        await Assert.That(write.Updated.MaterialContributions[(8, 3363)][0].Amount).IsEqualTo(1u);
        await Assert.That(duplicate.Updated.DemandRemainders[31832][8]).IsEqualTo(1);
        await Assert.That(store.CommitAttempts).IsEqualTo(0);
        await Assert.That(MarketState(manager)).IsSameReferenceAs(live);
        await AssertMarketEquals(live, seed);
        await AssertMarketEquals(store.Load(), seed);

        // The sale transaction owns persistence; staging must not publish even after an external commit.
        store.Commit(write);
        await Assert.That(() => store.Commit(duplicate)).Throws<SpecialtyMarketConflictException>();
        await AssertMarketEquals(MarketState(manager), seed);
        await AssertMarketEquals(MarketState(CreateRestoredMarketManager(store)), write.Updated);
    }

    [Test]
    [Arguments("material")]
    [Arguments("cargo")]
    [Arguments("sequence")]
    [Arguments("ambiguous-tags")]
    public async Task PrepareSaleMarketWrite_InvalidDeliveryThrowsWithoutDroppingCountsOrChangingCache(string failure)
    {
        var seed = CreatePersistedMarket();
        switch (failure)
        {
            case "material":
                seed.MaterialContributions[(8, 3361)] = [new SpecialtyMaterialContribution(1, 31832, uint.MaxValue)];
                seed.MaterialContributions.Remove((8, 3362));
                break;
            case "cargo": seed.CargoStock[(8, 12)] = uint.MaxValue; break;
            case "sequence": seed.MaterialContributions[(8, 3361)] = [new SpecialtyMaterialContribution(ulong.MaxValue, 31833, 49)]; break;
            case "ambiguous-tags": seed.MaterialContributions.Remove((8, 3361)); break;
        }
        var store = new InMemoryMarketStore(seed);
        var manager = CreateRestoredMarketManager(store, ambiguousMaterialTags: failure == "ambiguous-tags");
        var live = MarketState(manager);

        if (failure == "ambiguous-tags")
            await Assert.That(() => manager.PrepareSaleMarketWrite(31832, 8)).Throws<InvalidDataException>();
        else
            await Assert.That(() => manager.PrepareSaleMarketWrite(31832, 8)).Throws<OverflowException>();

        await Assert.That(store.CommitAttempts).IsEqualTo(0);
        await Assert.That(MarketState(manager)).IsSameReferenceAs(live);
        await AssertMarketEquals(live, seed);
        await AssertMarketEquals(store.Load(), seed);
    }

    [Test]
    [Arguments("item")]
    [Arguments("destination")]
    [Arguments("bundle")]
    [Arguments("ratio-low")]
    [Arguments("ratio-high")]
    [Arguments("material-zone")]
    [Arguments("material-category")]
    [Arguments("material-tag")]
    [Arguments("material-item")]
    [Arguments("material-complete-recipe")]
    [Arguments("cargo-zone")]
    [Arguments("cargo-category")]
    [Arguments("cargo-recipe")]
    public async Task RestoreMarketState_RejectsInvalidAuthoredReferencesWithoutReplacingCache(string invalidReference)
    {
        var store = new InMemoryMarketStore(CreatePersistedMarket());
        var manager = CreateRestoredMarketManager(store);
        var live = MarketState(manager);
        var before = store.Load();
        var invalid = store.Load();
        invalid.Revision++;
        switch (invalidReference)
        {
            case "item": invalid.PriceRatios[99999] = new() { [8] = 100 }; break;
            case "destination": invalid.PriceRatios[31832][9] = 100; break;
            case "bundle":
                MarketField<Dictionary<uint, SpecialtyNpc>>(manager, "_specialtyNpcs")[1].SpecialtyBundleId = 99;
                break;
            case "ratio-low": invalid.PriceRatios[31832][8] = 4999; break;
            case "ratio-high": invalid.PriceRatios[31832][8] = 13001; break;
            case "material-zone": invalid.MaterialContributions[(99, 3361)] = [new(1, 31832, 1)]; break;
            case "material-category": invalid.MaterialContributions[(9, 3361)] = [new(1, 31832, 1)]; break;
            case "material-tag": invalid.MaterialContributions[(8, 99999)] = [new(1, 31832, 1)]; break;
            case "material-item": invalid.MaterialContributions[(8, 3361)] = [new(1, 31894, 1)]; break;
            case "material-complete-recipe":
                invalid.MaterialContributions.Clear();
                foreach (var contribution in CreateMaterialContributions(
                    (8, 3361, 31832, 50),
                    (8, 3362, 31894, 30),
                    (8, 3363, 49064, 10)))
                    invalid.MaterialContributions.Add(contribution.Key, contribution.Value);
                break;
            case "cargo-zone": invalid.CargoStock[(99, 12)] = 1; break;
            case "cargo-category": invalid.CargoStock[(9, 12)] = 1; break;
            case "cargo-recipe": invalid.CargoStock[(8, 99999)] = 1; break;
        }
        store.Commit(new SpecialtyMarketWrite(before, invalid));

        await Assert.That(() => manager.RestoreMarketState()).Throws<InvalidDataException>();

        await Assert.That(MarketState(manager)).IsSameReferenceAs(live);
        await AssertMarketEquals(live, before);
        await AssertMarketEquals(store.Load(), invalid);
    }

    private static SpecialtyManager CreateRestoredMarketManager(InMemoryMarketStore store, bool ambiguousMaterialTags = false)
    {
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneGroupById(8).Returns(new ZoneGroup { Id = 8, FactionChatRegionId = 2 });
        zones.GetZoneGroupById(9).Returns(new ZoneGroup { Id = 9, FactionChatRegionId = 3 });
        var manager = CreateManager(CreateCargoItem(),
            itemTags: new Dictionary<uint, uint>
            {
                [31832] = 3361,
                [31833] = 3361,
                [31894] = 3362,
                [49064] = 3363
            },
            marketStore: store, zoneManager: zones.Object, ambiguousMaterialTags: ambiguousMaterialTags);
        using var tradeGoods = CreateTradeGoodDatabase();
        // Load another valid category so wrong-region tests exercise membership, not just missing data.
        Execute(tradeGoods, """
            INSERT INTO tradegood_categories VALUES (2, 'Haranya');
            INSERT INTO tradegoods VALUES (13, 43323, 5, 1000, 0, 2, 104);
            INSERT INTO tradegood_materials VALUES (1301, 13, 4361, 50);
            """);
        manager.LoadTradeGoodData(tradeGoods);
        using var specialty = CreateSpecialtySaleDatabase();
        manager.LoadSpecialtySaleData(specialty);
        var bundle = CreateMarketBundle();
        var sameMaterialBundle = CreateMarketBundle(31833);
        var otherBundle = CreateMarketBundle(31894);
        var rareBundle = CreateMarketBundle(49064);
        var mappings = MarketField<Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>>>(manager, "_specialtyBundleItemsMapped");
        mappings.Add(bundle.ItemId, new() { [bundle.SpecialtyBundleId] = bundle });
        mappings.Add(sameMaterialBundle.ItemId, new() { [sameMaterialBundle.SpecialtyBundleId] = sameMaterialBundle });
        mappings.Add(otherBundle.ItemId, new() { [otherBundle.SpecialtyBundleId] = otherBundle });
        mappings.Add(rareBundle.ItemId, new() { [rareBundle.SpecialtyBundleId] = rareBundle });
        MarketField<Dictionary<uint, SpecialtyNpc>>(manager, "_specialtyNpcs").Add(1,
            new SpecialtyNpc { Id = 1, NpcId = 1, SpecialtyBundleId = bundle.SpecialtyBundleId, ZoneGroupId = 8 });
        manager.RestoreMarketState();
        return manager;
    }

    private static SpecialtyBundleItem CreateMarketBundle(uint itemId = 31832) => new()
    {
        Id = itemId, ItemId = itemId, SpecialtyBundleId = 1, Ratio = 1000,
        Item = new BackpackTemplate { Id = itemId, Refund = 100000 }
    };

    private static SpecialtyMarketState CreatePersistedMarket() => new()
    {
        Revision = 7,
        PriceRatios = new() { [31832] = new() { [8] = 10000 } },
        DemandRemainders = new() { [31832] = new() { [8] = 3 } },
        MaterialContributions = CreateMaterialContributions(
            (8, 3361, 31832, 49),
            (8, 3362, 31894, 31),
            (8, 3363, 49064, 11)),
        CargoStock = new() { [(8, 12)] = 1 },
        Records = new() { [(31832, 8)] = [new SpecialtyMarketRecord(1000, 10)] }
    };

    private static T MarketField<T>(SpecialtyManager manager, string name) =>
        (T)typeof(SpecialtyManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

    private static SpecialtyMarketState MarketState(SpecialtyManager manager) => MarketField<SpecialtyMarketState>(manager, "_market");

    private static async Task AssertMarketEquals(SpecialtyMarketState actual, SpecialtyMarketState expected)
    {
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.PriceRatios.Keys).IsEquivalentTo(expected.PriceRatios.Keys);
        foreach (var (itemId, ratios) in expected.PriceRatios)
            await Assert.That(actual.PriceRatios[itemId]).IsEquivalentTo(ratios);
        await Assert.That(actual.DemandRemainders.Keys).IsEquivalentTo(expected.DemandRemainders.Keys);
        foreach (var (itemId, counts) in expected.DemandRemainders)
            await Assert.That(actual.DemandRemainders[itemId]).IsEquivalentTo(counts);
        await Assert.That(actual.MaterialContributions.Keys).IsEquivalentTo(expected.MaterialContributions.Keys);
        foreach (var (key, contributions) in expected.MaterialContributions)
        {
            var actualContributions = actual.MaterialContributions[key];
            await Assert.That(actualContributions.Select(x => (x.Sequence, x.ItemId, x.Amount)))
                .IsEquivalentTo(contributions.Select(x => (x.Sequence, x.ItemId, x.Amount)));
        }
        await Assert.That(actual.CargoStock).IsEquivalentTo(expected.CargoStock);
        await Assert.That(actual.Records.Keys).IsEquivalentTo(expected.Records.Keys);
        foreach (var (route, records) in expected.Records)
        {
            await Assert.That(actual.Records[route].Count).IsEqualTo(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                await Assert.That(actual.Records[route][i].Ratio).IsEqualTo(records[i].Ratio);
                await Assert.That(actual.Records[route][i].Recorded).IsEqualTo(records[i].Recorded);
            }
        }
    }

    private static Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>>
        CreateMaterialContributions(params (uint ZoneGroupId, uint TagId, uint ItemId, uint Amount)[] entries)
    {
        var result = new Dictionary<(uint, uint), List<SpecialtyMaterialContribution>>();
        foreach (var entry in entries)
        {
            var key = (entry.ZoneGroupId, entry.TagId);
            if (!result.TryGetValue(key, out var contributions))
            {
                contributions = [];
                result.Add(key, contributions);
            }
            contributions.Add(new SpecialtyMaterialContribution((ulong)contributions.Count + 1, entry.ItemId, entry.Amount));
        }
        return result;
    }

    private sealed class InMemoryMarketStore(SpecialtyMarketState initial = null) : ISpecialtyMarketStore
    {
        private readonly object _lock = new();
        private SpecialtyMarketState _persisted = (initial ?? new SpecialtyMarketState()).Clone();
        public Exception CommitFailure { get; set; }
        public int CommitAttempts { get; private set; }

        public SpecialtyMarketState Load()
        {
            lock (_lock)
                return _persisted.Clone();
        }

        public void Commit(SpecialtyMarketWrite write)
        {
            lock (_lock)
            {
                CommitAttempts++;
                if (CommitFailure != null)
                    throw CommitFailure;
                if (write.Expected.Revision != _persisted.Revision)
                    throw new SpecialtyMarketConflictException();
                if (write.Updated.Revision != checked(write.Expected.Revision + 1))
                    throw new InvalidOperationException("A market write must advance exactly one revision.");
                _persisted = write.Updated.Clone();
            }
        }

        public void Apply(MySqlConnection connection, MySqlTransaction transaction, SpecialtyMarketWrite write) =>
            throw new NotSupportedException("Manager tests do not open database transactions.");
    }
}
