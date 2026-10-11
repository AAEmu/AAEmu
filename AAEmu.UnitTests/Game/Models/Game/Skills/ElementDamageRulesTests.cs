using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class ElementDamageRulesTests
{
    private static Formula Formula65() => new(
        "(  element_value / ( element_value + 8000 ) ) * element_effect_ratio * (1 - element_resist_value / ( element_resist_value + 200 ))")
    {
        Id = (uint)FormulaKind.ElementDamageEffect
    };

    [Test]
    public async Task ElementValue_IsDefaultPlusAddTimesLevel()
    {
        // item_elements row shape: default 450, add 250 → level 0 = 450, level 10 = 2950.
        await Assert.That(ElementDamageRules.ElementValue(450, 250, 0)).IsEqualTo(450);
        await Assert.That(ElementDamageRules.ElementValue(450, 250, 10)).IsEqualTo(2950);
    }

    [Test]
    public async Task Factor_LevelZeroUnarmored_IsDefaultShareOf8000()
    {
        // 450 / (450 + 8000) * 1 * 1 ≈ 0.05325
        var factor = ElementDamageRules.Factor(Formula65(), 450, 1f, 0);
        await Assert.That(factor).IsEqualTo(450f / 8450f).Within(0.0001f);
    }

    [Test]
    public async Task Factor_ClothVsPierceFullRatio_AppliesResistTerm()
    {
        // armor_element_resists cloth/pierce: effect_ratio 1000, add_resist 20, default 0.
        // level-10 armor → resist 200 → (1 - 200/400) = 0.5; element_value 2950 → ~0.1345.
        var elementValue = ElementDamageRules.ElementValue(450, 250, 10);
        var resist = ElementDamageRules.ElementResistValue(0, 20, 10);
        var ratio = ElementDamageRules.EffectRatio(1000);
        var factor = ElementDamageRules.Factor(Formula65(), elementValue, ratio, resist);

        var expected = (2950f / 10950f) * 1f * (1f - 200f / 400f);
        await Assert.That(factor).IsEqualTo(expected).Within(0.0001f);
    }

    [Test]
    public async Task Factor_ZeroEffectRatio_OrMissingFormula_IsNeutral()
    {
        await Assert.That(ElementDamageRules.Factor(Formula65(), 2950, 0f, 0))
            .IsEqualTo(ElementDamageRules.NeutralBonus);
        await Assert.That(ElementDamageRules.Factor(null, 2950, 1f, 0))
            .IsEqualTo(ElementDamageRules.NeutralBonus);
        await Assert.That(ElementDamageRules.Factor(Formula65(), 0, 1f, 0))
            .IsEqualTo(ElementDamageRules.NeutralBonus);
    }

    [Test]
    public async Task ApplyBonus_ZeroFactor_IsBitIdentical()
    {
        const float damage = 1488.25f;
        await Assert.That(ElementDamageRules.ApplyBonus(damage, 0f)).IsEqualTo(damage);
        await Assert.That(ElementDamageRules.ApplyBonus(1000f, 0.0532544f))
            .IsEqualTo(1053.2544f).Within(0.0001f);
    }

    [Test]
    public async Task FactorFor_FlagOff_IsNeutralWithoutLookingUpContent()
    {
        await Assert.That(ElementDamageRules.FactorFor(false, null, null, -1, true, false, false))
            .IsEqualTo(ElementDamageRules.NeutralBonus);
    }
}
