using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World
{
    public class WorldSetVocationrateSubCommand : SubCommandBase
    {
        public WorldSetVocationrateSubCommand()
        {
            Title = "[World Set VocationRate]";
            Description = "Setting the vocation rate";
            CallPrefix = $"{CommandManager.CommandPrefix}vocationrate";
            AddParameter(new NumericSubCommandParameter<float>("VocationRate", "VocationRate", true));
        }
        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            float vocationRate = parameters["VocationRate"];
            if (vocationRate < 1.0f || vocationRate > 1000.0f)
            {
                SendColorMessage(character, Color.Coral, $"Vocation rate = {vocationRate} must be at least 1.0 and no more than 1000.0 |r");
                return;
            }
            character.SetVocationRate(vocationRate);
            SendMessage(character, "Set VocationRate {0}", vocationRate);
            _log.Warn($"{Title}: {vocationRate}");
        }
    }
}
