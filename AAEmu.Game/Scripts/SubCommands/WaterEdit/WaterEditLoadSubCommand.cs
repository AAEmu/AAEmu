using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using System.IO;
using AAEmu.Commons.IO;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditLoadSubCommand : SubCommandBase 
    {
        public WaterEditLoadSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Load current worlds's water data from disk";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit load";
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
            
            var loadFileName = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "water_bodies.json");
            if (!WaterBodies.Load(loadFileName, out var newWater))
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] Error loading {loadFileName} !|r");
            }
            else
            {
                world.Water = newWater;
                character.SendMessage($"[WaterEdit] |cFFFFFFFF{loadFileName}|r has been loaded.");
            }
        }
    }
}
