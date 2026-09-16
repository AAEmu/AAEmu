using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// How simultaneous buff grants combine. The overlaps these rules exist for are in the shipped data
/// (10.0.2.13): the nine glider buffs 실험형 날틀 1029 / 개량형 3528 / 강화형 3529 / 완성형 3530 /
/// 달빛 그림자 날개 3583 / 뇌우 3584 / 이지 여신의 날개 2098 / 붉은 용 날개 2099 / 용오름 2097 all grant
/// 날틀 접기 17657, 23 buffs swap 폭탄 발사 준비 35351 and 9 swap 발묶음 12133.
/// Ids are faked here; the shapes are the live ones.
/// </summary>
public class BuffGrantRulesTests
{
    private const uint FoldGlider = 17657;
    private const uint GliderBoost = 13435;
    private const uint StanceOrigin = 35351;
    private const uint FireballStance = 34500;
    private const uint FrostStance = 34501;
    private const uint HarpoonLanding = 17618;
    private const uint PassiveA = 315;
    private const uint PassiveB = 316;

    private static BuffGrantSet Grants(params uint[] skillIds) => new() { GrantedSkills = skillIds };

    private static BuffGrantSet Swaps(uint origin, uint replacement, int priority, uint rowId, uint buffId = 1) =>
        new() { Swaps = [new BuffSkillSwap(rowId, buffId, priority, origin, replacement)] };

    private static BuffGrantSet Passives(params uint[] passiveIds) => new() { PassiveBuffIds = passiveIds };

    [Test]
    public async Task HeldSkills_TwoHoldersGrantingTheSameSkill_HoldItOnce()
    {
        var held = BuffGrantRules.HeldSkills([Grants(FoldGlider), Grants(FoldGlider, GliderBoost)]);

        await Assert.That(held).IsEquivalentTo(new[] { FoldGlider, GliderBoost });
    }

    [Test]
    public async Task ReleasedSkills_OneOfTwoHoldersEnds_SkillTheOtherStillGrantsSurvives()
    {
        // Two glider buffs are up at once, one ends: 날틀 접기 belongs to the one still running.
        var released = BuffGrantRules.ReleasedSkills([FoldGlider], [Grants(FoldGlider, GliderBoost)]);

        await Assert.That(released).IsEmpty();
        await Assert.That(BuffGrantRules.HeldSkills([Grants(FoldGlider, GliderBoost)]))
            .Contains(FoldGlider);
    }

    [Test]
    public async Task ReleasedSkills_LastHolderEnds_SkillIsReleased()
    {
        var released = BuffGrantRules.ReleasedSkills([FoldGlider, GliderBoost], []);

        await Assert.That(released).IsEquivalentTo(new[] { FoldGlider, GliderBoost });
    }

    [Test]
    public async Task ReleasedSkills_HolderEnds_ReleasesItsOwnSkillAndKeepsTheSharedOne()
    {
        // A grants 날틀 접기 + 날틀 추진, B grants 날틀 접기. A ends: only its own skill goes.
        var released = BuffGrantRules.ReleasedSkills([FoldGlider, GliderBoost], [Grants(FoldGlider)]);

        await Assert.That(released).IsEquivalentTo(new[] { GliderBoost });
        await Assert.That(BuffGrantRules.HeldSkills([Grants(FoldGlider)])).IsEquivalentTo(new[] { FoldGlider });
    }

    [Test]
    public async Task AddedSkills_SecondHolderJoins_OnlyTheSkillNobodyHeldYetIsAdded()
    {
        var added = BuffGrantRules.AddedSkills([FoldGlider], [Grants(FoldGlider), Grants(FoldGlider, GliderBoost)]);

        await Assert.That(added).IsEquivalentTo(new[] { GliderBoost });
    }

    [Test]
    public async Task WinningSwaps_SameOrigin_HighestPriorityWins()
    {
        var winners = BuffGrantRules.WinningSwaps(
        [
            Swaps(StanceOrigin, FireballStance, priority: 0, rowId: 10, buffId: 18382),
            Swaps(StanceOrigin, FrostStance, priority: 1, rowId: 9, buffId: 18383)
        ]);

        await Assert.That(winners).HasCount().EqualTo(1);
        await Assert.That(winners[0].NewSkillId).IsEqualTo(FrostStance);
    }

