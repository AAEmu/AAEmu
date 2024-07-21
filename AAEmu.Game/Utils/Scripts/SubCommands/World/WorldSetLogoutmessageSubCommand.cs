using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetLogoutmessageSubCommand : SubCommandBase
{
    public WorldSetLogoutmessageSubCommand()
    {
        Title = "[World Set LogoutMessage]";
        Description = "Setting the logout message";
        CallPrefix = $"{CommandManager.CommandPrefix}logoutmessage";
        AddParameter(new StringSubCommandParameter("LogoutMessage", "LogoutMessage", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string logoutMessage = parameters["LogoutMessage"];
        if (logoutMessage is "")
        {
            SendColorMessage(messageOutput, Color.Coral, $"Logout message must not be an empty string");
            return;
        }
        character.SetLogoutMessage(logoutMessage);
        SendMessage(messageOutput, $"Set LogoutMessage {logoutMessage}");
        Logger.Warn($"{Title}: {logoutMessage}");
    }
}
