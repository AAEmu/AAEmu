using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public sealed class DumpSpecialtyEvents : ICommand
{
    private readonly SpecialtyManager _manager;

    public DumpSpecialtyEvents() : this(SpecialtyManager.Instance)
    {
    }

    internal DumpSpecialtyEvents(SpecialtyManager manager)
    {
        _manager = manager;
    }

    public string[] CommandNames { get; set; } = ["dump_specialty_events"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => string.Empty;

    public string GetCommandHelpText() => "Lists authored specialty events and their manual activation state.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length != 0)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /dump_specialty_events");
            return;
        }

        var active = _manager.GetActiveSpecialtyEvents().ToDictionary(x => x.EventId);
        CommandManager.SendNormalText(
            this,
            messageOutput,
            $"Specialty events: {(_manager.SpecialtyEventsEnabled ? "enabled" : "disabled")}, " +
            $"{active.Count}/{_manager.SpecialtyEventCatalog.Events.Count} active.");

        foreach (var descriptor in _manager.SpecialtyEventCatalog.Events.Values.OrderBy(x => x.Id))
        {
            var state = active.TryGetValue(descriptor.Id, out var activation)
                ? $"active until {activation.ExpiresAt.UtcDateTime:u}"
                : "inactive";
            CommandManager.SendNormalText(
                this,
                messageOutput,
                $"{descriptor.Id}: {descriptor.Type} value={descriptor.Value} " +
                $"target={descriptor.ObjectType}:{descriptor.ObjectId} zone={descriptor.Trigger.ZoneGroupId} {state}");
        }
    }
}
