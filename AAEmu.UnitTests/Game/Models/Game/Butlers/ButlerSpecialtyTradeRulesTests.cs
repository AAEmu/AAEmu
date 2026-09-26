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
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(admission, 100, false,
            out var costs)).IsTrue();
        await Assert.That(costs.LaborPower).IsEqualTo(12u);
        await Assert.That(costs.BaseProductionCost).IsEqualTo(7u);
        await Assert.That(costs.OverworkProductionCost).IsEqualTo(0u);
        await Assert.That(costs.TotalProductionCost).IsEqualTo(7u);
        await Assert.That(costs.Materials.Select(material => material.ItemId))
            .IsEquivalentTo(new[] { 7003u, 7004u });
    }

    [Test]
    public async Task Costs_ChargeOverworkProductionCostOnlyWhenOtherProductionIsRunning()
    {
        // Shipped ui_texts 11129 / 11224: a farmhand already running another production function
        // consumes an extra production cost. overwork_production_cost_mul is per-mille, rounded up.
        var context = CreateContext();
        context.ButlerTemplate.OverworkProductionCostMul = 500; // 50% of the base cost
        await Assert.That(ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out var admission))
            .IsTrue();

        // No other production running: base cost only.
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(admission, 100, false,
            out var idle)).IsTrue();
        await Assert.That(idle.BaseProductionCost).IsEqualTo(7u);
        await Assert.That(idle.OverworkProductionCost).IsEqualTo(0u);
        await Assert.That(idle.TotalProductionCost).IsEqualTo(7u);

        // Other production running: overwork added. ceil(7 * 500 / 1000) = 4.
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(admission, 100, true,
            out var busy)).IsTrue();
        await Assert.That(busy.BaseProductionCost).IsEqualTo(7u);
        await Assert.That(busy.OverworkProductionCost).IsEqualTo(4u);
        await Assert.That(busy.TotalProductionCost).IsEqualTo(11u);
    }

    [Test]
    public async Task Costs_RoundOverworkUpAndDoNotOverflow()
    {
        // Any remainder rounds up: ceil(7 * 501 / 1000) = 4, not 3.
        var context = CreateContext();
        context.ButlerTemplate.OverworkProductionCostMul = 501;
        await Assert.That(ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out var admission))
            .IsTrue();
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(admission, 100, true,
            out var costs)).IsTrue();
        await Assert.That(costs.OverworkProductionCost).IsEqualTo(4u);
        await Assert.That(costs.TotalProductionCost).IsEqualTo(11u);

        // A multiplier large enough to overflow the computed total must fail closed, not wrap.
        // base * mul / 1000 > uint.MaxValue, i.e. base > 1_000_000 at mul = uint.MaxValue.
        var overflow = CreateContext();
        overflow.ButlerTemplate.OverworkProductionCostMul = uint.MaxValue;
        var bigTrade = overflow.Trade with { ConsumeProductionCost = 2_000_000 };
        await Assert.That(ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            overflow.ButlerTemplate, overflow.ButlerLevel, bigTrade, overflow.Craft,
            overflow.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out var overflowAdmission))
            .IsTrue();
        await Assert.That(ButlerSpecialtyTradeRules.TryCalculateCosts(overflowAdmission, 100, true,
            out _)).IsFalse();
    }

    [Test]
    public async Task Admission_ReportsSlotExhaustionAndDuplicateSeparatelyFromInvalidContent()
    {
        // The service can only report NoSpecialtyTradeSlot / DuplicateSpecialtyTrade if the rules
        // layer distinguishes them; previously both collapsed into a bare false, so the client
        // always saw InvalidContent and the service-side checks were unreachable.
        var context = CreateContext();
        var active = new ButlerSpecialtyTradeJob(1, 20, 9, 4, 7002, 10, 20);

        var duplicate = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, [active], 2, out _, out var duplicateFailure);
        await Assert.That(duplicate).IsFalse();
        await Assert.That(duplicateFailure)
            .IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.DuplicateSpecialtyTrade);

        var full = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill,
            [active, new ButlerSpecialtyTradeJob(2, 20, 9999, 4, 9999, 10, 20)], 2, out _,
            out var fullFailure);
        await Assert.That(full).IsFalse();
        await Assert.That(fullFailure)
            .IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.NoSpecialtyTradeSlot);

        var accepted = ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            context.ButlerTemplate, context.ButlerLevel, context.Trade, context.Craft,
            context.CraftSkill, Array.Empty<ButlerSpecialtyTradeJob>(), 2, out _, out var okFailure);
        await Assert.That(accepted).IsTrue();
        await Assert.That(okFailure).IsEqualTo(ButlerSpecialtyTradeRules.AdmissionFailure.None);
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
            DefaultSpecialtyTradeSlotCount = 2,
            OverworkProductionCostMul = 0
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
