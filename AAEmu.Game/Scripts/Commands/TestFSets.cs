using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestFSets : ICommand
{
    public string[] CommandNames { get; set; } = ["testfsets", "test_fsets"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
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
        foreach (Feature fObj in Enum.GetValues<Feature>())
        {
            if (FeaturesManager.Fsets.Check(fObj))
            {
                CommandManager.SendNormalText(this, messageOutput, $"|cFF00FF00ON  |cFF80FF80{fObj}");
            }
            else
            {
                CommandManager.SendNormalText(this, messageOutput, $"|cFFFF0000OFF |cFF802020{fObj}");
            }
        }
    }
}
