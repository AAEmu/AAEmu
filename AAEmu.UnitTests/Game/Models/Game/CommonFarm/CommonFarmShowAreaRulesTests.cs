using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.UnitTests.Game.Models.Game.CommonFarm;

/// <summary>
/// What a farm-area request may be answered with.
/// </summary>
/// <remarks>
/// The three answers fall on three different sides of their line, and the order is the rule: a
/// position that is not farm land is refused, farm land of another tab is cleared, and farm land of
/// the requested tab is answered. Each boundary is asserted from both sides, because a boundary that
/// is only tested from one side is a boundary that can be moved without a test noticing.
/// </remarks>
public class CommonFarmShowAreaRulesTests
{
    [Test]
    public async Task AFarmAreaOfTheRequestedTabIsAnswered()
    {
        var outcome = CommonFarmShowAreaRules.Evaluate(
            (int)FarmType.Farm, (int)FarmType.Farm, out var responseType);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Answer);
        await Assert.That(responseType).IsEqualTo((int)FarmType.Farm);
    }

    [Test]
    public async Task OffTheFarmIsRefusedAndSaysNothing()
    {
        // The refused side of the line. "Not a farm" is the absence of an area, and answering it
        // would put an area on the wire that the world does not have.
        var outcome = CommonFarmShowAreaRules.Evaluate(
            (int)FarmType.Farm, (int)FarmType.Invalid, out var responseType);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Refuse);
        await Assert.That(responseType).IsEqualTo(0);
    }

    [Test]
    public async Task ARequestForATabThatIsNotAFarmTabIsRefusedEvenOnFarmLand()
    {
        // Refused is asked about both sides: the land and the request. A request naming no tab cannot
        // be answered on farm land either, or the response would name a tab that does not exist.
        var outcome = CommonFarmShowAreaRules.Evaluate(
            (int)FarmType.Invalid, (int)FarmType.Farm, out var responseType);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Refuse);
        await Assert.That(responseType).IsEqualTo(0);
    }

    [Test]
    public async Task ARequestForATabOutsideTheKnownSetIsRefused()
    {
        // A value the enum does not define is not a tab. Accepting it would put an unnamed tab in a
        // response, and the caller's own resolution would be the only thing naming it.
        var outcome = CommonFarmShowAreaRules.Evaluate(9999, (int)FarmType.Farm, out _);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Refuse);
    }

    [Test]
    public async Task FarmLandOfAnotherTabIsClearedRatherThanAnsweredWithTheWrongPositions()
    {
        // The cleared side of the line. The positions that exist belong to another tab, and handing
        // them back would file them under the requested tab's name.
        var outcome = CommonFarmShowAreaRules.Evaluate(
            (int)FarmType.Ranch, (int)FarmType.Stable, out var responseType);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Clear);
        // The tab in a response is always the one the land carries, never the one asked for.
        await Assert.That(responseType).IsEqualTo((int)FarmType.Stable);
    }

    [Test]
    public async Task EveryFarmTabIsAnswerableForItself()
    {
        // Swept rather than spot-checked: a rule that treated one tab as special would still pass a
        // single assertion, and the tabs are the whole surface the request can name.
        foreach (var tab in Enum.GetValues<FarmType>().Where(t => t != FarmType.Invalid))
        {
            var outcome = CommonFarmShowAreaRules.Evaluate((int)tab, (int)tab, out var responseType);

            await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Answer);
            await Assert.That(responseType).IsEqualTo((int)tab);
        }
    }

    [Test]
    public async Task AnEmptyAreaIsAnsweredWithAZeroCountAndThatIsWhatClearsTheReader()
    {
        // An area with nothing planted needs no fourth outcome: the count resolves to zero, and a
        // count that is not positive is what makes the reader drop what it was holding. The two
        // halves are asserted together so the clear path cannot be lost if the count rule changes.
        var outcome = CommonFarmShowAreaRules.Evaluate(
            (int)FarmType.Farm, (int)FarmType.Farm, out var responseType);

        var resolved = CommonFarmShowAreaRules.TryResolveCount(0, 0, out var writeCount);

        await Assert.That(outcome).IsEqualTo(CommonFarmShowAreaOutcome.Answer);
        await Assert.That(resolved).IsTrue();
        await Assert.That(writeCount).IsEqualTo(0);
    }

    [Test]
    public async Task TheResponseNamesTheLandEvenWhenTheRequestNamedSomethingElse()
    {
        // Stated as its own assertion because it is the one fact a caller can silently get wrong by
        // echoing the request: the tab in the body is the one the position resolved to.
        CommonFarmShowAreaRules.Evaluate((int)FarmType.Nursery, (int)FarmType.Farm, out var responseType);

        await Assert.That(responseType).IsNotEqualTo((int)FarmType.Nursery);
        await Assert.That(responseType).IsEqualTo((int)FarmType.Farm);
    }
}
