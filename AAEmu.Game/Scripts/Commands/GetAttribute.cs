﻿using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class GetAttribute : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "getattribute", "getattr", "attr" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "<attrId || attrName> [target]";
        }

        public string GetCommandHelpText()
        {
            return "getattribute <attrId || attrName> [target]";
        }

        public void Execute(Character character, string[] args)
        {
            Unit target = character;


            if (args.Length == 0)
            {
                character.SendMessage("[GetAttribute] " + CommandManager.CommandPrefix + "getattribute <attrId || attrName> [target]");
                return;
            }

            if (args.Length > 1 && args[1] == "target")
            {
                if (character.CurrentTarget == null || !(character.CurrentTarget is Unit))
                {
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"No Target Selected"));
                    return;
                }
                target = (Unit)character.CurrentTarget;
            }

            if (byte.TryParse(args[0], out byte attrId))
            {
                if(Enum.IsDefined(typeof(UnitAttribute), attrId))
                {
                    string value = target.GetAttribute(attrId);
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attrId}: {value}"));
                }
                else
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
            }
            else
            {
                if(Enum.TryParse(typeof(UnitAttribute), args[0], true, out var attr))
                {
                    string value = target.GetAttribute((UnitAttribute)attr);
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
                }
                else
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
            }
        }
    }
}
