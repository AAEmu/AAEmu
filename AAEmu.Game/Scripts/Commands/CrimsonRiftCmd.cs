using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Events;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to manually start or stop the Crimson Rift event.
/// </summary>
public class CrimsonRiftCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["crimsonrift", "rift"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "<start|stop>";

    public string GetCommandHelpText() =>
        "Manually start or stop the Crimson Rift event.\r\n" +
        $"{CommandManager.CommandPrefix}{CommandNames[0]} start\r\n" +
        $"{CommandManager.CommandPrefix}{CommandNames[0]} stop";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0].ToLower())
        {
            case "start":
                CrimsonRift.Instance.Start();
                CommandManager.SendNormalText(this, messageOutput, "Crimson Rift: Start() invoked.");
                break;

            case "stop":
                CrimsonRift.Instance.Stop();
                CommandManager.SendNormalText(this, messageOutput, "Crimson Rift: Stop() invoked.");
                break;

            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown action '{args[0]}'. Use start or stop.");
                break;
        }
    }
}
