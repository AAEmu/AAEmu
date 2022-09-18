using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
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
    public class WaterEditCreateFromRecordingSubCommand : SubCommandBase 
    {
        public WaterEditCreateFromRecordingSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Takes the data from your last current flow recoding and creates a new body of water using it's data.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit createfromrecoding";
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
            
            
            character.SendMessage($"|cFFFF0000[WaterEdit] Conversion not yet implemented!|r");
        }
    }
}
