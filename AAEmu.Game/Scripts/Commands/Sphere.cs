using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class Sphere : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "sphere" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list||add||remove||quest||goto>";
    }

    public string GetCommandHelpText()
    {
        return "Sphere related commands ";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        SphereCommandUtil.GetCommandChoice(this, messageOutput, character, args[0], args);
    }
}
