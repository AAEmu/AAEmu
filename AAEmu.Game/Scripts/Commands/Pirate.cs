﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Pirate : ICommand
{
    public void OnLoad()
    {
        string[] names = { "setfaction", "set_faction" };
        CommandManager.Instance.Register(names, this);
    }

    public string GetCommandLineHelp()
    {
        return "<nuian||haranyan||elf||firran||pirate>";
    }

    public string GetCommandHelpText()
    {
        return "Sets your faction";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[Faction] " + CommandManager.CommandPrefix + "faction <nuian||haranyan||elf||firran||pirate||friendly>");
            return;
        }

        var newFactionId = 0u;

        var factionString = args[0];
        if (factionString == "nuian")
            newFactionId = 101u;
        else if (factionString == "elf")
            newFactionId = 103u;
        else if (factionString == "haranyan")
            newFactionId = 109u;
        else if (factionString == "firran")
            newFactionId = 113u;
        else if (factionString == "pirate")
            newFactionId = 161u;
        else if (factionString == "red")
            newFactionId = 159u;
        else if (factionString == "blue")
            newFactionId = 160u;
        else if (factionString == "friendly")
            newFactionId = 1u;
        else
        {
            character.SendMessage("Invalid faction");
            return;
        }

        character.SetFaction(newFactionId);
        character.SendMessage($"Faction set to: {newFactionId}");
    }
}
