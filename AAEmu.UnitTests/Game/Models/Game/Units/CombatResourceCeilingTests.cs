using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// <c>max_combat_resource</c> (215) through the real <see cref="Unit.AddCombatResource"/> clamp.
/// </summary>
/// <remarks>
/// The pool is 광란 (<c>combat_resources</c> id 1, max 5, no bar buff), which is the one buff 22278 (정복)
/// names when it stores 1 for "광란의 중첩 개수가 1개 증가합니다".
/// </remarks>
[NotInParallel]
public class CombatResourceCeilingTests
{
    private const int FightResourceId = 1;
    private const int FightMax = 5;

    [Before(Test)]
    public void SeedFight() => CombatResourceGameData.Instance.SeedForTests(
        [new CombatResource { Id = FightResourceId, Name = "광란", Max = FightMax }],
        null);

    [After(Test)]
    public void ClearTables() => CombatResourceGameData.Instance.ClearForTests();

    [Test]
    public async Task WithoutARow_ThePoolStopsAtItsOwnMax()
    {
        // The absence pin for 215, which is also the pre-existing behaviour.
        var unit = new Unit { ObjId = 1, Level = 50 };

        await Assert.That(unit.MaxCombatResource).IsEqualTo(0);

        await Assert.That(unit.AddCombatResource(FightResourceId, 10)).IsEqualTo(FightMax);
    }

    [Test]
    public async Task ABuffOfPlusOne_RaisesTheCeilingByOne()
    {
        var unit = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(unit, UnitAttribute.MaxCombatResource, 1);

        await Assert.That(unit.MaxCombatResource).IsEqualTo(1);

        await Assert.That(unit.AddCombatResource(FightResourceId, 10)).IsEqualTo(FightMax + 1);
    }

    [Test]
    public async Task TheCeilingStillClampsTheHeldAmount()
    {
        var unit = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(unit, UnitAttribute.MaxCombatResource, 1);

        await Assert.That(unit.AddCombatResource(FightResourceId, 1)).IsEqualTo(1);
        await Assert.That(unit.AddCombatResource(FightResourceId, 100)).IsEqualTo(FightMax + 1);
    }

    [Test]
    public async Task ANegativeRow_LowersTheCeiling()
    {
        // Two shipped rows store -2, which is a real reduction on a five-point pool.
        var unit = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(unit, UnitAttribute.MaxCombatResource, -2);

        await Assert.That(unit.AddCombatResource(FightResourceId, 10)).IsEqualTo(FightMax - 2);
    }

    [Test]
    public async Task AnUnknownResource_KeepsHavingNoCeiling()
    {
        // CombatResourceGameData.GetMax answers 0 for an id it does not know and AddCombatResource then only
        // keeps the total non-negative; the unit attribute must not invent a ceiling of its own.
        var unit = new Unit { ObjId = 1, Level = 50 };
        TestUnitModifier.Apply(unit, UnitAttribute.MaxCombatResource, 1500);

        await Assert.That(unit.AddCombatResource(9999, 40)).IsEqualTo(40);
        await Assert.That(unit.AddCombatResource(9999, -100)).IsEqualTo(0);
    }

    [Test]
    public async Task ACharacterSeesTheSameCeiling()
    {
        // The rows are not player-only, but they do reach a Character through the same getter.
        var character = new CharacterMock { ObjId = 2, Level = 50 };
        TestUnitModifier.Apply(character, UnitAttribute.MaxCombatResource, 3);

        await Assert.That(character.MaxCombatResource).IsEqualTo(3);
        await Assert.That(character.AddCombatResource(FightResourceId, 10)).IsEqualTo(FightMax + 3);
    }
}
