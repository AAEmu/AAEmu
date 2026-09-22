using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Snow : ICommand
{
    public string[] CommandNames { get; set; } = ["snow"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<true||false||auto>";
    }

    public string GetCommandHelpText()
    {
        return "Holds the snow effect on or off across the server; auto hands it back to the configured feature and the weather cycle";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // If no argument is provided send usage information
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (string.Equals(args[0], "auto", StringComparison.OrdinalIgnoreCase))
        {
            WorldManager.Instance.SetSnowHold(null);
            return;
        }

        // determine if we received true,false or something else
        if (bool.TryParse(args[0], out var isSnowing))
        {
            // Holds snow for everyone online and everyone who logs in, until auto releases it.
            WorldManager.Instance.SetSnowHold(isSnowing);
        }
        else
        {
            // user input was invalid notify them
            CommandManager.SendErrorText(this, messageOutput, $"Error parsing boolean");
        }
    }
}
