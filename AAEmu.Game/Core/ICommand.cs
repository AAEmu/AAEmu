using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core
{
    public interface ICommand
    {
        void Execute(ICharacter character, string[] args);
        string GetCommandLineHelp();
        string GetCommandHelpText();
    }
}
