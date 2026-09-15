using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class UnitAttributeLimitRulesTests
{
    // The rows quoted below are the 10.0.2.13 unit_attribute_limits values, id/unit_attribute_id/min/max:
    //   (2, 10, -10000, 8000) move_speed_mul      (1, 74, -666, 2000) global_cooldown_mul
    //   (6, 71, -600, 4000)  casting_time_mul     (49, 223, -1000, 0) item_evolving_cost_mul
    //   (27, 140, 100, 2000000000) drop_rate_mul  (8, 21, 0, 2000000000) melee_block (no enum row)
    private static readonly UnitAttributeLimit MoveSpeed = new(-10000, 8000);
    private static readonly UnitAttributeLimit GlobalCooldown = new(-666, 2000);
    private static readonly UnitAttributeLimit ItemEvolvingCost = new(-1000, 0);
    private static readonly UnitAttributeLimit DropRate = new(100, 2000000000);

    [Test]
    public async Task NoLimitRow_LeavesTheValueAlone()
    {
        // Mass (188) and LungCapacity (91) have no unit_attribute_limits row.
        await Assert.That(UnitAttributeLimitRules.Clamp(26000d, 1000d, null)).IsEqualTo(26000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-5d, 0d, null)).IsEqualTo(-5d);
    }

    [Test]
    public async Task ComposedValueAboveTheMaximum_ClampsToTheMaximum()
    {
        // A +2500% move-speed buff on the 1000 baseline: 1000 -> 26000, capped at 8000.
        await Assert.That(UnitAttributeLimitRules.Clamp(26000d, 1000d, MoveSpeed)).IsEqualTo(8000d);
    }

    [Test]
    public async Task ComposedValueBelowTheMinimum_ClampsToTheMinimum()
    {
        // A suffocating slow takes the GCD multiplier below -666.
        await Assert.That(UnitAttributeLimitRules.Clamp(-900d, 0d, GlobalCooldown)).IsEqualTo(-666d);
        // item_evolving_cost_mul is bounded above by 0: nothing may raise a synthesis price.
        await Assert.That(UnitAttributeLimitRules.Clamp(150d, 0d, ItemEvolvingCost)).IsEqualTo(0d);
    }

    [Test]
    public async Task ComposedValueInsideTheRow_IsUntouched()
    {
        await Assert.That(UnitAttributeLimitRules.Clamp(1000d, 1000d, MoveSpeed)).IsEqualTo(1000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(8000d, 1000d, MoveSpeed)).IsEqualTo(8000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-10000d, -1000d, MoveSpeed)).IsEqualTo(-10000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-1000d, 0d, ItemEvolvingCost)).IsEqualTo(-1000d);
    }

    [Test]
    public async Task UnchangedInput_StaysExactlyTheInput()
    {
        // Percent composition multiplies doubles; the clamp must not perturb a value it does not bound.
        await Assert.That(UnitAttributeLimitRules.Clamp(1234.5678d, 1000d, MoveSpeed)).IsEqualTo(1234.5678d);
    }

    [Test]
    public async Task BaseOutsideTheRow_SkipsTheRowEntirely()
    {
        // drop_rate_mul is the live case: Character.DropRateMul composes 0 as "no bonus" and LootPack
        // adds the row's 100 baseline itself, `(100 + DropRateMul) / 100`. Clamping 0 up to 100 would
        // double every loot roll, so a base outside the row means the two scales disagree and the row
        // is left alone.
        await Assert.That(UnitAttributeLimitRules.Clamp(0d, 0d, DropRate)).IsEqualTo(0d);
        await Assert.That(UnitAttributeLimitRules.Clamp(50d, 0d, DropRate)).IsEqualTo(50d);

        // With the base in the row's own scale the row applies again.
        await Assert.That(UnitAttributeLimitRules.Clamp(90d, 100d, DropRate)).IsEqualTo(100d);
        await Assert.That(UnitAttributeLimitRules.Clamp(150d, 100d, DropRate)).IsEqualTo(150d);
    }

    [Test]
    public async Task BaseAboveTheMaximum_AlsoSkipsTheRow()
    {
        // A caller that composes an attribute in a coarser unit than the table must not be rewritten.
        await Assert.That(UnitAttributeLimitRules.Clamp(9000d, 9000d, MoveSpeed)).IsEqualTo(9000d);
    }
}
