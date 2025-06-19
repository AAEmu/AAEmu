using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class DisconnectMe : ICommand
{
    public string[] CommandNames { get; set; } = ["disconnectme"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Forcefully disconnect your character from your connection";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
		// user input was invalid notify them
		CommandManager.SendErrorText(this, messageOutput, $"Disconnecting ...");
		character.Connection.ActiveChar = null;
		var con = character.Connection;
		character.Connection = null;
		con.Shutdown();
    }
}
