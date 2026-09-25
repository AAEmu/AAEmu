using Microsoft.Data.Sqlite;

using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// Pins the reviewed C08 content matrix without giving the runtime a target-enumeration rule. The
/// baseline is a test oracle for the audit, not a gameplay default: new rows, links or orphans must be
/// reviewed before the matrix changes.
/// </summary>
public class LoseTargetingContentAuditTests
{
    private static readonly LoseTargetingAuditBaseline ReviewedMatrix =
        new(65, 50, LoseTargetingLinkScope.ServerEffectLinks);

    [Test]
    public async Task ReviewedMatrix_PinsTheDocumentedGap()
    {
        var result = CreateSnapshot(65, 50);

        result.ValidateAgainst(ReviewedMatrix);

        await Assert.That(result.ContentRowCount).IsEqualTo(65);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks)).IsEqualTo(50);
        await Assert.That(result.UnlinkedIds(LoseTargetingLinkScope.ServerEffectLinks).Count).IsEqualTo(15);
    }

    [Test]
    public async Task NewOrphan_FailsLoudlyAgainstTheReviewedMatrix()
    {
        var result = CreateSnapshot(66, 50);

        var error = Assert.Throws<InvalidDataException>(() => result.ValidateAgainst(ReviewedMatrix));

        await Assert.That(error!.Message).Contains("content=66");
        await Assert.That(error.Message).Contains("unlinked=16");
    }

    [Test]
    public async Task LinkScope_KeepsPlotRowsOutOfServerLinks()
    {
        var rows = new[]
        {
            new LoseTargetingContentRow(1, SpecialType.LoseTargetingTheTarget, 4, 0),
            new LoseTargetingContentRow(2, SpecialType.LoseTargetingTheTarget, 4, 0),
        };
        var links = new[]
        {
            new LoseTargetingContentLink(1, LoseTargetingLinkSource.PlotEffect),
        };

        var result = LoseTargetingContentAuditResult.Create(rows, links);

        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks)).IsEqualTo(0);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.AllContentLinks)).IsEqualTo(1);
        await Assert.That(result.UnlinkedIds(LoseTargetingLinkScope.ServerEffectLinks)).IsEquivalentTo(new uint[] { 1, 2 });
    }

    [Test]
    public async Task UnknownLink_FailsLoudly()
    {
        var rows = new[]
        {
            new LoseTargetingContentRow(1, SpecialType.LoseTargetingTheTarget, 4, 0),
        };
        var links = new[]
        {
            new LoseTargetingContentLink(2, LoseTargetingLinkSource.SkillEffect),
        };

        var error = Assert.Throws<InvalidDataException>(() => LoseTargetingContentAuditResult.Create(rows, links));

        await Assert.That(error!.Message).Contains("unknown special effect row 2");
    }

    [Test]
    public async Task DuplicateContentRow_FailsLoudly()
    {
        var rows = new[]
        {
            new LoseTargetingContentRow(1, SpecialType.LoseTargetingTheTarget, 4, 0),
            new LoseTargetingContentRow(1, SpecialType.LoseTargetingTheTarget, 4, 0),
        };

        var error = Assert.Throws<InvalidDataException>(() => LoseTargetingContentAuditResult.Create(rows, []));

        await Assert.That(error!.Message).Contains("duplicate special effect row 1");
    }

    [Test]
    public async Task Read_LoadsTypedRowsAndKeepsLinkSourcesSeparate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection,
            "CREATE TABLE special_effects (id INTEGER, special_effect_type_id INTEGER, value1 INTEGER, value2 INTEGER);" +
            "CREATE TABLE effects (id INTEGER, actual_id INTEGER, actual_type TEXT);" +
            "CREATE TABLE skill_effects (effect_id INTEGER, enable TEXT);" +
            "CREATE TABLE buff_triggers (effect_id INTEGER, enable TEXT);" +
            "CREATE TABLE buff_tick_effects (effect_id INTEGER);" +
            "CREATE TABLE plot_effects (actual_id INTEGER, actual_type TEXT);");
        Execute(connection,
            "INSERT INTO special_effects VALUES (1, 146, 4, 0), (2, 146, 0, 0), (3, 146, 4, 0);" +
            "INSERT INTO effects VALUES (100, 1, 'SpecialEffect'), (101, 2, 'SpecialEffect');" +
            "INSERT INTO skill_effects VALUES (100, 't');" +
            "INSERT INTO buff_triggers VALUES (101, 't');" +
            "INSERT INTO plot_effects VALUES (3, 'SpecialEffect');");

        var result = LoseTargetingContentAudit.Read(connection);

        await Assert.That(result.ContentRowCount).IsEqualTo(3);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks)).IsEqualTo(2);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.AllContentLinks)).IsEqualTo(3);
        await Assert.That(result.LinkCounts[LoseTargetingLinkSource.SkillEffect]).IsEqualTo(1);
        await Assert.That(result.LinkCounts[LoseTargetingLinkSource.BuffTrigger]).IsEqualTo(1);
        await Assert.That(result.LinkCounts[LoseTargetingLinkSource.PlotEffect]).IsEqualTo(1);
    }

    private static LoseTargetingContentAuditResult CreateSnapshot(int contentRows, int linkedRows)
    {
        var rows = Enumerable.Range(1, contentRows)
            .Select(id => new LoseTargetingContentRow((uint)id, SpecialType.LoseTargetingTheTarget, 4, 0));
        var links = Enumerable.Range(1, linkedRows)
            .Select(id => new LoseTargetingContentLink((uint)id, LoseTargetingLinkSource.SkillEffect));
        return LoseTargetingContentAuditResult.Create(rows, links);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
