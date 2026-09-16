using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The per-attribute decisions <see cref="UnitAttributeLimitRules"/> makes: which row applies to which
/// scale, and that a row bounds one attribute's own value rather than a property that reads several.
/// </summary>
public class UnitAttributeLimitRulesTests
{
    // The rows quoted below are the 10.0.2.13 unit_attribute_limits values, id/unit_attribute_id/min/max:
    //   (2, 10, -10000, 8000) move_speed_mul       (1, 74, -666, 2000) global_cooldown_mul
    //   (6, 71, -600, 4000)  casting_time_mul      (49, 223, -1000, 0) item_evolving_cost_mul
    //   (27, 140, 100, 2000000000) drop_rate_mul   (8, 21, 0, 2000000000) melee_block (no enum row)
    //   (25, 95, 0, 500) exp_mul                   (43, 136, -2000000000, 50) living_point_gain
    //   (44, 137, -100, 2000000000) living_point_gain_mul
    private static readonly UnitAttributeLimit MoveSpeed = new(-10000, 8000);
    private static readonly UnitAttributeLimit GlobalCooldown = new(-666, 2000);
    private static readonly UnitAttributeLimit ItemEvolvingCost = new(-1000, 0);
    private static readonly UnitAttributeLimit DropRate = new(100, 2000000000);
    private static readonly UnitAttributeLimit Exp = new(0, 500);
    private static readonly UnitAttributeLimit LivingPointGain = new(-2000000000, 50);
    private static readonly UnitAttributeLimit LivingPointGainMul = new(-100, 2000000000);

    [Test]
    public async Task NoLimitRow_LeavesTheValueAlone()
    {
        // Mass (188) and LungCapacity (91) have no unit_attribute_limits row.
        await Assert.That(UnitAttributeLimitRules.Clamp(26000d, UnitAttribute.Mass, null)).IsEqualTo(26000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-5d, UnitAttribute.LungCapacity, null)).IsEqualTo(-5d);
    }

    [Test]
    public async Task ComposedValueAboveTheMaximum_ClampsToTheMaximum()
    {
        // A +2500% move-speed buff on the 1000 baseline: 1000 -> 26000, capped at 8000.
        await Assert.That(UnitAttributeLimitRules.Clamp(26000d, UnitAttribute.MoveSpeedMul, MoveSpeed)).IsEqualTo(8000d);
    }

    [Test]
    public async Task ComposedValueBelowTheMinimum_ClampsToTheMinimum()
    {
        // A suffocating slow takes the GCD multiplier below -666.
        await Assert.That(UnitAttributeLimitRules.Clamp(-900d, UnitAttribute.GlobalCooldownMul, GlobalCooldown)).IsEqualTo(-666d);
        // item_evolving_cost_mul is bounded above by 0: nothing may raise a synthesis price.
        await Assert.That(UnitAttributeLimitRules.Clamp(150d, UnitAttribute.ItemEvolvingCostMul, ItemEvolvingCost)).IsEqualTo(0d);
    }

    [Test]
    public async Task ComposedValueInsideTheRow_IsUntouched()
    {
        await Assert.That(UnitAttributeLimitRules.Clamp(1000d, UnitAttribute.MoveSpeedMul, MoveSpeed)).IsEqualTo(1000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(8000d, UnitAttribute.MoveSpeedMul, MoveSpeed)).IsEqualTo(8000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-10000d, UnitAttribute.MoveSpeedMul, MoveSpeed)).IsEqualTo(-10000d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-1000d, UnitAttribute.ItemEvolvingCostMul, ItemEvolvingCost)).IsEqualTo(-1000d);
    }

    [Test]
    public async Task UnchangedInput_StaysExactlyTheInput()
    {
        // Percent composition multiplies doubles; the clamp must not perturb a value it does not bound.
        await Assert.That(UnitAttributeLimitRules.Clamp(1234.5678d, UnitAttribute.MoveSpeedMul, MoveSpeed)).IsEqualTo(1234.5678d);
    }

    [Test]
    public async Task DropRateMul_RowIsInTheClientScale_SoTheDeltaIsNotClamped()
    {
        // Character.DropRateMul composes 0 as "no bonus" and the loot code adds the row's 100 baseline
        // itself, so the row's 100 minimum must not be applied to the delta: the 125 shipped flat rows
        // below it stay live.
        await Assert.That(UnitAttributeLimitRules.Clamp(0d, UnitAttribute.DropRateMul, DropRate)).IsEqualTo(0d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-80d, UnitAttribute.DropRateMul, DropRate)).IsEqualTo(-80d);
        await Assert.That(UnitAttributeLimitRules.Clamp(50d, UnitAttribute.DropRateMul, DropRate)).IsEqualTo(50d);
    }

    [Test]
    public async Task ExpMul_RowIsInTheClientScale_SoTheDeltaIsNotClamped()
    {
        // Same shape, but 0 is inside exp_mul's row, so the row would floor every exp penalty at 0:
        // buff 27888 ships -50 and npc templates 13444, 16553 and 16554 ship -500.
        await Assert.That(UnitAttributeLimitRules.Clamp(-50d, UnitAttribute.ExpMul, Exp)).IsEqualTo(-50d);
        await Assert.That(UnitAttributeLimitRules.Clamp(-500d, UnitAttribute.ExpMul, Exp)).IsEqualTo(-500d);
        await Assert.That(UnitAttributeLimitRules.Clamp(200d, UnitAttribute.ExpMul, Exp)).IsEqualTo(200d);
    }

    [Test]
    public async Task LivingPointGainMul_RowIsInTheClientScale_SoTheDeltaIsNotClamped()
    {
        // The third row of that shape: the award site adds the 100 baseline, so a -150% delta is not
        // rewritten into the row's -100 absolute minimum.
        await Assert.That(UnitAttributeLimitRules.Clamp(-150d, UnitAttribute.LivingPointGainMul, LivingPointGainMul)).IsEqualTo(-150d);
        await Assert.That(UnitAttributeLimitRules.Clamp(0d, UnitAttribute.LivingPointGainMul, LivingPointGainMul)).IsEqualTo(0d);
    }

    [Test]
    public async Task LivingPointGain_IsComposedInTheRowsScale_SoTheRowApplies()
    {
        // The flat sibling is not a delta: the server adds it to the award as it stands, so the row's 50
        // maximum caps the shipped +100 and +200 rows.
        await Assert.That(UnitAttributeLimitRules.Clamp(200d, UnitAttribute.LivingPointGain, LivingPointGain)).IsEqualTo(50d);
        await Assert.That(UnitAttributeLimitRules.Clamp(100d, UnitAttribute.LivingPointGain, LivingPointGain)).IsEqualTo(50d);
        await Assert.That(UnitAttributeLimitRules.Clamp(30d, UnitAttribute.LivingPointGain, LivingPointGain)).IsEqualTo(30d);
    }

    [Test]
    public async Task OnlyTheThreeDocumentedRowsAreTreatedAsDeltas()
    {
        // The scale decision is a list, not a guess: adding a fourth row has to be a deliberate edit here
        // and a new case above.
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.DropRateMul)).IsTrue();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.ExpMul)).IsTrue();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.LivingPointGainMul)).IsTrue();

        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.LivingPointGain)).IsFalse();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.MoveSpeedMul)).IsFalse();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.Armor)).IsFalse();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.MeleeDamageMul)).IsFalse();
        await Assert.That(UnitAttributeLimitRules.IsClientScaleDelta(UnitAttribute.HealMul)).IsFalse();
    }
}
