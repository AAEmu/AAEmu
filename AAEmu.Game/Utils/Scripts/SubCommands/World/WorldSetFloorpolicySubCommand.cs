using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetFloorpolicySubCommand : SubCommandBase
{
    public WorldSetFloorpolicySubCommand()
    {
        Title = "[World Set FloorPolicy]";
        Description = "Sets floor height policy: ByZHint (heightmap+nav-surface by zHint; not terrain-only) or Legacy (nearest .bai node). TerrainFirst accepted as alias for ByZHint. Independent of GeoDataMode.";
        CallPrefix = $"{CommandManager.CommandPrefix}floorpolicy";
        AddParameter(new StringSubCommandParameter("FloorPolicy", "FloorPolicy", true));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var raw = ((string)parameters["FloorPolicy"]).Trim();
        if (!Enum.TryParse<FloorPolicyMode>(raw, ignoreCase: true, out var mode))
        {
            SendColorMessage(messageOutput, Color.Coral, "FloorPolicy must be 'ByZHint' or 'Legacy' (TerrainFirst = ByZHint)");
            return;
        }

        character.SetFloorPolicy(mode);
        SendMessage(messageOutput, $"Set FloorPolicy: {mode}");
        Logger.Warn($"{Title}: {mode}");
    }
}
