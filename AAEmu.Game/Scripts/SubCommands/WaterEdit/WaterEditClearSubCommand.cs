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
    public class WaterEditClearSubCommand : SubCommandBase 
    {
        public WaterEditClearSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Un-selects the current water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit clear";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            if (WaterEditCmd.SelectedWater == null)
                character.SendMessage($"[WaterEdit] You had nothing selected.");
            WaterEditCmd.SelectedWater = null;
            WaterEditCmd.SelectedWorld = null;
            WaterEditCmd.ShowSelectedArea(character);
        }
    }
}
