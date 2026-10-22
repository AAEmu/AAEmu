using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// <see cref="Unit.AddCombatResource"/> must push CombatResource-owned unit_modifiers into
/// <see cref="Unit.AttackSpeedRating"/> (and siblings) so shared GCD matches the client sheet.
/// </summary>
[NotInParallel]
public class CombatResourceModifierBonusTests
{
    private const int FightResourceId = 1;
    private const int FightMax = 5;
    private const int AttackSpeedLinear = 3000;
    private const int CritLinear = 5000;

    [Before(Test)]
    public void SeedFightWithModifiers() => CombatResourceGameData.Instance.SeedForTests(
        [new CombatResource { Id = FightResourceId, Name = "광란", Max = FightMax }],
        resourceIdsByAbility: null,
        unitModifiers: new Dictionary<int, List<BonusTemplate>>
        {
            [FightResourceId] =
            [
                new BonusTemplate
                {
                    Attribute = UnitAttribute.AttackSpeedMul,
                    ModifierType = UnitModifierType.Value,
                    Value = 0,
                    LinearLevelBonus = AttackSpeedLinear
                },
                new BonusTemplate
                {
                    Attribute = UnitAttribute.MeleeCriticalBonus,
                    ModifierType = UnitModifierType.Value,
                    Value = 0,
                    LinearLevelBonus = CritLinear
                }
            ]
        });

    [After(Test)]
    public void ClearTables() => CombatResourceGameData.Instance.ClearForTests();

    [Test]
    public async Task AddCombatResource_RaisesAttackSpeedRatingByTheScaledRow()
    {
        var unit = new Unit { ObjId = 1, Level = 50 };
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(0L);

        unit.AddCombatResource(FightResourceId, 1);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(30L);

        unit.AddCombatResource(FightResourceId, 4);
        await Assert.That(unit.GetCombatResource(FightResourceId)).IsEqualTo(5);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(150L);
    }

    [Test]
    public async Task SpendingThePool_ClearsTheBonus()
    {
        var unit = new Unit { ObjId = 1, Level = 50 };
        unit.AddCombatResource(FightResourceId, 5);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(150L);

        unit.AddCombatResource(FightResourceId, -5);
        await Assert.That(unit.GetCombatResource(FightResourceId)).IsEqualTo(0);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(0L);
    }

    [Test]
    public async Task ResourceWithoutModifierRows_LeavesAttackSpeedAlone()
    {
        CombatResourceGameData.Instance.SeedForTests(
            [new CombatResource { Id = 2, Name = "착취", Max = 5 }],
            resourceIdsByAbility: null);

        var unit = new Unit { ObjId = 1, Level = 50 };
        unit.AddCombatResource(2, 5);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(0L);
    }
}
