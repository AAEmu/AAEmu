using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestLetItDoneAdvanceRulesTests
{
    [Test]
    public async Task ResolveProgress_ReportPath_AlwaysStaysInProgress()
    {
        // Classic LetItDone: wait for a report even when objectives already passed.
        await Assert.That(QuestLetItDoneAdvanceRules.ResolveProgressResult(
            rewardsDistributed: true,
            objectiveMet: true,
            autoCompletesWithoutReport: false)).IsFalse();
    }

    [Test]
    public async Task ResolveProgress_AutoCompleteWithoutReport_LeavesWhenActsMet()
    {
        await Assert.That(QuestLetItDoneAdvanceRules.ResolveProgressResult(
            rewardsDistributed: true,
            objectiveMet: true,
            autoCompletesWithoutReport: true)).IsTrue();
    }

    [Test]
    public async Task ResolveProgress_ExactCount_LeavesEvenThoughOverachieveGateWouldFail()
    {
        // QuestStep used to pass the overachieve gate (3/2) as the result. An exact 2/2 is
        // QuestComplete, so that gate is false, and the auto-complete path must still advance.
        await Assert.That(QuestLetItDoneAdvanceRules.ResolveProgressResult(
            rewardsDistributed: true,
            objectiveMet: true,
            autoCompletesWithoutReport: true)).IsTrue();
    }

    [Test]
    public async Task ResolveProgress_AutoCompleteWithoutReport_StaysWhenActsNotMet()
    {
        await Assert.That(QuestLetItDoneAdvanceRules.ResolveProgressResult(
            rewardsDistributed: true,
            objectiveMet: false,
            autoCompletesWithoutReport: true)).IsFalse();

        await Assert.That(QuestLetItDoneAdvanceRules.ResolveProgressResult(
            rewardsDistributed: false,
            objectiveMet: true,
            autoCompletesWithoutReport: true)).IsFalse();
    }

    [Test]
    public async Task AutoCompletesWithoutReport_TrueForAutoCompleteOnly()
    {
        var parent = NewComponentParent();
        var acts = new QuestActTemplate[]
        {
            new QuestActObjSphere(parent),
            new QuestActConAutoComplete(parent),
            new QuestActSupplyExp(parent),
        };

        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(acts)).IsTrue();
    }

    [Test]
    public async Task AutoCompletesWithoutReport_FalseWhenAnyReportActExists()
    {
        var parent = NewComponentParent();

        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(
        [
            new QuestActConAutoComplete(parent),
            new QuestActConReportNpc(parent),
        ])).IsFalse();

        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(
        [
            new QuestActConAutoComplete(parent),
            new QuestActConReportNpcGroup(parent),
        ])).IsFalse();

        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(
        [
            new QuestActConAutoComplete(parent),
            new QuestActConReportDoodad(parent),
        ])).IsFalse();

        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(
        [
            new QuestActConAutoComplete(parent),
            new QuestActConReportJournal(parent),
        ])).IsFalse();
    }

    [Test]
    public async Task AutoCompletesWithoutReport_FalseWithoutAutoComplete()
    {
        var parent = NewComponentParent();
        await Assert.That(QuestLetItDoneAdvanceRules.AutoCompletesWithoutReport(
        [
            new QuestActObjSphere(parent),
            new QuestActSupplyExp(parent),
        ])).IsFalse();
    }

    private static QuestComponentTemplate NewComponentParent()
    {
        var template = new QuestTemplate { Id = 1, LetItDone = true };
        return new QuestComponentTemplate(template) { Id = 1 };
    }
}
