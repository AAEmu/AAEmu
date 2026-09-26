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
    // The reviewed shipped matrix: 65 special_effects rows of type 146, 48 of them linked from a server
    // effect table, 17 orphans. This was recorded as 50, which was wrong - the shipped server-link union is
    // 48. The old figure also came from a query that matched buff_effects.actual_id against
    // special_effects.id, which is a different id-space.
    private static readonly LoseTargetingAuditBaseline ReviewedMatrix =
        new(65, 48, LoseTargetingLinkScope.ServerEffectLinks);

    [Test]
    public async Task ReviewedMatrix_PinsTheDocumentedGap()
    {
        // The links are deliberately ids 18..65 rather than 1..48, so the linked and unlinked counts are
        // not a restatement of the fixture's own arguments - the audit has to actually resolve the id sets
        // to produce 48 and 17.
        var rows = Enumerable.Range(1, 65)
            .Select(id => new LoseTargetingContentRow((uint)id, SpecialType.LoseTargetingTheTarget, 4, 0));
        var links = Enumerable.Range(18, 48)
            .Select(id => new LoseTargetingContentLink((uint)id, LoseTargetingLinkSource.SkillEffect));
        var result = LoseTargetingContentAuditResult.Create(rows, links);

        result.ValidateAgainst(ReviewedMatrix);

        await Assert.That(result.ContentRowCount).IsEqualTo(65);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks)).IsEqualTo(48);
        // The 17 orphans are the low ids 1..17, not an arithmetic leftover.
        await Assert.That(result.UnlinkedIds(LoseTargetingLinkScope.ServerEffectLinks))
            .IsEquivalentTo(Enumerable.Range(1, 17).Select(id => (uint)id));
    }

    [Test]
    public async Task NewOrphan_FailsLoudlyAgainstTheReviewedMatrix()
    {
        // One more content row with the same 48 links leaves 18 orphans, and the audit must say so.
        var result = CreateSnapshot(66, 48);

        var error = Assert.Throws<InvalidDataException>(() => result.ValidateAgainst(ReviewedMatrix));

        await Assert.That(error!.Message).Contains("content=66");
        await Assert.That(error.Message).Contains("unlinked=18");
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

    [Test]
    public async Task Read_DoesNotCountABuffEffectWhoseIdCollidesWithASpecialEffect()
    {
        // The regression for the id-space mix-up. special_effects.id and buff_effects.id overlap by 16,808
        // on the shipped DB, so a query that accepts actual_type = 'BuffEffect' and then matches
        // actual_id against special_effects.id reports links that do not exist - on the shipped content that
        // turned skill_effects 6 into 11, buff_triggers 42 into 43, buff_tick_effects 0 into 1 and
        // plot_effects 17 into 26. Every colliding row below is a buff effect, not a special effect.
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
            // effects 100 and 101 are both linked, and both point at a real type-146 id, but only 100 is a
            // SpecialEffect. 101 is a BuffEffect whose id 3 merely collides.
            "INSERT INTO effects VALUES (100, 1, 'SpecialEffect'), (101, 3, 'BuffEffect');" +
            "INSERT INTO skill_effects VALUES (100, 't'), (101, 't');" +
            "INSERT INTO buff_triggers VALUES (100, 't'), (101, 't');" +
            "INSERT INTO buff_tick_effects VALUES (100), (101);" +
            // Same collision on the plot side: id 2 is a real special effect, but this row is a buff.
            "INSERT INTO plot_effects VALUES (2, 'BuffEffect');");

        var result = LoseTargetingContentAudit.Read(connection);

        await Assert.That(result.ContentRowCount).IsEqualTo(3);
        // Only the one real SpecialEffect link, from either server table.
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks)).IsEqualTo(1);
        // And no plot link: the only plot row is a buff effect that collides. LinkCounts only carries
        // sources that produced something, so a source that matched nothing is absent rather than zero.
        await Assert.That(result.LinkCounts.GetValueOrDefault(LoseTargetingLinkSource.PlotEffect, 0))
            .IsEqualTo(0);
        await Assert.That(result.LinkedRowCount(LoseTargetingLinkScope.AllContentLinks)).IsEqualTo(1);
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
