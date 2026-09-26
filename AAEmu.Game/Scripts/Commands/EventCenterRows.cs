using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Read-only diagnostics for the event board's content projection: which schedule rows a board entry
/// could be built from, and which field has no content source yet. It changes no state and sends no
/// board packet; the board keeps answering its own requests from the entry/empty pair.
/// </summary>
public sealed class EventCenterRows : ICommand
{
    private readonly Func<EventCenterRowCatalog> _catalog;

    public EventCenterRows() : this(() => EventCenterRowCatalog.Build(GameScheduleManager.Instance))
    {
    }

    internal EventCenterRows(Func<EventCenterRowCatalog> catalog)
    {
        _catalog = catalog;
    }

    public string[] CommandNames { get; set; } = ["eventcenter_rows"];

    /// <summary>
    /// How many matching rows a gap listing prints before it stops and reports the remainder.
    /// </summary>
    /// <remarks>
    /// A diagnostic bound chosen by this command, not a limit recovered from the client. Several gaps
    /// are set on every projected row, so an unbounded listing pushes hundreds of chat lines at a
    /// client that is not asking for them. The per-gap counts printed without an argument are the
    /// part meant for scanning; the listing exists to sample.
    /// </remarks>
    public const int MaxListedRows = 20;

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[gap]";

    public string GetCommandHelpText() =>
        "Projects content schedule rows onto the event board's row fields and reports the gaps. " +
        "Optional argument: one gap name to list only the rows carrying it.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 1)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /eventcenter_rows [gap]");
            return;
        }

        var catalog = _catalog();
        CommandManager.SendNormalText(
            this,
            messageOutput,
            $"Event board rows: {catalog.Rows.Count} content rows, " +
            $"{catalog.RowsWithPeriod} with a resolved period, {catalog.WireReadyRows} writable.");

        if (args.Length == 1)
        {
            if (!Enum.TryParse<EventCenterRowGap>(args[0], ignoreCase: true, out var wanted))
            {
                CommandManager.SendErrorText(
                    this, messageOutput, $"Unknown gap '{args[0]}'.");
                return;
            }

            // TryParse also accepts "0" and "None", and a zero-valued flag matches every row through
            // HasFlag, so the listing would print the whole catalog while claiming to filter.
            if (wanted == EventCenterRowGap.None)
            {
                CommandManager.SendErrorText(
                    this,
                    messageOutput,
                    "'None' (0) is not a filter — it is the absence of every gap. " +
                    $"Name a gap to list; without an argument this command reports the per-gap counts.");
                return;
            }

            var matching = catalog.Rows.Where(row => row.Gaps.HasFlag(wanted)).ToList();
            CommandManager.SendNormalText(
                this, messageOutput, $"Gap {wanted} on {matching.Count} row(s):");
            foreach (var row in matching.Take(MaxListedRows))
            {
                SendRow(this, messageOutput, row);
            }

            if (matching.Count > MaxListedRows)
            {
                CommandManager.SendNormalText(
                    this,
                    messageOutput,
                    $"  ... {matching.Count - MaxListedRows} more row(s) not listed " +
                    $"(at most {MaxListedRows} are printed).");
            }

            return;
        }

        foreach (var gap in Enum.GetValues<EventCenterRowGap>().Where(gap => gap != EventCenterRowGap.None))
        {
            CommandManager.SendNormalText(
                this, messageOutput, $"  {gap}: {catalog.CountOf(gap)}");
        }
    }

    private static void SendRow(ICommand command, IMessageOutput messageOutput, EventCenterRowProjection row)
    {
        var period = row.IsPeriodResolved
            ? $"{row.PeriodStart:O} .. {row.PeriodEnd:O}"
            : "unresolved";
        var weekday = row.WeekdayFilter?.ToString() ?? "any";
        CommandManager.SendNormalText(
            command,
            messageOutput,
            $"  id={row.ScheduleId} allDay={row.IsAllDay} weekday={weekday} spawners={row.BoundSpawnerTemplateIds.Count} " +
            $"period={period} gaps={row.Gaps} name={row.ContentName}");
    }
}
