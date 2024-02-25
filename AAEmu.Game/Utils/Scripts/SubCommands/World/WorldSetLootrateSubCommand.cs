using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World;

public class WorldSetLootrateSubCommand : SubCommandBase
{
    public WorldSetLootrateSubCommand()
    {
        Title = "[World Set LootRate]";
        Description = "Setting the loot rate";
        CallPrefix = $"{CommandManager.CommandPrefix}lootrate";
        AddParameter(new NumericSubCommandParameter<float>("LootRate", "LootRate", true));
    }
    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        float lootRate = parameters["LootRate"];
        if (lootRate < 1.0f || lootRate > 1000.0f)
        {
            SendColorMessage(messageOutput, Color.Coral, $"Loot rate = {lootRate} must be at least 1.0 and no more than 1000.0");
            return;
        }
        character.SetLootRate(lootRate);
        SendMessage(messageOutput, $"Set GrowthRate {lootRate}");
        Logger.Warn($"{Title}: {lootRate}");
    }
}
