using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditDowngradeSubCommand : SubCommandBase 
    {
        public WaterEditDowngradeSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "This will reduce the amount of used points in your current selected water body. This can be used to easily reduce complexity of Rivers. Recuded points by only taking every skip amount of points in the list.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit downgrade";
            AddParameter(new NumericSubCommandParameter<int>("skip", "skip count", true, 2, 1000));
        }
        
        public override void Execute(ICharacter character, string triggerArgument, string[] args) =>
            Execute(character, triggerArgument, new Dictionary<string, ParameterValue>());

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }

            int scale = parameters["scale"];
            
            if (WaterEditCmd.SelectedWater.Points.Count <= scale)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] Scale value needs to be less than the total number of points!|r");
                return;
            }
            
            var newPoints = new List<Vector3>();
            var oldCount = WaterEditCmd.SelectedWater.Points.Count;

            for (var i = 0; i < WaterEditCmd.SelectedWater.Points.Count; i++)
            {
                // Pick every "skip" point, and the last one
                if (((i % scale) == 0) || (i >= WaterEditCmd.SelectedWater.Points.Count-1))
                    newPoints.Add();
            }

            WaterEditCmd.SelectedWater.Points = newPoints;

            WaterEditCmd.SelectedWater.UpdateBounds();

            WaterEditCmd.NextPoint = 0;
                
            WaterEditCmd.ShowSelectedArea(character);
                
            character.SendMessage($"[WaterEdit] Reduced points in the water body {WaterEditCmd.SelectedWater.Name} - {WaterEditCmd.SelectedWater.Id} from {oldCount} down to {WaterEditCmd.SelectedWater.Points.Count} !!|r");
        }
    }
}
