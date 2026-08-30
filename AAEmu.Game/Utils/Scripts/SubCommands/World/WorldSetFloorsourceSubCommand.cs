using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetFloorsourceSubCommand : SubCommandBase
{
    public WorldSetFloorsourceSubCommand()
    {
        Title = "[World Set FloorSource]";
        Description = "Sets floor height policy: TerrainFirst (heightmap) or Legacy (nearest .bai node). Independent of GeoDataMode.";
        CallPrefix = $"{CommandManager.CommandPrefix}floorsource";
        AddParameter(new StringSubCommandParameter("FloorSource", "FloorSource", true));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var raw = ((string)parameters["FloorSource"]).Trim();
        if (!Enum.TryParse<FloorSourceMode>(raw, ignoreCase: true, out var mode))
        {
            SendColorMessage(messageOutput, Color.Coral, "FloorSource must be 'TerrainFirst' or 'Legacy'");
            return;
        }

        character.SetFloorSource(mode);
        SendMessage(messageOutput, $"Set FloorSource: {mode}");
        Logger.Warn($"{Title}: {mode}");
    }
}
