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
    public class WaterEditRemoveWaterSubCommand : SubCommandBase 
    {
        public WaterEditRemoveWaterSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Removes the selected body of water from the world.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit removewater";
            AddParameter(new NumericSubCommandParameter<uint>("points", "number of points", true));
        }

        public override void Execute(ICharacter character, string triggerArgument,
            IDictionary<string, ParameterValue> parameters)
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

            var pointCount = GetOptionalParameterValue<int>(parameters, "points", -1);

            if (pointCount != WaterEditCmd.SelectedWater.Points.Count)
            {
                character.SendMessage(
                    $"|cFFFF0000[WaterEdit] Security check failed ! ({WaterEditCmd.SelectedWater.Points.Count})|r");
                return;
            }

            lock (world.Water._lock)
            {
                world.Water.Areas.Remove(WaterEditCmd.SelectedWater);
            }

            WaterEditCmd.SelectedWater = null;
            WaterEditCmd.ShowSelectedArea(character);
            character.SendMessage($"[WaterEdit] Removed water body !!|r");
        }
    }
}
