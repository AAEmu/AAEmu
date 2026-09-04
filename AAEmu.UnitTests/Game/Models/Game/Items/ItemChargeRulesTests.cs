using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemChargeRulesTests
{
    // Bamboo fishing rod 27308 names golden crafted lure 45600 as its recharge reagent.
    private const uint BambooRodRestrictLure = 45600;

    [Test]
    public async Task TryApply_NamedLure_StartsANewChargeWindow()
    {
        var rod = Rod(SlotType.Inventory);
        var lure = new Item { TemplateId = BambooRodRestrictLure };
        var at = new DateTime(2026, 9, 4, 12, 20, 45, DateTimeKind.Utc);

        var result = ItemChargeRules.TryApply(rod, lure, at);

        await Assert.That(result).IsEqualTo(ItemChargeRules.RechargeApply.Applied);
        await Assert.That(rod.ChargeStartTime).IsEqualTo(at);
        await Assert.That(rod.ChargeCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryApply_WrongReagent_IsRejected()
    {
        var rod = Rod(SlotType.Inventory);
        var other = new Item { TemplateId = 27319 };

        var result = ItemChargeRules.TryApply(rod, other, DateTime.UtcNow);

        await Assert.That(result).IsEqualTo(ItemChargeRules.RechargeApply.Rejected);
        await Assert.That(rod.ChargeStartTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task TryApply_EquippedRod_IsRefused()
    {
        var rod = Rod(SlotType.Equipment);
        var lure = new Item { TemplateId = BambooRodRestrictLure };

        var result = ItemChargeRules.TryApply(rod, lure, DateTime.UtcNow);

        await Assert.That(result).IsEqualTo(ItemChargeRules.RechargeApply.Equipped);
        await Assert.That(rod.ChargeStartTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task TryApply_MissingRestrict_IsRejected()
    {
        var rod = new EquipItem
        {
            Template = new EquipItemTemplate { RechargeRestrictItemId = 0, ChargeCount = 0 },
            SlotType = SlotType.Inventory
        };
        var lure = new Item { TemplateId = BambooRodRestrictLure };

        await Assert.That(ItemChargeRules.TryApply(rod, lure, DateTime.UtcNow))
            .IsEqualTo(ItemChargeRules.RechargeApply.Rejected);
    }

    private static EquipItem Rod(SlotType slot) =>
        new()
        {
            Template = new EquipItemTemplate
            {
                RechargeRestrictItemId = BambooRodRestrictLure,
                ChargeCount = 0,
                ChargeLifetime = 120,
                RechargeBuffId = 22921
            },
            SlotType = slot
        };
}
