using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Core.Managers;

public interface ICommandManager
{
    List<string> GetCommandKeys();
    ICommand GetCommandInterfaceByName(string commandName);
    string GetCommandNameBase(string aliasName);
    void Register(string name, ICommand command);
    void Register(string[] names, ICommand command);
    string UnAliasCommandName(string cmd);
    bool Handle(Character character, string text, out IMessageOutput messageOutput);
    void Clear();
}
