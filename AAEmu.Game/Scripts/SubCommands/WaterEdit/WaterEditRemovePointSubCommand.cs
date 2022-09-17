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
    public class WaterEditRemovePointSubCommand : SubCommandBase 
    {
        public WaterEditRemovePointSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Removes a specific point in the selected body of water.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit removepoint";
            AddParameter(new NumericSubCommandParameter<uint>("point", "point id", false));
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
            
            var pointIndex = GetOptionalParameterValue<int>(parameters, "point", WaterEditCmd.NextPoint);

            if ((pointIndex >= WaterEditCmd.SelectedWater.Points.Count - 1) || (pointIndex <= 0))
            {
                character.SendMessage($"|cFFFFFFFF[WaterEdit] {pointIndex} is not a valid point index (1~{WaterEditCmd.SelectedWater.Points.Count-2})!|r");
                return;
            }

            lock (WaterEditCmd.SelectedWorld.Water._lock)
            {
                WaterEditCmd.SelectedWater.Points.RemoveAt(pointIndex);
            }

            WaterEditCmd.ShowSelectedArea(character);
            character.SendMessage($"[WaterEdit] Remove point |cFFFFFFFF#{pointIndex}|r");
        }
    }
}
