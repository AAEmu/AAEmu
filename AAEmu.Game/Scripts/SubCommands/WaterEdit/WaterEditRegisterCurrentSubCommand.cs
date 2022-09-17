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
    public class WaterEditRegisterCurrentSubCommand : SubCommandBase 
    {
        public WaterEditRegisterCurrentSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Starts and stops recording currents excerted on the user in a body of water. Use this command twice while free floating in the river.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit registercurrent";
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

            if (WaterEditCmd.MeasureTime <= DateTime.MinValue)
            {
                // Begin recording
                WaterEditCmd.MeasureTime = DateTime.UtcNow;
                WaterEditCmd.MeasurePosition = character.Transform.World.Position;
                character.SendMessage($"|cFFFFFF00[WaterEdit] Started recording water current!|r");
                return;
            }
            else
            {
                var deltaPosition = character.Transform.World.Position - WaterEditCmd.MeasurePosition;
                var deltaTime = (float)(DateTime.UtcNow - WaterEditCmd.MeasureTime).TotalSeconds;

                var deltaPerSecond = deltaPosition / deltaTime;
                
                character.SendMessage($"[WaterEdit] You moved at |cFF00FF00{deltaPerSecond.Length():F3} m/s|r (offset {deltaPosition} in {deltaTime} seconds = {deltaPerSecond}/s)");

                WaterEditCmd.MeasureTime = DateTime.MinValue;
                WaterEditCmd.MeasurePosition = Vector3.Zero;
                return;
            }
            
        }
    }
}
