using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// BuffSteal (type 16): value3 is how many effects are transferred and value4 restricts them to one tag.
/// Ordinary rows take beneficial effects from the target; the paired 1/1 Reversal mode returns harmful
/// effects from the caster.
/// </summary>
public class BuffStealRulesTests
{
    private static BuffStealRules.StealCandidate Buff(int index, uint tag = 0, BuffKind kind = BuffKind.Good) =>
        new(index, (uint)(1000 + index), kind, Passive: false, System: false, tag == 0 ? [] : [tag]);

    [Test]
    public async Task AuthoredParameters_SelectTransferMode()
    {
        await Assert.That(BuffStealRules.ResolveMode(0, 0))
            .IsEqualTo(new BuffStealRules.TransferMode(BuffKind.Good, false));
        await Assert.That(BuffStealRules.ResolveMode(1, 1))
            .IsEqualTo(new BuffStealRules.TransferMode(BuffKind.Bad, true));

        // No shipped row gives either flag an independent meaning.
        await Assert.That(BuffStealRules.ResolveMode(1, 0))
            .IsEqualTo(new BuffStealRules.TransferMode(BuffKind.Good, false));
        await Assert.That(BuffStealRules.ResolveMode(0, 1))
            .IsEqualTo(new BuffStealRules.TransferMode(BuffKind.Good, false));
    }

    [Test]
    public async Task UntaggedRow_TakesTheLowestSlots()
    {
        var chosen = BuffStealRules.Select([Buff(7), Buff(2), Buff(5)], maxCount: 2, requiredTagId: 0,
            BuffKind.Good);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 2, 5 });
    }

    [Test]
    public async Task OnlyBeneficialEffectsAreTaken()
    {
        // The three leech skills all say 적대상의 이로운 효과 (the enemy target's beneficial effects).
        var chosen = BuffStealRules.Select(
            [Buff(1), Buff(2, kind: BuffKind.Bad), Buff(3, kind: BuffKind.Hidden), Buff(4)], 4, 0,
            BuffKind.Good);

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
        ], 4, 0, BuffKind.Good);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 3 });
    }

    [Test]
    public async Task TaggedRow_TakesOnlyThatFamily()
    {
        // 229 is the tag on 소드락질 10104's effect 34212, 1229 the one on effect 44232.
        var chosen = BuffStealRules.Select([Buff(1), Buff(2, 229), Buff(3, 1229), Buff(4, 229)], 3, 229,
            BuffKind.Good);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 2, 4 });
    }

    [Test]
    public async Task ACountBelowOne_TakesNothing()
    {
        await Assert.That(BuffStealRules.Select([Buff(1)], 0, 0, BuffKind.Good)).IsEmpty();
        await Assert.That(BuffStealRules.Select([Buff(1)], -1, 0, BuffKind.Good)).IsEmpty();
        await Assert.That(BuffStealRules.Select(null, 3, 0, BuffKind.Good)).IsEmpty();
    }

    [Test]
    public async Task ATagNobodyHolds_TakesNothing()
    {
        await Assert.That(BuffStealRules.Select([Buff(1), Buff(2)], 3, 229, BuffKind.Good)).IsEmpty();
    }

    [Test]
    public async Task ThreeEffects_AreAllTakenWhenTheRowAsksForThree()
    {
        // 31613 (skill 10104) and 58195 (돌려주기 44205) ask for three.
        var chosen = BuffStealRules.Select([Buff(1), Buff(2), Buff(3)], 3, 0, BuffKind.Good);

        await Assert.That(chosen.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ReversalRows_SelectHarmfulEffects()
    {
        var chosen = BuffStealRules.Select(
        [
            Buff(1),
            Buff(2, 229, BuffKind.Bad),
            new BuffStealRules.StealCandidate(3, 1003, BuffKind.Bad, Passive: true, System: false, [229]),
            new BuffStealRules.StealCandidate(4, 1004, BuffKind.Bad, Passive: false, System: true, [229]),
            Buff(5, 229, BuffKind.Bad),
            Buff(6, 229, BuffKind.Bad)
        ], 2, 229, BuffKind.Bad);

        await Assert.That(chosen.Select(candidate => candidate.Index)).IsEquivalentTo(new[] { 2, 5 });
    }
}
