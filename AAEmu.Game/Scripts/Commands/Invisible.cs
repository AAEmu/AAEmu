using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Invisible : ICommand
{
    public string[] CommandNames { get; set; } = ["invisible"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<true||false>";
    }

    public string GetCommandHelpText()
    {
        return "Sets yourself as invisible to other players";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (bool.TryParse(args[0], out var value))
        {
            character.SetInvisible(value);
        }
    }
}
