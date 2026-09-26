using System.Drawing;

using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

/// <summary>
/// Pins the two ways <c>/eventcenter_rows &lt;gap&gt;</c> used to flood a client: a zero-valued
/// filter matching every row, and an unbounded listing on a gap that most rows carry.
/// </summary>
public class EventCenterRowsCommandTests
{
    private sealed class RecordingMessageOutput : IMessageOutput
    {
        public List<string> Recorded { get; } = [];
        public IEnumerable<string> Messages => Recorded;
        public IEnumerable<string> ErrorMessages => Recorded;
        public void SendMessage(string message) => Recorded.Add(message);
        public void SendMessage(ChatType chatType, string message, Color? color = null) => Recorded.Add(message);
        public void SendMessage(ICharacter target, string message) => Recorded.Add(message);
        public string Text => string.Join("\n", Recorded);
        public int RowLines => Recorded.Count(i => i.Contains("id="));
    }

    private static readonly Character Tester = new(new UnitCustomModelParams()) { Id = 1 };

    private static (EventCenterRows Command, RecordingMessageOutput Output) Build(int rowCount, EventCenterRowGap gaps)
    {
        var rows = Enumerable.Range(0, rowCount)
            .Select(i => new EventCenterRowProjection { ScheduleId = i, Gaps = gaps })
            .ToList();
        var catalog = new EventCenterRowCatalog(rows);
        return (new EventCenterRows(() => catalog), new RecordingMessageOutput());
    }

    [Test]
    [Arguments("None")]
    [Arguments("0")]
    public async Task AZeroValuedFilterIsRefusedInsteadOfMatchingEveryRow(string argument)
    {
        // 500 rows all carry the same gap, so an unfixed command matches every one of them.
        var (command, output) = Build(500, EventCenterRowGap.MissingTitleSource);

        command.Execute(Tester, [argument], output);

        await Assert.That(output.Text).Contains("not a filter");
        await Assert.That(output.RowLines).IsEqualTo(0);
    }

    [Test]
    public async Task AGapListingIsBoundedAndSaysHowManyWereOmitted()
    {
        const int rows = 500;
        var (command, output) = Build(rows, EventCenterRowGap.MissingTitleSource);

        command.Execute(Tester, ["MissingTitleSource"], output);

        await Assert.That(output.RowLines).IsEqualTo(EventCenterRows.MaxListedRows);
        await Assert.That(output.Text).Contains($"{rows - EventCenterRows.MaxListedRows} more row(s) not listed");
    }

    [Test]
    public async Task AShortListingIsNotTruncated()
    {
        var (command, output) = Build(3, EventCenterRowGap.MissingBodySource);

        command.Execute(Tester, ["MissingBodySource"], output);

        await Assert.That(output.RowLines).IsEqualTo(3);
        await Assert.That(output.Text.Contains("not listed")).IsFalse();
    }

    [Test]
    public async Task NoArgumentStillReportsEveryGapCountWithoutListingRows()
    {
        var (command, output) = Build(500, EventCenterRowGap.MissingTitleSource);

        command.Execute(Tester, [], output);

        await Assert.That(output.RowLines).IsEqualTo(0);
        await Assert.That(output.Text).Contains("MissingTitleSource: 500");
    }
}