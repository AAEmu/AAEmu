using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Walks the Start-component rows of the quests the audit listed (checked in as
/// <see cref="QuestStartRequirementContentSnapshot"/>) through the kind switch. The owner is null,
/// so the walk proves that each row dispatches to a handled case and carries its display gate; the
/// outcome of a handled case needs the character state the case reads.
/// </summary>
public class QuestStartRequirementContentTests
{
    private static UnitReqs Row((uint Kind, uint Value1, uint Value2, uint Value3, bool DisplayMessage, int Rows, int Quests, uint RowId, uint QuestId, uint ComponentId) row) =>
        new()
        {
            Id = row.RowId,
            OwnerId = row.ComponentId,
            OwnerType = "QuestComponent",
            KindType = (UnitReqsKindType)row.Kind,
            Value1 = row.Value1,
            Value2 = row.Value2,
            Value3 = row.Value3,
            DisplayMessage = row.DisplayMessage
        };

    [Test]
    public async Task EveryStartKind_IsADeclaredKind()
    {
        foreach (var kind in QuestStartRequirementContentSnapshot.StartKinds)
            await Assert.That(Enum.IsDefined(typeof(UnitReqsKindType), kind.Kind)).IsTrue();
    }

    [Test]
    public async Task AffectedRows_DispatchToAHandledCase()
    {
        foreach (var row in QuestStartRequirementContentSnapshot.AffectedRows)
        {
            var result = Row(row).Validate(null, null);

            await Assert.That(result.ResultKey).IsNotEqualTo(SkillResultKeys.skill_urk_unknown);
            // Every affected kind refuses a null owner, so the gate is the row's display_msg on each one.
            await Assert.That(result.ResultKey).IsNotEqualTo(SkillResultKeys.ok);
            await Assert.That(result.DisplayMessage).IsEqualTo(row.DisplayMessage);
        }
    }

    [Test]
    public async Task FactionChangeChain_FailsClosedWithPlainFailure()
    {
        // 123/124/125 have their client rule recovered but no server state; the native handlers of all
        // three write FAILURE with zero details, which is what the fail-closed path reports.
        var chain = QuestStartRequirementContentSnapshot.AffectedRows
            .Where(row => QuestStartRequirementContentSnapshot.StillClosedKinds.Contains(row.Kind))
            .ToList();

        await Assert.That(chain.Count).IsEqualTo(10);
        foreach (var row in chain)
        {
            var result = Row(row).Validate(null, null);

            await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_failure);
            await Assert.That(result.NativeResult).IsNull();
            await Assert.That(result.ResultUShort).IsEqualTo((ushort)0);
            await Assert.That(result.ResultUInt).IsEqualTo(0u);
        }
    }

    [Test]
    public async Task OtherAffectedRows_DoNotFailClosed()
    {
        // A plain skill_failure without a native byte is the fail-closed shape; every other affected kind
        // answers with its own key or the native byte its client handler writes.
        foreach (var row in QuestStartRequirementContentSnapshot.AffectedRows)
        {
            if (QuestStartRequirementContentSnapshot.StillClosedKinds.Contains(row.Kind))
                continue;

            var result = Row(row).Validate(null, null);

            await Assert.That(result.ResultKey != SkillResultKeys.skill_failure || result.NativeResult != null).IsTrue();
        }
    }

    [Test]
    public async Task Snapshot_CountsAgree()
    {
        var kinds = QuestStartRequirementContentSnapshot.StartKinds.ToDictionary(kind => kind.Kind);

        foreach (var group in QuestStartRequirementContentSnapshot.AffectedRows.GroupBy(row => row.Kind))
            await Assert.That(group.Sum(row => row.Rows)).IsEqualTo(kinds[group.Key].Rows);

        foreach (var kind in QuestStartRequirementContentSnapshot.AuditUnknownKinds)
            await Assert.That(kinds.ContainsKey(kind)).IsTrue();

        // Each faction change quest carries a FactionPower row, so the chain is the kind 123 quest count.
        await Assert.That(kinds[123].Quests).IsEqualTo(QuestStartRequirementContentSnapshot.FactionChangeChainQuests);
        await Assert.That(kinds[QuestStartRequirementContentSnapshot.NationMemberKind].Quests).IsEqualTo(64);
        await Assert.That(QuestStartRequirementContentSnapshot.AuditUnknownQuests).IsEqualTo(163);
        await Assert.That(QuestStartRequirementContentSnapshot.QuestsWithStartRequirements).IsEqualTo(5026);
    }
}
