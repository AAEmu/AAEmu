using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Read-only view of the event-center runtime state. It never changes the projection, never schedules
/// anything, and never sends an event-board packet.
/// </summary>
public sealed class EventCenterRuntime : ICommand
{
    public string[] CommandNames { get; set; } = ["eventcenter_runtime"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => string.Empty;

    public string GetCommandHelpText() =>
        "Prints the W09B current/upcoming/deferred event-board runtime state without sending a board row.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /eventcenter_runtime");
            return;
        }

        var snapshot = EventCenterRuntimeManager.Instance.Snapshot;
        if (!snapshot.EvaluatedAtUtc.HasValue)
        {
            CommandManager.SendErrorText(this, messageOutput, "Event-center runtime has not loaded yet.");
            return;
        }

        var nextBoundary = snapshot.NextBoundaryUtc.HasValue
            ? snapshot.NextBoundaryUtc.Value.ToString("O")
            : "none";
        CommandManager.SendNormalText(
            this,
            messageOutput,
            $"Event board runtime at {snapshot.EvaluatedAtUtc.Value:O}: " +
            $"current={snapshot.CurrentScheduleIds.Count}, " +
            $"upcoming={snapshot.UpcomingScheduleIds.Count}, " +
            $"deferred={snapshot.DeferredScheduleIds.Count}, " +
            $"ended={snapshot.EndedScheduleIds.Count}, " +
            $"wire-ready={snapshot.WireReadyRows}, next-boundary={nextBoundary}");

        foreach (var row in snapshot.Rows.Where(row =>
                     row.State == EventCenterRuntimeRowState.Current
                     || row.State == EventCenterRuntimeRowState.Upcoming))
        {
            CommandManager.SendNormalText(
                this,
                messageOutput,
                $"  {row.State} id={row.ScheduleId} start={row.StartUtc!.Value:O} end={row.EndUtc!.Value:O}");
        }
    }
}
