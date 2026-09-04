using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;
using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class FishSchoolCountCmd : ICommand
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["fishschools"];

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
        return "Count fish-school doodads that are still present.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var all = FishSchoolManager.Instance.GetAllFishSchools();
        var fresh = 0;
        var salt = 0;
        var other = 0;
        foreach (var doodad in all)
        {
            if (doodad.TemplateId == DoodadConstants.FreshwaterFishSchool)
                fresh++;
            else if (doodad.TemplateId == DoodadConstants.SaltwaterFishSchool)
                salt++;
            else
                other++;
        }

        var msg = $"present={all.Count} fresh={fresh} salt={salt} other={other}";
        Logger.Info("FishSchoolCount {0}", msg);
        CommandManager.SendNormalText(this, messageOutput, msg);
    }
}
