using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditShowSurfaceSubCommand : SubCommandBase 
    {
        public WaterEditShowSurfaceSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Shows the surface of the currently selected water";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit showsurface";
        }
        
        public override void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput) =>
            Execute(character, triggerArgument, new Dictionary<string, ParameterValue>(), messageOutput);

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }

            if (WaterEditCmd.SelectedWater != null)
                character.SendMessage($"[WaterEdit] Showing surface of |cFFFFFFFF{WaterEditCmd.SelectedWater.Name}|r ({WaterEditCmd.SelectedWater.Id}), depth: |cFF00FF00{WaterEditCmd.SelectedWater.Depth}|r");
            else
                character.SendMessage($"[WaterEdit] No water selected yet");
            WaterEditCmd.ShowSelectedSurface(character);
        }
    }
}
