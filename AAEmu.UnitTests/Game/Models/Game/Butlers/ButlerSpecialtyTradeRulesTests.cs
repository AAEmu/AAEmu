using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public class ButlerSpecialtyTradeRulesTests
{
    [Test]
    public async Task Admission_UsesTypedCraftLaborAndProductionCosts()
    {
        var context = CreateContext();
        var accepted = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out var admission);

        await Assert.That(accepted).IsTrue();
        await Assert.That(admission.Trade.Id).IsEqualTo(9u);
        await Assert.That(admission.Trade.CraftId).IsEqualTo(7001u);
        await Assert.That(admission.Product.ItemId).IsEqualTo(7002u);
        await Assert.That(admission.Materials.Count).IsEqualTo(2);
        await Assert.That(admission.AvailableSpecialtyTradeSlots).IsEqualTo(2u);
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(admission, 100, 60,
            out var costs)).IsTrue();
        await Assert.That(costs.LaborPower).IsEqualTo(12u);
        await Assert.That(costs.ProductionCost).IsEqualTo(7u);
        await Assert.That(costs.Materials.Select(material => material.ItemId))
            .IsEquivalentTo(new[] { 7003u, 7004u });
    }

    [Test]
    public async Task Admission_RejectsDuplicateTradeIdAndZoneAndSlotOverflow()
    {
        var context = CreateContext();
        var active = new ButlerSpecialtyTradeJob(1, 20, 9, 4, 7002, 10, 20);
        var duplicate = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, [active], 2, out _);
        var full = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill,
            [active, new ButlerSpecialtyTradeJob(2, 20, 9999, 4, 9999, 10, 20)], 2, out _);

        await Assert.That(duplicate).IsFalse();
        await Assert.That(full).IsFalse();
    }

    [Test]
    public async Task Admission_AddsPersistedSpecialtyTradeExpansionsToCapacity()
    {
        var context = CreateContext();
        var active = new ButlerSpecialtyTradeJob(1, 20, 9999, 4, 7002, 10, 20);
        var accepted = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, [active], 2, 1, out var admission);
        var overflow = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, [active, new ButlerSpecialtyTradeJob(2, 20, 10, 4, 7002, 10, 20)],
            2, 0, out _);

        await Assert.That(accepted).IsTrue();
        await Assert.That(admission.AvailableSpecialtyTradeSlots).IsEqualTo(2u);
        await Assert.That(overflow).IsFalse();
    }

    [Test]
    public async Task DeliveryTimeAndDueState_AreContentBoundedAndClockIndependent()
    {
        var context = CreateContext();
        var chosen = ButlerSpecialtyTradeRules.TryChooseDeliveryTime(context.Trade,
            (minimum, maximum) => minimum + (maximum - minimum) / 2, out var deliveryTime);
        var job = new ButlerSpecialtyTradeJob(1, context.Trade.NpcId, context.Trade.Id,
            4, context.Product.ItemId, 100, deliveryTime);

        await Assert.That(chosen).IsTrue();
        await Assert.That(deliveryTime).IsEqualTo(125u);
        await Assert.That(ButlerSpecialtyTradeRules.IsDue(job, 224)).IsFalse();
        await Assert.That(ButlerSpecialtyTradeRules.IsDue(job, 225)).IsTrue();
        await Assert.That(ButlerSpecialtyTradeRules.RemainingDeliverySeconds(job, 224)).IsEqualTo(1u);
        await Assert.That(ButlerSpecialtyTradeRules.RemainingDeliverySeconds(job, 225)).IsEqualTo(0u);
        await Assert.That(ButlerSpecialtyTradeRules.TryChooseDeliveryTime(context.Trade,
            (_, _) => 0, out _)).IsFalse();
    }

    [Test]
    public async Task Admission_RejectsUnsupportedCraftOutputInsteadOfInventingAReward()
    {
        var context = CreateContext();
        context.Craft.CraftProducts.Add(new CraftProduct
        {
            CraftId = context.Craft.Id,
            ItemId = 9999,
            Amount = 1,
            Rate = 50
        });

        var accepted = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out _);

        await Assert.That(accepted).IsFalse();
    }

    private static ButlerSpecialtyTradeAdmissionContext CreateContext()
    {
        var template = new ButlerTemplate
        {
            Id = 1,
            TradeAvailableLevel = 1,
            DefaultSpecialtyTradeSlotCount = 2
        };
        var level = new ButlerLevel { ButlerId = 1, Level = 1, MaxLaborPower = 100 };
        var trade = new ButlerSpecialtyTradeDefinition(9, 20, 7001, 100, 150, 7, 4);
        var craft = new Craft
        {
            Id = 7001,
            SkillId = 8001,
            CraftProducts = [new CraftProduct { CraftId = 7001, ItemId = 7002, Amount = 1, Rate = 100 }],
            CraftMaterials =
            [
                new CraftMaterial { CraftId = 7001, ItemId = 7003, Amount = 2 },
                new CraftMaterial { CraftId = 7001, ItemId = 7004, Amount = 3 }
            ]
        };
        var skill = new SkillTemplate { Id = 8001, ConsumeLaborPower = 12 };
        return new ButlerSpecialtyTradeAdmissionContext(template, level, trade, craft, skill,
            craft.CraftProducts[0], craft.CraftMaterials
                .Select(material => new ButlerSpecialtyTradeMaterialCost(material.ItemId,
                    checked((uint)material.Amount))).ToArray(), 2);
    }
}
