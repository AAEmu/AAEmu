using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GetAttribute : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "getattribute", "getattr", "attr" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[\"target\"] <attrId || attrName || all || used>";
    }

    public string GetCommandHelpText()
    {
        return $"Shows a list of all attributes of target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        Unit target = character;
        var argsIdx = 0;

        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (args.Length > 1 && args[0] == "target")
        {
            if (character.CurrentTarget == null || !(character.CurrentTarget is Unit))
            {
                CommandManager.SendErrorText(this, messageOutput, $"No Target Selected");
                return;
            }

            target = (Unit)character.CurrentTarget;
            argsIdx++;
        }

        CommandManager.SendNormalText(this, messageOutput, $"Stats for target {target.Name} ({target.ObjId})");

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
                var hide = value == "NotFound" || value == "0";
                // Exception for multipliers
                if (value != "NotFound" && ((UnitAttribute)attr).ToString().Contains("Mul"))
                {
                    hide = value == "1";
                }

                if (!hide)
                {
                    character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
                }
            }
        }
        else if (byte.TryParse(args[argsIdx], out var attrId))
        {
            if (Enum.IsDefined(typeof(UnitAttribute), attrId))
            {
                var value = target.GetAttribute(attrId);
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attrId}: {value}"));
            }
            else
            {
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
            }
        }
        else
        {
            if (Enum.TryParse(typeof(UnitAttribute), args[argsIdx], true, out var attr))
            {
                var value = target.GetAttribute((UnitAttribute)attr);
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"{(UnitAttribute)attr}: {value}"));
            }
            else
            {
                character.SendPacket(new SCChatMessagePacket(ChatType.System, $"Attribute doesn't exist."));
            }
        }
    }
}
