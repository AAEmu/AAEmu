using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetMotdmessageSubCommand : SubCommandBase
{
    public WorldSetMotdmessageSubCommand()
    {
        Title = "[World Set MOTD]";
        Description = "Setting the MOTD";
        CallPrefix = $"{CommandManager.CommandPrefix}motd";
        AddParameter(new StringSubCommandParameter("MOTD", "MOTD", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
    {
        string motd = parameters["MOTD"];
        if (motd is "")
        {
            SendColorMessage(character, Color.Coral, $"MOTD message must not be an empty string |r");
            return;
        }
        character.SetMotdMessage(motd);
        SendMessage(character, $"Set MOTD {motd}");
        Logger.Warn($"{Title}: {motd}");
    }
}
