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
    public class WaterEditSetHeightSubCommand : SubCommandBase 
    {
        public WaterEditSetHeightSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Set the height of the points in the water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit setheight";
            AddParameter(new NumericSubCommandParameter<float>("height", "height", true, 0f, 4096f));
        }

        public override void Execute(ICharacter character, string triggerArgument,
            IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }

            if (WaterEditCmd.SelectedWater == null)
            {
                character.SendMessage($"|cFFFF0000[WaterEdit] You need to select a water body first!|r");
                return;
            }

            if (WaterEditCmd.SelectedWorld != world)
            {
                character.SendMessage(
                    $"|cFFFF0000[WaterEdit] Currently selected water is not in the same world as you! ({WaterEditCmd.SelectedWorld.Name})|r");
                return;
            }

            float newHeight = parameters["height"];

            WaterEditCmd.SelectedWater.Height = newHeight;
            WaterEditCmd.ShowSelectedArea(character);
            character.SendMessage($"[WaterEdit] Height for |cFFFFFFFF{WaterEditCmd.SelectedWater.Name}|r set to |cFF00FF00{newHeight}!|r");
        }
    }
}
