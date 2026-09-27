using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetFloordebugSubCommand : SubCommandBase
{
    public WorldSetFloordebugSubCommand()
    {
        Title = "[World Set FloorDebug]";
        Description = "Enables FloorQuery debug lines in Server.log (parse with Scripts/find-floor-mismatch.sh)";
        CallPrefix = $"{CommandManager.CommandPrefix}floordebug";
        AddParameter(new StringSubCommandParameter("FloorDebug", "FloorDebug", true));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var raw = ((string)parameters["FloorDebug"]).Trim();
        if (raw is not ("true" or "false"))
        {
            SendColorMessage(messageOutput, Color.Coral, "FloorDebug must be 'true' or 'false'");
            return;
        }

        var enabled = raw == "true";
        character.SetFloorDebug(enabled);
        SendMessage(messageOutput, $"Set FloorDebug: {enabled}");
        Logger.Warn($"{Title}: {enabled}");
    }
}
