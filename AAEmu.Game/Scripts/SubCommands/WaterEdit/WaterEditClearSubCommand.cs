using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditClearSubCommand : SubCommandBase 
    {
        public WaterEditClearSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Un-selects the current water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit clear";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput) =>
            Execute(character, triggerArgument, new Dictionary<string, ParameterValue>(), messageOutput);

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
        {
            if (WaterEditCmd.SelectedWater == null)
                character.SendMessage($"[WaterEdit] You had nothing selected.");
            WaterEditCmd.SelectedWater = null;
            WaterEditCmd.SelectedWorld = null;
            WaterEditCmd.ShowSelectedArea(character);
        }
    }
}
