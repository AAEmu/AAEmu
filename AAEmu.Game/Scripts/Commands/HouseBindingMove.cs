﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class HouseBindingMove : ICommand
{
    public void OnLoad()
    {
        string[] name = { "housebindingmove", "house_binding_move" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<AttachPointId> <X> <Y> <Z>";
    }

    public string GetCommandHelpText()
    {
        return "Command used for testing and moving house binding points";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget != null && character.CurrentTarget is House house)
        {
            if (args.Length < 4)
            {
                character.SendMessage("[HouseBindings] " + CommandManager.CommandPrefix + "house_binding_move <AttachPointId> <X> <Y> <Z>");
                return;
            }
            if (uint.TryParse(args[0], out var attachPointIdVal) &&
                float.TryParse(args[1], out var x) &&
                float.TryParse(args[2], out var y) &&
                float.TryParse(args[3], out var z))
            {
                var attachPointId = (AttachPointKind)attachPointIdVal;
                var attachPointObj = house.AttachedDoodads.Find(o => o.AttachPoint == attachPointId);
                if (attachPointObj != null)
                {
                    house.Delete();

                    attachPointObj.Transform.Local.SetPosition(x, y, z);

                    house.Spawn();

                    character.CurrentTarget = house;

                    character
                        .BroadcastPacket(
                            new SCTargetChangedPacket(character.ObjId, character.CurrentTarget.ObjId), true);
                }
                else
                    character.SendMessage("|cFFFF0000[HouseBindings] Not found this attach doodad|r");
            }
            else
                character.SendMessage("|cFFFF0000[HouseBindings] Throw parse args|r");
        }
        else
            character.SendMessage("|cFFFF0000[HouseBindings] First select house|r");
    }
}
