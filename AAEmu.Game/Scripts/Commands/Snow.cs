using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
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
        return "<true||false>";
    }

    public string GetCommandHelpText()
    {
        return "Enables or disables snow effect across the server";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // If no argument is provided send usage information
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        // determine if we received true,false or something else             
        if (bool.TryParse(args[0], out var isSnowing))
        {
            // Set Snowing state to user input, This will 
            // Enable Snow on all players who log into the server
            WorldManager.Instance.IsSnowing = isSnowing;

            // Turn snow on or off for all online characters,
            // Put this on the script level, so it only gets executed once when GM enables/disables snow
            WorldManager.Instance.BroadcastPacketToServer(new SCOnOffSnowPacket(isSnowing));
        }
        else
        {
            // user input was invalid notify them
            CommandManager.SendErrorText(this, messageOutput, $"Error parsing boolean");
        }
    }
}
