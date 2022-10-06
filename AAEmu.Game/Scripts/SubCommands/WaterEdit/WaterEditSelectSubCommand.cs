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
    public class WaterEditSelectSubCommand : SubCommandBase 
    {
        public WaterEditSelectSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Selects a specific water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit select";
            AddParameter(new StringSubCommandParameter("id", "water body name||id", true));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            string selectName = parameters["id"];
            WaterEditCmd.SelectedWater = null;
            WaterEditCmd.SelectedWorld = world;
            // search exact name or id
            lock (world.Water._lock)
                foreach (var area in world.Water.Areas)
                {
                    if ((area.Name.ToLower() == selectName) || (area.Id.ToString() == selectName))
                    {
                        WaterEditCmd.SelectedWater = area;
                        WaterEditCmd.NextPoint = 0;
                        break;
                    }
                }

            // If nothing found, do partial name search
            if (WaterEditCmd.SelectedWater == null)
            {
                lock (world.Water._lock)
                    foreach (var area in world.Water.Areas)
                    {
                        if (area.Name.ToLower().Contains(selectName))
                        {
                            WaterEditCmd.SelectedWater = area;
                            WaterEditCmd.NextPoint = 0;
                            break;
                        }
                    }
            }

            if (WaterEditCmd.SelectedWater != null)
			{
				if (WaterEditCmd.SelectedWater.AreaType == WaterBodyAreaType.LineArray)
					character.SendMessage($"[WaterEdit] Selected |cFFFFFFFF{WaterEditCmd.SelectedWater.Name}|r ({WaterEditCmd.SelectedWater.Id}), depth: |cFF00FF00{WaterEditCmd.SelectedWater.Depth}|r, river-width: |cFF00FF00{WaterEditCmd.SelectedWater.RiverWidth}|r, speed: |cFF00FF00{WaterEditCmd.SelectedWater.Speed}|r");
				else
					character.SendMessage($"[WaterEdit] Selected |cFFFFFFFF{WaterEditCmd.SelectedWater.Name}|r ({WaterEditCmd.SelectedWater.Id}), depth: |cFF00FF00{WaterEditCmd.SelectedWater.Depth}|r");
            }
			else
			{
                character.SendMessage($"[WaterEdit] Nothing selected");
			}
            WaterEditCmd.ShowSelectedArea(character);
        }
    }
}
