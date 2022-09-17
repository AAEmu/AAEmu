using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditListPointsSubCommand : SubCommandBase 
    {
        public WaterEditListPointsSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Lists all points in the selected body of water.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit listpoints";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }
            
            for (var i = 0; i < WaterEditCmd.SelectedWater.Points.Count-1; i++)
                character.SendMessage($"[WaterEdit] #{i} : {WaterEditCmd.SelectedWater.Points[i]}");

            WaterEditCmd.ShowSelectedArea(character);
        }
    }
}
