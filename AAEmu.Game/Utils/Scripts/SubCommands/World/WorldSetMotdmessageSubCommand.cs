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
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string motd = parameters["MOTD"];
        if (motd is "")
        {
            SendColorMessage(messageOutput, Color.Coral, $"MOTD message must not be an empty string");
            return;
        }
        character.SetMotdMessage(motd);
        SendMessage(messageOutput, $"Set MOTD {motd}");
        Logger.Warn($"{Title}: {motd}");
    }
}
