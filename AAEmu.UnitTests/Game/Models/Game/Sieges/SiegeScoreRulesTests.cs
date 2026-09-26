using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.UnitTests.Game.Models.Game.Sieges;

public class SiegeScoreRulesTests
{
    // The shipped content_configs win points.
    private static readonly SiegeWinPoints WinPoints = new(Defense: 1000, Offense: 100, Outlaw: 100);

    private const uint Raider = 114;
    private const uint Defender = 148;
    private const uint Attacker = 149;

    private static SiegeFactionRoles Roles() => SiegeFactionRoles.FromRows(
    [
        new SiegeFactionRole(Raider, 15, false, true),
        new SiegeFactionRole(Defender, 50, true, true),
        new SiegeFactionRole(Attacker, 50, true, true),
    ]);

    private static SiegeScoreState Score(uint outlaw = 0, uint defense = 0, uint offense = 0) => new()
    {
        ZoneGroupId = 33,
        OutlawPoint = outlaw,
        DefensePoint = defense,
        OffensePoint = offense,
    };

    [Test]
    public async Task Reached_IsTrueAtTheWinPointAndBelowIt()
    {
        var winPoint = WinPoints.For(SiegeScoreSide.Offense);

        await Assert.That(SiegeScoreRules.Reached(Score(offense: winPoint - 1), WinPoints, SiegeScoreSide.Offense))
            .IsFalse();
        await Assert.That(SiegeScoreRules.Reached(Score(offense: winPoint), WinPoints, SiegeScoreSide.Offense))
            .IsTrue();
        await Assert.That(SiegeScoreRules.Reached(Score(offense: winPoint + 1), WinPoints, SiegeScoreSide.Offense))
            .IsTrue();
    }

    [Test]
    public async Task ReachedSide_NamesTheOneSideThatGotThere()
    {
        await Assert.That(SiegeScoreRules.ReachedSide(Score(), WinPoints)).IsNull();
        await Assert.That(SiegeScoreRules.ReachedSide(Score(offense: 100), WinPoints))
            .IsEqualTo(SiegeScoreSide.Offense);
        await Assert.That(SiegeScoreRules.ReachedSide(Score(outlaw: 100), WinPoints))
            .IsEqualTo(SiegeScoreSide.Outlaw);
    }

    [Test]
    public async Task Resolve_HandsTheDominionToTheAttackingAllianceThatReachedItsWinPoint()
    {
        var decision = SiegeScoreRules.Resolve(Score(offense: 100), WinPoints, Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.OffenseBrokeThrough);
        await Assert.That(decision.WinnerFactionId).IsEqualTo(Attacker);
        await Assert.That(decision.DefenderFactionId).IsEqualTo(Defender);
        await Assert.That(decision.ChangesOwner).IsTrue();
    }

    [Test]
    public async Task Resolve_DoesNotHandTheDominionToAnAllianceThatCannotDefend()
    {
        // Faction 114 is the raider: siege_faction_troops gives it an offense troop and no defense troop, so
        // SiegeFactionRoles names it the one alliance that never holds ground. Giving it the dominion left
        // the next settlement unable to resolve in RequireDefender, and the zone group never left its siege
        // period again.
        var decision = SiegeScoreRules.Resolve(Score(outlaw: 100), WinPoints, Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.OutlawBrokeThrough);
        await Assert.That(decision.WinnerFactionId).IsEqualTo(0u);
        await Assert.That(decision.ChangesOwner).IsFalse();
        // The raider is still named in the reason, so the record says who broke through.
        await Assert.That(decision.Reason).Contains(Raider.ToString());
    }

    [Test]
    public async Task ARaiderBreakthroughLeavesASiegeThatCanStillBeSettledNextCycle()
    {
        // The regression itself: settle a cycle in which the raider wins, then settle the next one against
        // whatever the first recorded as the winner. Before the fix the recorded winner was 114, and
        // RequireDefender(114) threw for ever.
        var roles = Roles();
        var first = SiegeScoreRules.Resolve(Score(outlaw: 100), WinPoints, roles, Defender);
        var nextDefender = first.WinnerFactionId != 0 ? first.WinnerFactionId : Defender;

        var second = SiegeScoreRules.Resolve(Score(), WinPoints, roles, nextDefender);

        await Assert.That(second.Outcome).IsEqualTo(SiegeOutcome.DefenseHeld);
    }

