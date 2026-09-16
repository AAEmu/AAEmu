using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// BuffSteal (type 16): value3 is how many beneficial effects are taken and value4 restricts the take to one
/// tag. Skill 소드락질 10104's text ("적대상의 이로운 효과 2개 탈취") is what value3 reads as.
/// </summary>
public class BuffStealRulesTests
{
    private static BuffStealRules.StealCandidate Buff(int index, uint tag = 0, BuffKind kind = BuffKind.Good) =>
        new(index, (uint)(1000 + index), kind, Passive: false, System: false, tag == 0 ? [] : [tag]);

    [Test]
    public async Task UntaggedRow_TakesTheLowestSlots()
    {
        var chosen = BuffStealRules.Select([Buff(7), Buff(2), Buff(5)], maxCount: 2, requiredTagId: 0);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 2, 5 });
    }

    [Test]
    public async Task OnlyBeneficialEffectsAreTaken()
    {
        // The three leech skills all say 적대상의 이로운 효과 (the enemy target's beneficial effects).
        var chosen = BuffStealRules.Select(
            [Buff(1), Buff(2, kind: BuffKind.Bad), Buff(3, kind: BuffKind.Hidden), Buff(4)], 4, 0);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 1, 4 });
    }

    [Test]
    public async Task PassivesAndSystemBuffsAreNotTaken()
    {
        var chosen = BuffStealRules.Select(
        [
            new BuffStealRules.StealCandidate(1, 1001, BuffKind.Good, Passive: true, System: false, []),
            new BuffStealRules.StealCandidate(2, 1002, BuffKind.Good, Passive: false, System: true, []),
            Buff(3)
        ], 4, 0);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 3 });
    }

    [Test]
    public async Task TaggedRow_TakesOnlyThatFamily()
    {
        // 229 is the tag on 소드락질 10104's effect 34212, 1229 the one on effect 44232.
        var chosen = BuffStealRules.Select([Buff(1), Buff(2, 229), Buff(3, 1229), Buff(4, 229)], 3, 229);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 2, 4 });
    }

    [Test]
    public async Task ACountBelowOne_TakesNothing()
    {
        await Assert.That(BuffStealRules.Select([Buff(1)], maxCount: 0, requiredTagId: 0)).IsEmpty();
        await Assert.That(BuffStealRules.Select([Buff(1)], maxCount: -1, requiredTagId: 0)).IsEmpty();
        await Assert.That(BuffStealRules.Select(null, maxCount: 3, requiredTagId: 0)).IsEmpty();
    }

    [Test]
    public async Task ATagNobodyHolds_TakesNothing()
    {
        await Assert.That(BuffStealRules.Select([Buff(1), Buff(2)], maxCount: 3, requiredTagId: 229)).IsEmpty();
    }

    [Test]
    public async Task ThreeEffects_AreAllTakenWhenTheRowAsksForThree()
    {
        // 31613 (skill 10104) and 51348 (돌려주기 44205) ask for three.
        var chosen = BuffStealRules.Select([Buff(1), Buff(2), Buff(3)], maxCount: 3, requiredTagId: 0);

        await Assert.That(chosen.Count).IsEqualTo(3);
    }
}
