using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditCreateWaterSubCommand : SubCommandBase 
    {
        public WaterEditCreateWaterSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Create a new polygon body of water using a specified name.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit createwater";
            AddParameter(new StringSubCommandParameter("name", "name", true));
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

            var newName = parameters["name"];
            var newBody = new WaterBodyArea(newName, WaterBodyAreaType.Polygon);
            
            var centerPos = character.Transform.World.Position with { Z = character.Transform.World.Position.Z - 5f };
            newBody.Points.Add(new Vector3(centerPos.X - 15f, centerPos.Y - 15f, centerPos.Z));
            newBody.Points.Add(new Vector3(centerPos.X - 15f, centerPos.Y + 15f, centerPos.Z));
            newBody.Points.Add(new Vector3(centerPos.X + 15f, centerPos.Y + 15f, centerPos.Z));
            newBody.Points.Add(new Vector3(centerPos.X + 15f, centerPos.Y - 15f, centerPos.Z));
            newBody.Points.Add(new Vector3(centerPos.X - 15f, centerPos.Y - 15f, centerPos.Z));
            newBody.Depth = 10f;
            newBody.UpdateBounds();

            lock (world.Water._lock)
            {
                newBody.Id = world.Water.GetNewId();
                world.Water.Areas.Add(newBody);
            }
            WaterEditCmd.SelectedWater = newBody;
            WaterEditCmd.SelectedWorld = world;
            WaterEditCmd.NextPoint = 0;
                
            WaterEditCmd.ShowSelectedArea(character);
                
            character.SendMessage($"[WaterEdit] Create new water cube {WaterEditCmd.SelectedWater.Name} - {WaterEditCmd.SelectedWater.Id}!!|r");
            
            character.SendMessage($"|cFFFF0000[WaterEdit] Conversion not yet implemented!|r");
        }
    }
}
