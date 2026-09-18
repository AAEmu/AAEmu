using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Instance helper: the channel list a system instance's picker needs, and a way into a chosen channel
/// without walking to its entrance.
/// </summary>
public class IndunCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["indun"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "channels <zoneId> | enter <zoneId> [channel]";
    }

    public string GetCommandHelpText()
    {
        return "System instances. 'channels <zoneId>' sends the channel list for that instance zone, which is " +
               "what opens the client's channel picker; 'enter <zoneId> [channel]' enters the copy on that " +
               "channel, creating it when it does not exist yet.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character == null)
            return;

        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "channels";
        switch (action)
        {
            case "channels":
                Channels(character, args, messageOutput);
                break;
            case "enter":
                Enter(character, args, messageOutput);
                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown action '{action}'. {GetCommandLineHelp()}");
                break;
        }
    }

    private void Channels(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var zoneId))
        {
            CommandManager.SendErrorText(this, messageOutput, "channels <zoneId>");
            return;
        }

        var sent = IndunManager.Instance.SendChannelList(character, zoneId);
        CommandManager.SendNormalText(this, messageOutput,
            sent ? $"Channel list sent for zone {zoneId}" : $"No channel list for zone {zoneId} (not an instance?)");
    }

    private void Enter(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var zoneId))
        {
            CommandManager.SendErrorText(this, messageOutput, "enter <zoneId> [channel]");
            return;
        }

        var channel = 0u;
        if (args.Length > 2 && !uint.TryParse(args[2], out channel))
        {
            CommandManager.SendErrorText(this, messageOutput, "enter <zoneId> [channel]");
            return;
        }

        var entered = IndunManager.Instance.RequestSystemInstance(character, zoneId, channel, out var dungeon);
        CommandManager.SendNormalText(this, messageOutput,
            entered
                ? $"Entering zone {zoneId} channel {channel} (copy {dungeon?.World?.Id ?? 0})"
                : $"Could not enter zone {zoneId} channel {channel}");
    }
}
