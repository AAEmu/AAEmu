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
    public class WaterEditNearbySubCommand : SubCommandBase 
    {
        public WaterEditNearbySubCommand()
        {
            Title = "[WaterEdit]";
            Description = "Lists all nearby bodies of water in your current world.";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit nearby";
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
            if (world == null)
            {
                character.SendMessage($"[WaterEdit] You are somehow not in a valid world!");
                return;
            }
            
            CreateNearbyList(character, world);
            var c = 0;
            for (var i = 0; (i < NearbyList.Count) && (i < 5); i++)
            {
                var area = WaterEditCmd.NearbyList[i].Item1;
                var distance = WaterEditCmd.NearbyList[i].Item2;
                if (distance > 1000f)
                    break;
                c++;
                character.SendMessage($"[WaterEdit] |cFFFFFFFF{area.Name}|r ({area.Id}) - {distance:F1}m");
            }

            if (c <= 0)
                character.SendMessage($"[WaterEdit] |cFFFF0000 Nothing nearby");
        }
    }
}
