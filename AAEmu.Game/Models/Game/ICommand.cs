using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game
{
    public interface ICommand
    {
        void Execute(Character character, string[] args);
        string GetCommandLineHelp();
        string GetCommandHelpText();
    }
}
