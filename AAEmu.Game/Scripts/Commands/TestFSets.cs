﻿using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestFSets : ICommand
{

    public void OnLoad()
    {
        string[] name = { "testfsets", "test_fsets" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Shows currently active fsets of the server";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        foreach (var fObj in Enum.GetValues(typeof(Feature)))
        {
            var f = (Feature)fObj;
            if (FeaturesManager.Fsets.Check(f))
                character.SendMessage("[Feature] |cFF00FF00ON  |cFF80FF80" + f.ToString() + "|r");
            else
                character.SendMessage("[Feature] |cFFFF0000OFF |cFF802020" + f.ToString() + "|r");
        }

    }
}
