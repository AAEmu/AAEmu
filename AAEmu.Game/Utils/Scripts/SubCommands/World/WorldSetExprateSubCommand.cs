using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World
{
    public class WorldSetExprateSubCommand : SubCommandBase
    {
        public WorldSetExprateSubCommand()
        {
            Title = "[World Set ExpRate]";
            Description = "Setting the exp rate";
            CallPrefix = $"{CommandManager.CommandPrefix}exprate";
            AddParameter(new NumericSubCommandParameter<float>("ExpRate", "ExpRate", true));
        }
        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            float expRate = parameters["ExpRate"];
            if (expRate < 1.0f || expRate > 1000.0f)
            {
                SendColorMessage(character, Color.Coral, $"Exp Rate = {expRate} must be at least 1.0 and no more than 1000.0 |r");
                return;
            }
            character.SetExpRate(expRate);
            SendMessage(character, "Set ExpRate {0}", expRate);
            _log.Warn($"{Title}: {expRate}");
        }
    }
}