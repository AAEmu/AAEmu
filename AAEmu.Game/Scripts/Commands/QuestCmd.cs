using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class QuestCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["quest"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list||template||add||remove||step||prog||uncomplete||resetdaily||progress||objective>";
    }

    public string GetCommandHelpText()
    {
        return "[Quest] /quest <add||remove||list||prog||reward||resetdaily>";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        QuestCommandUtil.GetCommandChoice(this, messageOutput, character, args[0], args);
    }
}
