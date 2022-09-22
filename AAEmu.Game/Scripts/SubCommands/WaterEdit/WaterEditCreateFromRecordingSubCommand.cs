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
    public class WaterEditCreateFromRecordingSubCommand : SubCommandBase 
    {
        public WaterEditCreateFromRecordingSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Takes the data from your last current flow recoding and creates a new body of water using it's data using a specified name.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit createfromrecoding";
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
            
            if ((WaterEditCmd.RecordingTask != null) && (WaterEditCmd.RecordingTask.IsRecording()))
            {
                character.SendMessage($"|cFFFFFF00[WaterEdit] Cannot be used while still recording!|r");
                return;
            }
            
            if (WaterEditCmd.RecordedData.Count < 2)
            {
                character.SendMessage($"|cFFFFFF00[WaterEdit] You need a minimum of 2 data points to create a water body!|r");
                return;
            }

            var newName = parameters["name"];
            var newBody = new WaterBodyArea(newName, WaterBodyAreaType.LineArray);

            foreach (var point in WaterEditCmd.RecordedData)
                newBody.Points.Add(point);

            newBody.Depth = 10f;
            newBody.RiverWidth = 20f;
            newBody.Speed = WaterEditCmd.RecordedSpeed;
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
                
            character.SendMessage($"[WaterEdit] Created new river water body from recoding, {WaterEditCmd.SelectedWater.Name} - {WaterEditCmd.SelectedWater.Id}!!|r");
        }
    }
}
