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
    public async Task Resolve_HandsTheDominionToTheRaiderWhenTheRaiderReachedItsWinPoint()
    {
        var decision = SiegeScoreRules.Resolve(Score(outlaw: 100), WinPoints, Roles(), Defender);

        await Assert.That(decision.Outcome).IsEqualTo(SiegeOutcome.OutlawBrokeThrough);
        await Assert.That(decision.WinnerFactionId).IsEqualTo(Raider);
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
