using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetGeodatamodeSubCommand : SubCommandBase
{
    public WorldSetGeodatamodeSubCommand()
    {
        Title = "[World Set GeoDataMode]";
        Description = "Setting the GeoDataMode";
        CallPrefix = $"{CommandManager.CommandPrefix}geodatamode";
        AddParameter(new StringSubCommandParameter("GeoDataMode", "GeoDataMode", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string geoDataMode = parameters["GeoDataMode"];
        if (geoDataMode is "")
        {
            SendColorMessage(messageOutput, Color.Coral, $"GeoDataMode must be an 'true' or 'false'");
            return;
        }

        character.SetGeoDataMode(geoDataMode == "true");
        SendMessage(messageOutput, $"Set GeoDataMode: {geoDataMode}");
        Logger.Warn($"{Title}: {geoDataMode}");
    }
}
