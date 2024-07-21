using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetGrowthrateSubCommand : SubCommandBase
{
    public WorldSetGrowthrateSubCommand()
    {
        Title = "[World Set GrowthRate]";
        Description = "Setting the growth rate";
        CallPrefix = $"{CommandManager.CommandPrefix}growthrate";
        AddParameter(new NumericSubCommandParameter<float>("GrowthRate", "GrowthRate", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        float growthRate = parameters["GrowthRate"];
        if (growthRate < 1.0f || growthRate > 1000.0f)
        {
            SendColorMessage(messageOutput, Color.Coral, $"Growth rate = {growthRate} must be at least 1.0 and no more than 1000.0");
            return;
        }
        character.SetGrowthRate(growthRate);
        SendMessage(messageOutput, $"Set GrowthRate {growthRate}");
        Logger.Warn($"{Title}: {growthRate}");
    }
}
