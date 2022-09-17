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
    public class WaterEditGoToSubCommand : SubCommandBase 
    {
        public WaterEditGoToSubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Teleport to currently selected water body";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit goto";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
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
                character.SendMessage($"|cFFFF0000[WaterEdit] Currently selected water is not in the same world as you! ({WaterEditCmd.SelectedWorld.Name})|r");
                return;
            }
            
            var pos = WaterEditCmd.SelectedWater.GetCenter(true);
            Character chara = character as Character; 
            chara.ForceDismount();
            chara.DisabledSetPosition = true;
            character.SendPacket(new SCTeleportUnitPacket(0, 0, pos.X + 1f, pos.Y + 1f, pos.Z + 3f, 0));
        }
    }
}
