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
    public class WaterEditNextSubCommand : SubCommandBase 
    {
        public WaterEditNextSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Selects the next body of water in the current world.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit next";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args) =>
            Execute(character, triggerArgument, new Dictionary<string, ParameterValue>());

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            var lastId = WaterEditCmd.SelectedWater != null ? WaterEditCmd.SelectedWater.Id : 0;
            var doSelectNext = lastId <= 0;
            foreach (var area in world.Water.Areas)
            {
                if (doSelectNext)
                {
                    WaterEditCmd.SelectedWater = area;
                    WaterEditCmd.SelectedWorld = world;
                    WaterEditCmd.NextPoint = 0;
                    break;
                }

                if (area.Id == lastId)
                    doSelectNext = true;
            }

            if (WaterEditCmd.SelectedWater != null)
                character.SendMessage($"[WaterEdit] Selected |cFFFFFFFF{WaterEditCmd.SelectedWater.Name}|r ({WaterEditCmd.SelectedWater.Id}), depth: |cFF00FF00{WaterEditCmd.SelectedWater.Depth}|r");
            else
                character.SendMessage($"[WaterEdit] Nothing selected");
                
            WaterEditCmd.ShowSelectedArea(character);
        }
    }
}
