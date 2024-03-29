﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class IgnoreCooldowns : ICommand
{
    public void OnLoad()
    {
        string[] name = { "ignoreskillcds", "disablecooldowns", "ignorecooldowns", "ignorecd" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<true||false>";
    }

    public string GetCommandHelpText()
    {
        return "Enables or disables skill cooldowns.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[IgnoreCooldowns] " + CommandManager.CommandPrefix + "ignorecd <true||false>");
            return;
        }

        if (bool.TryParse(args[0], out var ignoreCooldowns))
            character.IgnoreSkillCooldowns = ignoreCooldowns;
        else
            character.SendMessage("|cFFFF0000[IgnoreCooldowns] Throw parse bool!|r");
    }
}
