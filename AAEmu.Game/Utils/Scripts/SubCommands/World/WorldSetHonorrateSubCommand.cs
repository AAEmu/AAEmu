using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World
{
    public class WorldSetHonorrateSubCommand : SubCommandBase
    {
        public WorldSetHonorrateSubCommand()
        {
            Title = "[World Set HonorRate]";
            Description = "Setting the honor rate";
            CallPrefix = $"{CommandManager.CommandPrefix}honorrate";
            AddParameter(new NumericSubCommandParameter<float>("HonorRate", "HonorRate", true));
        }
        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            float honorRate = parameters["HonorRate"];
            if (honorRate < 1.0f || honorRate > 1000.0f)
            {
                SendColorMessage(character, Color.Coral, $"Honor rate = {honorRate} must be at least 1.0 and no more than 1000.0 |r");
                return;
            }
            character.SetHonorRate(honorRate);
            SendMessage(character, "Set HonorRate {0}", honorRate);
            _log.Warn($"{Title}: {honorRate}");
        }
    }
}