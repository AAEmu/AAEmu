using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// A <c>RecoverExpEffect</c> cast clears the character's resurrection penalty only once every cost and
/// priest check has passed. The effect used to clear it first and run the checks afterwards, so a cast
/// that was refused for money, labour or priest reasons still handed out a free recovery.
/// </summary>
/// <remarks>content: recover_exp_effects id 1 (need_money f, need_labor_power t, need_priest t,
/// penaltied f — skill 17063 기도 드리기) and id 5 (all f, penaltied t — skill 26611 경험치 완전
/// 복구); both cost 10 labour (ExpRecoveryLabor) and neither charges coin.</remarks>
public class RecoverExpEffectTests
{
    private const int PenaltySeconds = 60;

    [Test]
    public async Task Apply_WithoutTheLabourToPay_KeepsThePenalty()
    {
        // Row 1 shape, cast by another unit (the priest check passes), 0 labour in both pools.
        var effect = new RecoverExpEffect { NeedPriest = true, NeedLaborPower = true };
        var character = CreateCharacter();
        var priest = CreateCharacter();

        Apply(effect, priest, character);

        await Assert.That(character.RezPenaltyDuration).IsEqualTo(PenaltySeconds);
    }

    [Test]
    public async Task Apply_WithoutTheMoneyToPay_KeepsThePenalty()
    {
        var effect = new RecoverExpEffect { NeedMoney = true };
        var character = CreateCharacter();

        Apply(effect, CreateCharacter(), character);

        await Assert.That(character.RezPenaltyDuration).IsEqualTo(PenaltySeconds);
    }

    [Test]
    public async Task Apply_SelfCastWhereAPriestIsRequired_KeepsThePenalty()
    {
        // NeedPriest only requires a different unit as caster; the character may not recover itself.
        var effect = new RecoverExpEffect { NeedPriest = true };
        var character = CreateCharacter();

        Apply(effect, character, character);

        await Assert.That(character.RezPenaltyDuration).IsEqualTo(PenaltySeconds);
    }

    [Test]
    public async Task Apply_WithEveryCostPaid_ClearsThePenalty()
    {
        // Row 5 shape: no cost at all, so nothing stands between the cast and the recovery.
        var effect = new RecoverExpEffect();
        var character = CreateCharacter();

        Apply(effect, CreateCharacter(), character);

        await Assert.That(character.RezPenaltyDuration).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_WithMoneyButNoLabour_ChargesNoMoney()
    {
        // Both requirements are checked before either is charged. The money used to leave the character
        // before the labour check ran, and nothing refunded it when the cast was refused.
        var effect = new RecoverExpEffect { NeedMoney = true, NeedLaborPower = true };
        var character = CreateCharacter();
        character.Money = 100_000; // level 50 prices the recovery at 50_000

        Apply(effect, CreateCharacter(), character);

        await Assert.That(character.Money).IsEqualTo(100_000);
        await Assert.That(character.RezPenaltyDuration).IsEqualTo(PenaltySeconds);
    }

    [Test]
    public async Task Apply_WithNoPenalty_LeavesTheCharacterAlone()
    {
        var effect = new RecoverExpEffect();
        var character = CreateCharacter();
        character.RezPenaltyDuration = 0;

        Apply(effect, CreateCharacter(), character);

        await Assert.That(character.RezPenaltyDuration).IsEqualTo(0);
    }

    private static Character CreateCharacter() => new(new UnitCustomModelParams())
    {
        Id = 42,
        ObjId = 42,
        Name = "Tester",
        Level = 50,
        RezPenaltyDuration = PenaltySeconds
    };

    private static void Apply(RecoverExpEffect effect, Character caster, Character target) =>
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(17063, 1), new EffectSource(), null, DateTime.UtcNow);
}
