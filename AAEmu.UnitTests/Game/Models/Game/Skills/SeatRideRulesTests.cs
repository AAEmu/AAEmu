using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Which seats run their timeout trigger when the rider stands up. The chains below mirror the shipped
/// shapes: the floor mover walks timeout -> skill -> buff -> buff -> Started -> Blink, while the
/// meditation cushion walks timeout -> skill -> reward buff with no trigger left to follow.
/// </summary>
public class SeatRideRulesTests
{
    private static BuffTriggerTemplate Timeout(EffectTemplate effect) =>
        new() { Kind = BuffEventTriggerKind.Timeout, Effect = effect };

    private static BuffTriggerTemplate Started(EffectTemplate effect) =>
        new() { Kind = BuffEventTriggerKind.Started, Effect = effect };

    private static BuffEffect Applies(uint buffId) => new() { Buff = new BuffTemplate { Id = buffId } };

    private static SpecialEffect SkillUse(uint skillId) =>
        new() { SpecialEffectTypeId = SpecialType.SkillUse, Value1 = (int)skillId };

    private static SpecialEffect Blink() =>
        new() { SpecialEffectTypeId = SpecialType.Blink, Value1 = 10, Value2 = 15 };

    private static SkillTemplate SkillOf(params EffectTemplate[] effects) =>
        new() { Effects = effects.Select(e => new SkillEffect { Template = e }).ToList() };

    private static Func<uint, SkillTemplate> Resolver(params (uint Id, SkillTemplate Skill)[] skills)
    {
        var map = skills.ToDictionary(x => x.Id, x => x.Skill);
        return id => map.GetValueOrDefault(id);
    }

    private static Func<uint, IEnumerable<BuffTriggerTemplate>> TriggerResolver(
        params (uint Buff, BuffTriggerTemplate[] Triggers)[] entries)
    {
        var map = entries.ToDictionary(x => x.Buff, x => (IEnumerable<BuffTriggerTemplate>)x.Triggers);
        return id => map.TryGetValue(id, out var value) ? value : [];
    }

    [Test]
    public async Task LiftRide_WalksToTheBlink_AndTimesOutOnUnbond()
    {
        // seat skill -> buff 23741 (timeout casts 40229) -> buff 23742 (timeout applies 23743)
        // -> buff 23743 (started blinks)
        var resolver = Resolver(
            (40228, SkillOf(Applies(23741))),
            (40229, SkillOf(Applies(23742))));
        var triggers = TriggerResolver(
            (23741, [Timeout(SkillUse(40229))]),
            (23742, [Timeout(Applies(23743))]),
            (23743, [Started(Blink())]));

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(40228, resolver, triggers)).IsTrue();
    }

    [Test]
    public async Task MeditationCushion_EndsInARewardBuff_AndDoesNotTimeOutOnUnbond()
    {
        // seat skill -> buff 29851 (timeout casts 48042) -> buff 29854, which has no trigger left
        var resolver = Resolver(
            (48017, SkillOf(Applies(29851))),
            (48042, SkillOf(Applies(29854))));
        var triggers = TriggerResolver((29851, [Timeout(SkillUse(48042))]));

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(48017, resolver, triggers)).IsFalse();
    }

    [Test]
    public async Task Bed_TimeoutAppliesABuff_AndDoesNotTimeOutOnUnbond()
    {
        var resolver = Resolver((20383, SkillOf(Applies(4895))));
        var triggers = TriggerResolver((4895, [Timeout(Applies(5208))]));

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(20383, resolver, triggers)).IsFalse();
    }

    [Test]
    public async Task DirectBlinkTrigger_IsARide_WithoutResolvers()
    {
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond([Timeout(Blink())])).IsTrue();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond([Started(Blink())])).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond([Timeout(SkillUse(40229))])).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(Array.Empty<BuffTriggerTemplate>())).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond((IEnumerable<BuffTriggerTemplate>)null)).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(0)).IsFalse();
    }

    [Test]
    public async Task LiftRideThroughAPlot_IsFoundInThePlotStartEvent()
    {
        // seat skill -> buff 31410 (timeout casts 49288) -> 49288 runs plot 6567, whose start event blinks
        var resolver = Resolver(
            (49287, SkillOf(Applies(31410))),
            (49288, new SkillTemplate { Effects = [], Plot = new Plot { Id = 6567 } }));
        var triggers = TriggerResolver((31410, [Timeout(SkillUse(49288))]));
        Func<uint, IEnumerable<EffectTemplate>> plotEffects = id => id == 6567 ? [Blink()] : [];

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(49287, resolver, triggers, plotEffects)).IsTrue();
    }

    [Test]
    public async Task PlotWithoutAMove_IsNotARide()
    {
        var resolver = Resolver((1, new SkillTemplate { Effects = [], Plot = new Plot { Id = 2 } }));
        var triggers = TriggerResolver((900001, [Timeout(Applies(1))]));
        Func<uint, IEnumerable<EffectTemplate>> plotEffects = _ => [Applies(900002)];

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(1, resolver, triggers, plotEffects)).IsFalse();
    }

    [Test]
    public async Task EndlessHop_StopsAtTheDepthLimit()
    {
        // Every hop applies the same buff again: the walk must give up instead of recursing forever.
        var loop = 900000u;
        var resolver = Resolver((1, SkillOf(Applies(loop))));
        var triggers = TriggerResolver((loop, [Timeout(Applies(loop)), Timeout(SkillUse(1))]));

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(1, resolver, triggers)).IsFalse();
    }
}
