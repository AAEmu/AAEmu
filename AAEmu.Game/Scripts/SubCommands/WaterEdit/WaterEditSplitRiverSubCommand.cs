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
    public class WaterEditSplitRiverSubCommand : SubCommandBase 
    {
        public WaterEditSplitRiverSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Takes the currently selected rivers, and splits it into two part starting at given point creating a new named river using the same paramters";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit splitriver";
            AddParameter(new NumericSubCommandParameter<int>("point", "point id", true));
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

            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to have selected a water body to use this command!|r");
                return;
            }

            if (WaterEditCmd.SelectedWater.AreaType != WaterBodyAreaType.LineArray)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] This command can only be used on waterbodies of the LineArray type (rivers)!|r");
                return;
            }
            
            int startPoint = parameters["point"];
            
            if ((startPoint <= 0) || (startPoint >= WaterEditCmd.SelectedWater.Points.Count - 1)) 
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] The starting point must somewhere in the middle (1 ~ {(WaterEditCmd.SelectedWater.Points.Count - 2)})!|r");
                return;
            }
            
            var newPoints = new List<Vector3>();
            var oldBody = WaterEditCmd.SelectedWater;
            var oldCount = oldBody.Points.Count;

            // Copy point locations for new river
            for (var i = startPoint; i < oldBody.Points.Count; i++)
                newPoints.Add(oldBody.Points[i]);

            var newName = parameters["name"];
            var newBody = new WaterBodyArea(newName, WaterBodyAreaType.LineArray);

            foreach (var point in newPoints)
                newBody.Points.Add(point);

            newBody.Depth = oldBody.Depth;
            newBody.RiverWidth = oldBody.RiverWidth;
            newBody.Speed = oldBody.Speed;
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

            character.SendMessage($"[WaterEdit] Created new river water body previous river section, {newBody.Name} - {newBody.Id} from {oldBody.Name} - {oldBody.Id} @ {startPoint} !!|r");
            
            // Truncate old water body (leaving startPoint as it's new endpoint)
            lock (world.Water._lock)
            {
                oldBody.Points.RemoveRange(startPoint + 1, oldBody.Points.Count - startPoint - 2);
            }

            character.SendMessage($"[WaterEdit] Old: {oldBody.Name} - {oldBody.Id} changed from {oldCount} to {oldBody.Points.Count} points.|r");
            character.SendMessage($"[WaterEdit] New: {newBody.Name} - {newBody.Id} has {newBody.Points.Count} points.|r");
        }
    }
}
