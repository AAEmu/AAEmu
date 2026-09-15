using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public sealed class SetSpecialtyEvent : ICommand
{
    private readonly SpecialtyManager _manager;

    public SetSpecialtyEvent() : this(SpecialtyManager.Instance)
    {
    }

    internal SetSpecialtyEvent(SpecialtyManager manager)
    {
        _manager = manager;
    }

    public string[] CommandNames { get; set; } = ["set_specialty_event"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "<eventId> <on|off> [durationSeconds]";

    public string GetCommandHelpText() => "Manually activates or deactivates an authored specialty event.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length is < 2 or > 3 || !uint.TryParse(args[0], out var eventId))
        {
            SendUsage(messageOutput);
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "on":
                var durationSeconds = _manager.DefaultManualSpecialtyEventDurationSeconds;
                if (args.Length == 3 && !uint.TryParse(args[2], out durationSeconds) || durationSeconds == 0)
                {
                    SendUsage(messageOutput);
                    return;
                }
                if (!_manager.TryActivateSpecialtyEvent(
                        eventId,
                        TimeSpan.FromSeconds(durationSeconds),
                        character.Id,
                        out var activation,
                        out var activationError))
                {
                    CommandManager.SendErrorText(this, messageOutput, activationError);
                    return;
                }
                CommandManager.SendNormalText(
                    this,
                    messageOutput,
                    $"Activated specialty event {eventId} until {activation.ExpiresAt.UtcDateTime:u}.");
                break;
            case "off":
                if (args.Length != 2)
                {
                    SendUsage(messageOutput);
                    return;
                }
                if (!_manager.TryDeactivateSpecialtyEvent(eventId, out _, out var deactivationError))
                {
                    CommandManager.SendErrorText(this, messageOutput, deactivationError);
                    return;
                }
                CommandManager.SendNormalText(this, messageOutput, $"Deactivated specialty event {eventId}.");
                break;
            default:
                SendUsage(messageOutput);
                break;
        }
    }

    private void SendUsage(IMessageOutput messageOutput)
    {
        CommandManager.SendErrorText(this, messageOutput, "Usage: /set_specialty_event <eventId> <on|off> [durationSeconds]");
    }
}
