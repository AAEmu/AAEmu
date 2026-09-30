using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class HousingManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockObjectId = Mock.Of<IObjectIdManager>();
        var mockFaction = Mock.Of<IFactionManager>();
        var mockLocale = Mock.Of<ILocalizationManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var mockSkill = Mock.Of<ISkillManager>();
        var mockHousingId = Mock.Of<IHousingIdManager>();
        var mockHousingTld = Mock.Of<IHousingTldManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockMail = Mock.Of<IMailManager>();
        var mockName = Mock.Of<INameManager>();
        var mockZone = Mock.Of<IZoneManager>();
        var mockDoodad = Mock.Of<IDoodadManager>();
        var mockUcc = Mock.Of<IUccManager>();
        var mockButler = Mock.Of<IButlerManager>();
        var mockDominion = Mock.Of<IDominionManager>();
        var mockGuildDominion = Mock.Of<IGuildDominionManager>();

        var manager = new HousingManager(
            mockObjectId.Object,
            mockFaction.Object,
            mockLocale.Object,
            mockWorld.Object,
            mockTask.Object,
            mockSkill.Object,
            mockHousingId.Object,
            mockHousingTld.Object,
            mockItem.Object,
            mockMail.Object,
            mockName.Object,
            mockZone.Object,
            mockDoodad.Object,
            mockUcc.Object,
            mockButler.Object,
            mockDominion.Object,
            mockGuildDominion.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockObjectId);
        Mock.VerifyNoOtherCalls(mockFaction);
        Mock.VerifyNoOtherCalls(mockLocale);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockSkill);
        Mock.VerifyNoOtherCalls(mockHousingId);
        Mock.VerifyNoOtherCalls(mockHousingTld);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockMail);
        Mock.VerifyNoOtherCalls(mockName);
        Mock.VerifyNoOtherCalls(mockZone);
        Mock.VerifyNoOtherCalls(mockDoodad);
        Mock.VerifyNoOtherCalls(mockUcc);
        Mock.VerifyNoOtherCalls(mockButler);
        Mock.VerifyNoOtherCalls(mockDominion);
        Mock.VerifyNoOtherCalls(mockGuildDominion);
    }

    [Test]
    public async Task GetHouseAtLocation_IgnoresOverlappingHouseFromAnotherWorldInstance()
    {
        var manager = CreateManager();
        var worldTemplate = new WorldTemplate { Name = "instanced_world" };
        var requestedWorld = new WorldInstance(worldTemplate, 0, true, 10);
        var otherWorld = new WorldInstance(worldTemplate, 0, true, 11);
        var otherHouse = CreateHouse(otherWorld, 1, 100f, 200f);
        var requestedHouse = CreateHouse(requestedWorld, 2, 100f, 200f);
        SetPrivateField(manager, "_houses", new Dictionary<uint, House>
        {
            [otherHouse.Id] = otherHouse,
            [requestedHouse.Id] = requestedHouse
        });

        var result = manager.GetHouseAtLocation(requestedWorld, 100f, 200f);

        await Assert.That(result).IsSameReferenceAs(requestedHouse);
    }

    [Test]
    public async Task GetHouseAtLocation_NullWorldDoesNotMatchUninitializedHouse()
    {
        var manager = CreateManager();
        var house = CreateHouse(null, 1, 100f, 200f);
        SetPrivateField(manager, "_houses", new Dictionary<uint, House> { [house.Id] = house });

        var result = manager.GetHouseAtLocation(null, 100f, 200f);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareOwnershipTransfer_DoesNotUnbindSellerWhenPaymentFails()
    {
        var unbindCalled = false;
        var refundCalled = false;

        var result = HousingManager.PrepareOwnershipTransfer(
            () => false,
            () => unbindCalled = true,
            () => refundCalled = true);

        await Assert.That(result).IsEqualTo(HousingManager.HousePurchasePreparation.PaymentFailed);
        await Assert.That(unbindCalled).IsFalse();
        await Assert.That(refundCalled).IsFalse();
    }

    [Test]
    public async Task PrepareOwnershipTransfer_RefundsBuyerWhenDurableUnbindFails()
    {
        var refundCalled = false;

        var result = HousingManager.PrepareOwnershipTransfer(
            () => true,
            () => false,
            () => refundCalled = true);

        await Assert.That(result).IsEqualTo(HousingManager.HousePurchasePreparation.ButlerUnbindFailed);
        await Assert.That(refundCalled).IsTrue();
    }

    [Test]
    public async Task CalculateBuildingTaxInfo_FirstHouseOfTheAccount_PaysWeeklyTaxAndDeposit()
    {
        // Scarecrow Garden (design 267): taxation 8, 50000 per week.
        var manager = CreateManager();
        SetPrivateField(manager, "_houses", new Dictionary<uint, House>());
        var garden = TaxedTemplate(267, 50_000);

        var ok = manager.CalculateBuildingTaxInfo(AccountId, garden, true,
            out var total, out var heavy, out var normal, out _, out var weekly);

        // One week up front plus the two-week deposit that demolition later refunds.
        await Assert.That(total).IsEqualTo(150_000);
        await Assert.That(ok).IsTrue();
        await Assert.That(weekly).IsEqualTo(50_000);
        await Assert.That(heavy).IsEqualTo(0);
        await Assert.That(normal).IsEqualTo(1);
    }

    [Test]
    public async Task CalculateBuildingTaxInfo_SecondHouse_CountsTheHouseAlreadyOwned()
    {
        var manager = CreateManager();
        var owned = CreateHouse(null, 1, 100f, 200f);
        owned.AccountId = AccountId;
        owned.Template = TaxedTemplate(267, 50_000);
        SetPrivateField(manager, "_houses", new Dictionary<uint, House> { [owned.Id] = owned });

        var ok = manager.CalculateBuildingTaxInfo(AccountId, TaxedTemplate(175, 100_000), true,
            out var total, out _, out var normal, out _, out var weekly);

        await Assert.That(ok).IsTrue();
        await Assert.That(total).IsEqualTo(300_000);
        await Assert.That(weekly).IsEqualTo(100_000);
        await Assert.That(normal).IsEqualTo(2);
    }

    private const uint AccountId = 77;

    private static HousingTemplate TaxedTemplate(uint id, uint weeklyTax) => new()
    {
        Id = id,
        Taxation = new Taxation { Id = 1, Tax = weeklyTax },
        HousingSize = new HousingSize { Id = 2, GardenRadius = 10f }
    };

    private static HousingManager CreateManager()
    {
        return new HousingManager(
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IFactionManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IHousingIdManager>().Object,
            Mock.Of<IHousingTldManager>().Object,
            Mock.Of<IItemManager>().Object,
            Mock.Of<IMailManager>().Object,
            Mock.Of<INameManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<IDoodadManager>().Object,
            Mock.Of<IUccManager>().Object,
            Mock.Of<IButlerManager>().Object,
            Mock.Of<IDominionManager>().Object,
            Mock.Of<IGuildDominionManager>().Object);
    }

    private static House CreateHouse(WorldInstance world, uint id, float x, float y)
    {
        var house = new House
        {
            Id = id,
            Template = new HousingTemplate
            {
                HousingSize = new HousingSize { Id = 2, GardenRadius = 10f }
            }
        };
        house.Transform.Local.SetPosition(x, y, 0f);
        typeof(GameObject).GetField("_parentWorld",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(house, world);
        return house;
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        instance.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }
}
