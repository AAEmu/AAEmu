using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
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

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
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