    [Test]
    [Arguments(0u, 0u, 0u)]
    [Arguments(0u, 0u, 100u)]
    [Arguments(0u, 100u, 0u)]
    [Arguments(0u, 0u, 100u + 1u)]
    [Arguments(100u, 0u, 100u)]
    [Arguments(200u, 0u, 200u)]
    public async Task NoOutcomeEverRecordsAWinnerThatCannotDefend(uint outlaw, uint defense, uint offense)
    {
        // The property the backstop enforces, checked where it can actually be reached: across every
        // combination of reaching an attacker win point, nothing Resolve can produce is ever handed to an
        // alliance the next settlement's RequireDefender would refuse.
        var roles = Roles();
        var decision = SiegeScoreRules.Resolve(Score(outlaw, defense, offense), WinPoints, roles, Defender);

        if (decision.WinnerFactionId != 0)
        {
            await Assert.That(roles.CanDefend(decision.WinnerFactionId)).IsTrue();
            await Assert.That(decision.ChangesOwner).IsTrue();
        }
        else
        {
            await Assert.That(decision.ChangesOwner).IsFalse();
        }
    }

    [Test]
    public async Task Resolve_LeavesTheDominionWithTheDefenderWhenNeitherAttackerGotThere()
    {
        // The shipped defense condition: prevent the tower's magic power from being purified or destroyed
        // until the siege ends.
        var decision = SiegeScoreRules.Resolve(Score(outlaw: 99, defense: 1000, offense: 99), WinPoints,
            Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.DefenseHeld);
        // No winner is recorded: siege_settlements stores 0 for a siege nobody took, and the alliance that kept
        // the ground is already named in defender_faction_id.
        await Assert.That(decision.WinnerFactionId).IsEqualTo(0u);
        await Assert.That(decision.DefenderFactionId).IsEqualTo(Defender);
        await Assert.That(decision.ChangesOwner).IsFalse();
    }

    [Test]
    public async Task Resolve_RecordsNoWinnerForAContestedSiegeEither()
    {
        var decision = SiegeScoreRules.Resolve(Score(outlaw: 100, offense: 100), WinPoints, Roles(), Defender);

        await Assert.That(decision.WinnerFactionId).IsEqualTo(0u);
        await Assert.That(decision.DefenderFactionId).IsEqualTo(Defender);
    }

    [Test]
    public async Task Resolve_DoesNotHandTheDominionOverWhenTwoSidesReachedTheirWinPoints()
    {
        var decision = SiegeScoreRules.Resolve(Score(outlaw: 100, offense: 100), WinPoints, Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.Contested);
        await Assert.That(decision.WinnerFactionId).IsEqualTo(0u);
        await Assert.That(decision.ChangesOwner).IsFalse();
    }

    [Test]
    public async Task Resolve_IgnoresTheDefenseCounter()
    {
        // The defender's counter is the magic power it kept, which the guard-tower runtime totals up. It is
        // not consulted here: a defense counter over its own win point with no attacker over theirs is still
        // a defended siege.
        var decision = SiegeScoreRules.Resolve(Score(defense: uint.MaxValue), WinPoints, Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.DefenseHeld);
    }

    [Test]
    public async Task Resolve_RefusesADefenderTheContentCannotHaveHeldGround()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SiegeScoreRules.Resolve(Score(offense: 100), WinPoints, Roles(), Raider));
        await Assert.That(ex.Message).Contains("cannot defend");
    }

    [Test]
    public async Task Resolve_RecordsWhyInTheReason()
    {
        var decision = SiegeScoreRules.Resolve(Score(offense: 120), WinPoints, Roles(), Defender);

        await Assert.That(decision.Reason.Contains("120")).IsTrue();
    }
}
