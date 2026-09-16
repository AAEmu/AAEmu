using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class EquipSlotReinforceRulesTests
{
    private static EquipSlotReinforceStep Step(byte level, int needExp) =>
        new() { SlotTypeId = 15, Level = level, NeedExp = needExp, Attribute = EquipSlotReinforceAttribute.Offence };

    private static EquipSlotReinforceState State(byte slot, sbyte level, int exp) =>
        new() { SlotTypeId = slot, Level = level, Exp = exp };

    [Test]
    public async Task ExpAccepted_BanksOnlyWhatTheBarHasRoomFor()
    {
        // 200 needed, 150 banked, a 100 feed: 50 lands, the rest is discarded.
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(150, 200, 100)).IsEqualTo(50);
    }

    [Test]
    public async Task ExpAccepted_GrantsTheWholeFeedWhenItFits()
    {
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(0, 200, 100)).IsEqualTo(100);
    }

    [Test]
    public async Task ExpAccepted_RefusesWhenTheBarIsFullOrTheFeedIsEmpty()
    {
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(200, 200, 100)).IsEqualTo(0);
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(250, 200, 100)).IsEqualTo(0);
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(0, 200, 0)).IsEqualTo(0);
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(0, 200, -5)).IsEqualTo(0);
    }

    [Test]
    public async Task ExpAccepted_OnTheLastStepAcceptsNothing_BecauseItNeedsNoExp()
    {
        // The top of a ladder ships need_exp 0: the bar is full the moment the level is reached, and
        // the final level is bought with its item rather than with experience.
        await Assert.That(EquipSlotReinforceRules.ExpAccepted(0, 0, 500)).IsEqualTo(0);
    }

    [Test]
    public async Task CanLevelUp_NeedsAFullBarForTheVeryNextLevel()
    {
        var next = Step(3, 1200);

        await Assert.That(EquipSlotReinforceRules.CanLevelUp(2, 1200, next)).IsTrue();
        await Assert.That(EquipSlotReinforceRules.CanLevelUp(2, 1199, next)).IsFalse();
        // A bar that is full for a different level is not a level-up.
        await Assert.That(EquipSlotReinforceRules.CanLevelUp(1, 5000, next)).IsFalse();
        await Assert.That(EquipSlotReinforceRules.CanLevelUp(2, 1200, null)).IsFalse();
    }

    [Test]
    public async Task NextStep_WalksTheLadderAndEndsAtTheTop()
    {
        var ladder = new[] { Step(1, 200), Step(2, 700), Step(3, 1200) };

        await Assert.That(EquipSlotReinforceRules.NextStep(0, ladder)?.NeedExp).IsEqualTo(200);
        await Assert.That(EquipSlotReinforceRules.NextStep(2, ladder)?.NeedExp).IsEqualTo(1200);
        await Assert.That(EquipSlotReinforceRules.NextStep(3, ladder)).IsNull();
    }

    [Test]
    public async Task AttributeTotal_SumsOnlyTheSlotsOfThatAttribute()
    {
        var states = new[] { State(15, 4, 0), State(16, 6, 0), State(0, 3, 0) };

        EquipSlotReinforceAttribute? AttributeOf(byte slot) => slot switch
        {
            15 or 16 => EquipSlotReinforceAttribute.Offence,
            0 => EquipSlotReinforceAttribute.Defence,
            _ => null
        };

        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Offence, states, AttributeOf))
            .IsEqualTo(10);
        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Defence, states, AttributeOf))
            .IsEqualTo(3);
        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Support, states, AttributeOf))
            .IsEqualTo(0);
    }

    [Test]
    public async Task AttributeTotal_IgnoresSlotsWithNoAttribute()
    {
        var states = new[] { State(15, 4, 0), State(99, 7, 0) };

        EquipSlotReinforceAttribute? AttributeOf(byte slot) =>
            slot == 15 ? EquipSlotReinforceAttribute.Offence : null;

        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Offence, states, AttributeOf))
            .IsEqualTo(4);
    }

    [Test]
    public async Task SetEffectLevel_TakesTheHighestThresholdReached()
    {
        var effects = new[]
        {
            new EquipSlotReinforceSetEffect { Attribute = EquipSlotReinforceAttribute.Offence, RequireReinforceLevel = 15, Level = 1 },
            new EquipSlotReinforceSetEffect { Attribute = EquipSlotReinforceAttribute.Offence, RequireReinforceLevel = 17, Level = 2 },
            new EquipSlotReinforceSetEffect { Attribute = EquipSlotReinforceAttribute.Defence, RequireReinforceLevel = 15, Level = 3 }
        };

        await Assert.That(EquipSlotReinforceRules.SetEffectLevel(EquipSlotReinforceAttribute.Offence, 14, effects)).IsEqualTo((byte)0);
        await Assert.That(EquipSlotReinforceRules.SetEffectLevel(EquipSlotReinforceAttribute.Offence, 15, effects)).IsEqualTo((byte)1);
        await Assert.That(EquipSlotReinforceRules.SetEffectLevel(EquipSlotReinforceAttribute.Offence, 40, effects)).IsEqualTo((byte)2);
        // A defence threshold does not pay out to offence.
        await Assert.That(EquipSlotReinforceRules.SetEffectLevel(EquipSlotReinforceAttribute.Offence, 16, effects)).IsEqualTo((byte)1);
    }

    [Test]
    public async Task BundleEffectLevel_NeedsAllThreeThresholds()
    {
        var effects = new[]
        {
            new EquipSlotReinforceBundleEffect { BundleEffectLevel = 1, RequireOffenseLevel = 12, RequireDefenseLevel = 28, RequireSupportLevel = 12 },
            new EquipSlotReinforceBundleEffect { BundleEffectLevel = 2, RequireOffenseLevel = 21, RequireDefenseLevel = 49, RequireSupportLevel = 18 }
        };

        await Assert.That(EquipSlotReinforceRules.BundleEffectLevel(12, 28, 11, effects)).IsEqualTo((byte)0);
        await Assert.That(EquipSlotReinforceRules.BundleEffectLevel(12, 28, 12, effects)).IsEqualTo((byte)1);
        await Assert.That(EquipSlotReinforceRules.BundleEffectLevel(30, 70, 24, effects)).IsEqualTo((byte)2);
    }

    [Test]
    public async Task EligibleLevelEffects_ListWhatTheLevelHasUnlocked()
    {
        var effects = new[]
        {
            new EquipSlotReinforceLevelEffect { SlotTypeId = 1, TriggerLevel = 2 },
            new EquipSlotReinforceLevelEffect { SlotTypeId = 1, TriggerLevel = 3 },
            new EquipSlotReinforceLevelEffect { SlotTypeId = 1, TriggerLevel = 4 },
            new EquipSlotReinforceLevelEffect { SlotTypeId = 0, TriggerLevel = 1 }
        };

        await Assert.That(EquipSlotReinforceRules.EligibleLevelEffects(1, 1, effects).Count).IsEqualTo(0);
        await Assert.That(EquipSlotReinforceRules.EligibleLevelEffects(1, 3, effects).Count).IsEqualTo(2);
        await Assert.That(EquipSlotReinforceRules.EligibleLevelEffects(1, 9, effects).Count).IsEqualTo(3);
    }

    [Test]
    public async Task NormalizeLevelEffectIndex_KeepsAChoiceThatIsStillValid()
    {
        await Assert.That(EquipSlotReinforceRules.NormalizeLevelEffectIndex(0, 3)).IsEqualTo(0);
        await Assert.That(EquipSlotReinforceRules.NormalizeLevelEffectIndex(1, 3)).IsEqualTo(1);
        // Nothing chosen yet, or the choice is out of range: fall back to the last eligible effect.
        await Assert.That(EquipSlotReinforceRules.NormalizeLevelEffectIndex(-1, 3)).IsEqualTo(2);
        await Assert.That(EquipSlotReinforceRules.NormalizeLevelEffectIndex(7, 3)).IsEqualTo(2);
        // No eligible effect at all.
        await Assert.That(EquipSlotReinforceRules.NormalizeLevelEffectIndex(0, 0)).IsEqualTo(-1);
    }

    [Test]
    public async Task ApplyExp_BanksWhatFitsAndReportsWhenTheBarIsFull()
    {
        var ladder = new[] { Step(1, 200), Step(2, 700) };
        var state = State(15, 0, 0);

        await Assert.That(EquipSlotReinforceRules.ApplyExp(state, ladder, 150))
            .IsEqualTo(EquipSlotReinforceChange.ExpGained);
        await Assert.That(state.Exp).IsEqualTo(150);

        // The surplus is discarded, not carried: 100 more into 50 of room caps the bar at 200.
        await Assert.That(EquipSlotReinforceRules.ApplyExp(state, ladder, 100))
            .IsEqualTo(EquipSlotReinforceChange.ExpCapped);
        await Assert.That(state.Exp).IsEqualTo(200);
    }

    [Test]
    public async Task ApplyExp_RefusesAtTheTopOfTheLadder()
    {
        var ladder = new[] { Step(1, 200) };
        var state = State(15, 1, 0);

        await Assert.That(EquipSlotReinforceRules.ApplyExp(state, ladder, 500))
            .IsEqualTo(EquipSlotReinforceChange.Refused);
        await Assert.That(state.Exp).IsEqualTo(0);
    }

    [Test]
    public async Task TryLevelUp_TakesTheLevelAndStartsAFreshBar()
    {
        var ladder = new[] { Step(1, 200), Step(2, 700) };
        var state = State(15, 1, 700);

        var change = EquipSlotReinforceRules.TryLevelUp(state, ladder, out var reached);

        await Assert.That(change).IsEqualTo(EquipSlotReinforceChange.LeveledUp);
        await Assert.That(state.Level).IsEqualTo((sbyte)2);
        await Assert.That(state.Exp).IsEqualTo(0);
        await Assert.That(reached?.NeedExp).IsEqualTo(700);
    }

    [Test]
    public async Task TryLevelUp_RefusesOnAPartialBarAndLeavesItAlone()
    {
        var ladder = new[] { Step(1, 200), Step(2, 700) };
        var state = State(15, 1, 699);

        var change = EquipSlotReinforceRules.TryLevelUp(state, ladder, out var reached);

        await Assert.That(change).IsEqualTo(EquipSlotReinforceChange.Refused);
        await Assert.That(reached).IsNull();
        await Assert.That(state.Level).IsEqualTo((sbyte)1);
        await Assert.That(state.Exp).IsEqualTo(699);
    }

    [Test]
    public async Task TryLevelUp_RefusesOnceTheLadderEnds()
    {
        var ladder = new[] { Step(1, 200) };
        var state = State(15, 1, 0);

        await Assert.That(EquipSlotReinforceRules.TryLevelUp(state, ladder, out _))
            .IsEqualTo(EquipSlotReinforceChange.Refused);
        await Assert.That(state.Level).IsEqualTo((sbyte)1);
    }
}