    [Test]
    public async Task WinningSwaps_SameOriginAndPriority_LowestRowIdWins()
    {
        // Two 0-priority rows cannot both own the entry, and the winner must not depend on which buff
        // happened to land first.
        var winners = BuffGrantRules.WinningSwaps(
        [
            Swaps(StanceOrigin, FrostStance, priority: 0, rowId: 12, buffId: 18382),
            Swaps(StanceOrigin, FireballStance, priority: 0, rowId: 10, buffId: 18381)
        ]);

        await Assert.That(winners).HasCount().EqualTo(1);
        await Assert.That(winners[0].NewSkillId).IsEqualTo(FireballStance);
    }

    [Test]
    public async Task HeldSkills_SwapHandsOverTheReplacementAndTakesTheOriginOffTheBar()
    {
        var swap = Swaps(StanceOrigin, FrostStance, priority: 0, rowId: 9);

        await Assert.That(BuffGrantRules.HeldSkills([swap])).IsEquivalentTo(new[] { FrostStance });
        await Assert.That(BuffGrantRules.ReplacedOrigins([swap])).IsEquivalentTo(new[] { StanceOrigin });
    }

    [Test]
    public async Task HeldSkills_OriginHeldByAnotherBuffIsStillTakenOffTheBar()
    {
        // One buff grants the origin directly, another swaps it away: both cannot occupy the entry.
        var held = BuffGrantRules.HeldSkills([Grants(StanceOrigin), Swaps(StanceOrigin, FrostStance, 0, 9)]);

        await Assert.That(held).IsEquivalentTo(new[] { FrostStance });
    }

    [Test]
    public async Task ReleasedSkills_HigherPrioritySwapEnds_LowerPriorityReplacementTakesOver()
    {
        var high = Swaps(StanceOrigin, FrostStance, priority: 1, rowId: 9, buffId: 18383);
        var low = Swaps(StanceOrigin, FireballStance, priority: 0, rowId: 10, buffId: 18382);

        var held = BuffGrantRules.HeldSkills([high, low]);
        await Assert.That(held).IsEquivalentTo(new[] { FrostStance });

        // The priority-1 stance drops: the other swap takes the entry back and its skill has to be added.
        var released = BuffGrantRules.ReleasedSkills(held, [low]);
        var added = BuffGrantRules.AddedSkills(held, [low]);
        await Assert.That(released).IsEquivalentTo(new[] { FrostStance });
        await Assert.That(added).IsEquivalentTo(new[] { FireballStance });
        await Assert.That(BuffGrantRules.ReplacedOrigins([low])).IsEquivalentTo(new[] { StanceOrigin });
    }

    [Test]
    public async Task HeldSkills_DifferentOrigins_AreReplacedIndependently()
    {
        var held = BuffGrantRules.HeldSkills(
        [
            Swaps(StanceOrigin, FrostStance, 0, 9),
            Swaps(HarpoonLanding, FoldGlider, 0, 11)
        ]);

        await Assert.That(held).IsEquivalentTo(new[] { FrostStance, FoldGlider });
        await Assert.That(BuffGrantRules.ReplacedOrigins(
                [Swaps(StanceOrigin, FrostStance, 0, 9), Swaps(HarpoonLanding, FoldGlider, 0, 11)]))
            .IsEquivalentTo(new[] { StanceOrigin, HarpoonLanding });
    }

    [Test]
    public async Task HeldPassives_IsTheUnionOfTheActiveHolders()
    {
        await Assert.That(BuffGrantRules.HeldPassives([Passives(PassiveA), Passives(PassiveA, PassiveB)]))
            .IsEquivalentTo(new[] { PassiveA, PassiveB });
        await Assert.That(BuffGrantRules.HeldPassives([Passives(PassiveA)])).IsEquivalentTo(new[] { PassiveA });
        await Assert.That(BuffGrantRules.HeldPassives([])).IsEmpty();
    }

    [Test]
    public async Task HeldSkills_ZeroIdsAndEmptySets_AreIgnored()
    {
        var held = BuffGrantRules.HeldSkills([null, BuffGrantSet.Empty, Grants(0, FoldGlider)]);

        await Assert.That(held).IsEquivalentTo(new[] { FoldGlider });
    }
}
