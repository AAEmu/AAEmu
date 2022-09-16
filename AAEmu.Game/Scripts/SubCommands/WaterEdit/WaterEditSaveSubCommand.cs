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
    public class WaterEditSaveSubCommand : SubCommandBase 
    {
        public WaterEditSaveSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Save current worlds's water data to disk";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit save";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            var saveFileName = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "water_bodies.json");
            if (WaterBodies.Save(saveFileName, world.Water))
                character.SendMessage($"[WaterEdit] |cFFFFFFFF{saveFileName}|r has been saved.");
            else
                character.SendMessage($"|cFFFF0000[WaterEdit] Error saving {saveFileName} !|r");
        }
    }
}
