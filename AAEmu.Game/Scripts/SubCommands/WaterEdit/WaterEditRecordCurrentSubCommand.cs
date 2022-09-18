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
    public class WaterEditRecordCurrentSubCommand : SubCommandBase 
    {
        public WaterEditRecordCurrentSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Starts and stops recording currents excerted on the user in a body of water. Use this command twice while free floating in the river. Recording also stops if you are no longer moving by a current.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit recordcurrent";
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
            
            /*
            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }
            */

            if ((WaterEditCmd.RecordingTask != null) && (WaterEditCmd.RecordingTask.IsRecording()))
            {
                // Stop recording
                WaterEditCmd.RecordingTask.StopRecording();
                character.SendMessage($"|cFFFFFF00[WaterEdit] Stopping recording!|r");
                return;
            }
            else
            {
                // Start Recording
                WaterEditCmd.RecordingTask = new WaterEditRecordTask(character as Character);
                TaskManager.Instance.Schedule(WaterEditCmd.RecordingTask);
                character.SendMessage($"|cFFFFFF00[WaterEdit] Start recording flowing water!|r");
                return;
            }
        }
    }
}
