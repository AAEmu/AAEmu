using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GetAttribute : ICommand
{
    public void OnLoad()
    {
        string[] name = { "getattribute", "getattr", "attr" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "[target] <attrId || attrName || all || used>";
    }

    public string GetCommandHelpText()
    {
        return "getattribute " + GetCommandLineHelp();
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        Unit target = character;
        int argsIdx = 0;

        if (args.Length == 0)
        {
            character.SendMessage("[GetAttribute] " + CommandManager.CommandPrefix + GetCommandHelpText());
            return;
        }

        if (args.Length > 1 && args[0] == "target")
        {
            if (character.CurrentTarget == null || !(character.CurrentTarget is Unit))
            {
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"No Target Selected"));
                return;
            }
            target = (Unit)character.CurrentTarget;
            argsIdx++;
        }

        character.SendPacket(new SCChatMessagePacket(ChatType.System, $"[GetAttribute] Stats for target {target.Name} ({target.ObjId})"));

        if (args[argsIdx].ToLower() == "all")
        {
            foreach (var attr in Enum.GetValues(typeof(UnitAttribute)))
            {
                var value = target.GetAttribute((UnitAttribute)attr);
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
            }
        }
        else if (args[argsIdx].ToLower() == "used")
        {
            foreach (var attr in Enum.GetValues(typeof(UnitAttribute)))
            {
                var value = target.GetAttribute((UnitAttribute)attr);
                var hide = (value == "NotFound") || (value == "0");
                // Exception for multipliers
                if ((value != "NotFound") && ((UnitAttribute)attr).ToString().Contains("Mul"))
                    hide = (value == "1");

                if (!hide)
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
            }
        }
        else if (byte.TryParse(args[argsIdx], out byte attrId))
        {
            if (Enum.IsDefined(typeof(UnitAttribute), attrId))
            {
                string value = target.GetAttribute(attrId);
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attrId}: {value}"));
            }
            else
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
        }
        else
        {
            if (Enum.TryParse(typeof(UnitAttribute), args[argsIdx], true, out var attr))
            {
                string value = target.GetAttribute((UnitAttribute)attr);
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
            }
            else
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
        }
    }
}
