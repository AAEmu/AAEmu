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
        await Assert.That(EquipSlotReinforceRules.CanLevelUp(9, 0, Step(10, 0))).IsFalse();
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
    public async Task DisplayLevel_IsOneMoreThanReached_ClampedToTheLadderTop()
    {
        await Assert.That(EquipSlotReinforceRules.DisplayLevel(0, 10)).IsEqualTo(1);
        await Assert.That(EquipSlotReinforceRules.DisplayLevel(9, 10)).IsEqualTo(10);
        await Assert.That(EquipSlotReinforceRules.DisplayLevel(10, 10)).IsEqualTo(10);
        await Assert.That(EquipSlotReinforceRules.DisplayLevel(3, 4)).IsEqualTo(4);
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

        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Offence, states, AttributeOf, _ => 10))
            .IsEqualTo(12);
        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Defence, states, AttributeOf, _ => 10))
            .IsEqualTo(4);
        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Support, states, AttributeOf, _ => 4))
            .IsEqualTo(0);
    }

    [Test]
    public async Task AttributeTotal_IgnoresSlotsWithNoAttribute()
    {
        var states = new[] { State(15, 4, 0), State(99, 7, 0) };

        EquipSlotReinforceAttribute? AttributeOf(byte slot) =>
            slot == 15 ? EquipSlotReinforceAttribute.Offence : null;

        await Assert.That(EquipSlotReinforceRules.AttributeTotal(EquipSlotReinforceAttribute.Offence, states, AttributeOf, _ => 10))
            .IsEqualTo(5);
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
    public async Task TierAtLevel_FindsOnlyTheTierThatLevelUnlocks()
    {
        var effects = new[]
        {
            new EquipSlotReinforceLevelEffect { Id = 1, SlotTypeId = 0, TriggerLevel = 5 },
            new EquipSlotReinforceLevelEffect { Id = 2, SlotTypeId = 0, TriggerLevel = 10 },
            new EquipSlotReinforceLevelEffect { Id = 3, SlotTypeId = 1, TriggerLevel = 5 }
        };

        await Assert.That(EquipSlotReinforceRules.TierAtLevel(0, 5, effects)?.Id).IsEqualTo(1u);
        await Assert.That(EquipSlotReinforceRules.TierAtLevel(0, 10, effects)?.Id).IsEqualTo(2u);
        // Level 7 sits between two tiers and unlocks neither, and another slot's tier is not this slot's.
        await Assert.That(EquipSlotReinforceRules.TierAtLevel(0, 7, effects)).IsNull();
        await Assert.That(EquipSlotReinforceRules.TierAtLevel(9, 5, effects)).IsNull();
    }

    [Test]
    public async Task TierAtLevel_IsWhatTheReplaceWindowNames()
    {
        // The Replace cast carries the slot and the trigger level of the line the player selected, measured
        // from the client: the chest sent 5 for its ★5 line and 10 for its ★10 line. Those two have to land on
        // two different tiers, or a replace would always re-roll the same one.
        var chest = new[]
        {
            new EquipSlotReinforceLevelEffect { Id = 7, SlotTypeId = 2, TriggerLevel = 5 },
            new EquipSlotReinforceLevelEffect { Id = 8, SlotTypeId = 2, TriggerLevel = 10 }
        };

        await Assert.That(EquipSlotReinforceRules.TierAtLevel(2, 5, chest)?.Id).IsEqualTo(7u);
        await Assert.That(EquipSlotReinforceRules.TierAtLevel(2, 10, chest)?.Id).IsEqualTo(8u);
    }

    private static EquipSlotReinforceUnitModifier Modifier(uint id, int weight, int value = 20) =>
        new() { Id = id, Weight = weight, Value = value, UnitAttributeId = id, UnitModifierTypeId = 0 };

    [Test]
    public async Task RollModifier_TakesTheRowWhoseWeightBandTheRollFallsIn()
    {
        // Three rows of weight 10 each: the pool is 0..29 and each row owns a third of it.
        var pool = new[] { Modifier(1, 10), Modifier(2, 10), Modifier(3, 10) };

        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 0)?.Id).IsEqualTo(1u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 9)?.Id).IsEqualTo(1u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 10)?.Id).IsEqualTo(2u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 29)?.Id).IsEqualTo(3u);
        // A roll beyond the pool wraps into it, and a negative one wraps too.
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 30)?.Id).IsEqualTo(1u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, -1)?.Id).IsEqualTo(3u);
    }

    [Test]
    public async Task RollModifier_WeightDecidesHowMuchOfThePoolARowOwns()
    {
        // 10 against 30: the first row takes rolls 0..9, the second everything from 10 to 39.
        var pool = new[] { Modifier(1, 10), Modifier(2, 30) };

        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 9)?.Id).IsEqualTo(1u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 10)?.Id).IsEqualTo(2u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 39)?.Id).IsEqualTo(2u);
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 40)?.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task RollModifier_SkipsWhatTheCharacterAlreadyObtained()
    {
        // An artifact effect can only be obtained once, so a held row is out of the pool entirely.
        var pool = new[] { Modifier(1, 10), Modifier(2, 10), Modifier(3, 10) };

        var roll = EquipSlotReinforceRules.RollModifier(pool, id => id is 1 or 2, 0);

        await Assert.That(roll?.Id).IsEqualTo(3u);
    }

    [Test]
    public async Task RollModifier_ReturnsNothingWhenTheTierHasNothingLeftToHandOut()
    {
        var pool = new[] { Modifier(1, 10), Modifier(2, 10) };

        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, _ => true, 0)).IsNull();
        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 0)?.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task RollModifier_IgnoresWeightlessRowsAndAnEmptyPool()
    {
        var pool = new[] { Modifier(1, 0), Modifier(2, -5) };

        await Assert.That(EquipSlotReinforceRules.RollModifier(pool, null, 0)).IsNull();
        await Assert.That(EquipSlotReinforceRules.RollModifier([], null, 0)).IsNull();
        await Assert.That(EquipSlotReinforceRules.RollModifier(null, null, 0)).IsNull();
    }

    [Test]
    public async Task HasRerollCandidate_ExcludesTheCurrentRowAndWhatIsAlreadyHeld()
    {
        var pool = new[] { Modifier(1, 10), Modifier(2, 10), Modifier(3, 10), Modifier(4, 0) };

        await Assert.That(EquipSlotReinforceRules.HasRerollCandidate(pool, 1, id => id == 2)).IsTrue();
        await Assert.That(EquipSlotReinforceRules.HasRerollCandidate(pool, 1, id => id is 2 or 3)).IsFalse();
        await Assert.That(EquipSlotReinforceRules.HasRerollCandidate(pool, 1, null)).IsTrue();
        await Assert.That(EquipSlotReinforceRules.HasRerollCandidate([], 1, null)).IsFalse();
        await Assert.That(EquipSlotReinforceRules.HasRerollCandidate(null, 1, null)).IsFalse();
    }

    [Test]
    public async Task PickConsumable_TakesTheFirstAlternativeTheCharacterHoldsInFull()
    {
        // A material's set members are alternatives: here either 5 singles or 1 bulk item pays for it.
        var members = new[] { (ItemId: 51594u, Count: 5), (ItemId: 211955u, Count: 1) };

        var bulk = EquipSlotReinforceRules.PickConsumable(members, itemId => itemId == 211955 ? 1 : 0);
        await Assert.That(bulk).IsEqualTo((211955u, 1));

        var singles = EquipSlotReinforceRules.PickConsumable(members, itemId => itemId == 51594 ? 5 : 0);
        await Assert.That(singles).IsEqualTo((51594u, 5));
    }

    [Test]
    public async Task PickConsumable_RefusesWhenNothingIsHeldInFull()
    {
        var members = new[] { (ItemId: 51594u, Count: 5), (ItemId: 211955u, Count: 1) };

        // Four singles is not five, and a partial alternative does not let the bulk one through either.
        await Assert.That(EquipSlotReinforceRules.PickConsumable(members, itemId => itemId == 51594 ? 4 : 0)).IsNull();
        await Assert.That(EquipSlotReinforceRules.PickConsumable(members, _ => 0)).IsNull();
        await Assert.That(EquipSlotReinforceRules.PickConsumable([], _ => 99)).IsNull();
        await Assert.That(EquipSlotReinforceRules.PickConsumable(null, _ => 99)).IsNull();
    }

    [Test]
    public async Task PickConsumable_IgnoresEmptyMembers()
    {
        var members = new[] { (ItemId: 0u, Count: 5), (ItemId: 51595u, Count: 0), (ItemId: 51596u, Count: 1) };

        var pick = EquipSlotReinforceRules.PickConsumable(members, _ => 0);

        await Assert.That(pick).IsNull();
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
