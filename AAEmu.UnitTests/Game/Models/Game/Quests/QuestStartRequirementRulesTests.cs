using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestStartRequirementRulesTests
{
    private static UnitReqsValidationResult Ok() => new(SkillResultKeys.ok, 0, 0);

    private static UnitReqsValidationResult Fail(
        SkillResultKeys key = SkillResultKeys.skill_urk_level,
        ushort detail = 0,
        uint value = 0,
        bool display = true) =>
        new(key, detail, value) { DisplayMessage = display };

    /// <summary>Yields the rows one at a time and records which ones the walk asked for.</summary>
    private static IEnumerable<UnitReqsValidationResult> Walk(List<int> visited, params UnitReqsValidationResult[] rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            visited.Add(i);
            yield return rows[i];
        }
    }

    [Test]
    public async Task EmptyList_PassesInBothModes()
    {
        // Answers success before looking at the mode when the list is empty.
        await Assert.That(QuestStartRequirementRules.Evaluate(false, []).ResultKey).IsEqualTo(SkillResultKeys.ok);
        await Assert.That(QuestStartRequirementRules.Evaluate(true, []).ResultKey).IsEqualTo(SkillResultKeys.ok);
    }

    [Test]
    public async Task And_AllPassing_Passes()
    {
        var result = QuestStartRequirementRules.Evaluate(false, [Ok(), Ok(), Ok()]);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
        await Assert.That(result.DisplayMessage).IsTrue();
    }

    [Test]
    public async Task And_ReturnsTheFirstFailingRowAsIs()
    {
        var refusing = Fail(SkillResultKeys.skill_urk_gender, display: false);

        var result = QuestStartRequirementRules.Evaluate(false, [Ok(), refusing, Fail()]);

        await Assert.That(result).IsSameReferenceAs(refusing);
        await Assert.That(result.DisplayMessage).IsFalse();
    }

    [Test]
    public async Task And_StopsAtTheFirstFailure()
    {
        var visited = new List<int>();

        QuestStartRequirementRules.Evaluate(false, Walk(visited, Ok(), Fail(), Fail(), Ok()));

        await Assert.That(visited).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task And_KeepsTheFailingRowsDetails()
    {
        var result = QuestStartRequirementRules.Evaluate(
            false, [Fail(SkillResultKeys.skill_urk_expedition_member, detail: 0x328, value: 7)]);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_urk_expedition_member);
        await Assert.That(result.ResultUShort).IsEqualTo((ushort)0x328);
        await Assert.That(result.ResultUInt).IsEqualTo(7u);
    }

    [Test]
    public async Task Or_FirstPassingRowEndsTheWalkWithSuccess()
    {
        var visited = new List<int>();

        var result = QuestStartRequirementRules.Evaluate(true, Walk(visited, Fail(display: false), Ok(), Fail()));

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
        await Assert.That(result.DisplayMessage).IsTrue();
        await Assert.That(visited).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task Or_Exhausted_AnswersUnitReqsOrFailWithTheGateOn()
    {
        // The exhausted OR group writes 0x31 with zero details and gate 1, whatever the rows carried.
        var result = QuestStartRequirementRules.Evaluate(
            true, [Fail(detail: 9, value: 9, display: false), Fail(SkillResultKeys.skill_urk_gender, display: false)]);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_unit_reqs_or_fail);
        await Assert.That(result.ResultUShort).IsEqualTo((ushort)0);
        await Assert.That(result.ResultUInt).IsEqualTo(0u);
        await Assert.That(result.DisplayMessage).IsTrue();
        await Assert.That(result.NativeResult).IsNull();
    }

    [Test]
    public async Task Or_SingleFailingRow_IsStillTheGroupAnswer()
    {
        var result = QuestStartRequirementRules.Evaluate(true, [Fail()]);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_unit_reqs_or_fail);
    }

    [Test]
    public async Task Passes_NeedsAnOkResult()
    {
        await Assert.That(QuestStartRequirementRules.Passes(Ok())).IsTrue();
        await Assert.That(QuestStartRequirementRules.Passes(Fail())).IsFalse();
        await Assert.That(QuestStartRequirementRules.Passes(null)).IsFalse();
    }

    [Test]
    public async Task WireResult_MapsTheKey()
    {
        await Assert.That(QuestStartRequirementRules.WireResult(Fail())).IsEqualTo(SkillResult.UrkLevel);
        await Assert.That(QuestStartRequirementRules.WireResult(Ok())).IsEqualTo(SkillResult.Success);
        await Assert.That(QuestStartRequirementRules.WireResult(Fail(SkillResultKeys.skill_unit_reqs_or_fail)))
            .IsEqualTo(SkillResult.UnitReqsOrFail);
    }

    [Test]
    public async Task WireResult_PrefersTheNativeByte()
    {
        // A kind whose native byte has no key member reports skill_failure with NativeResult set; the
        // wire carries the native byte so the client formats the message its own evaluator would.
        var native = Fail(SkillResultKeys.skill_failure);
        native.NativeResult = SkillResult.UrkDominionMember;

        await Assert.That(QuestStartRequirementRules.WireResult(native)).IsEqualTo(SkillResult.UrkDominionMember);
        await Assert.That((byte)QuestStartRequirementRules.WireResult(native)).IsEqualTo((byte)0x9C);
    }
}
