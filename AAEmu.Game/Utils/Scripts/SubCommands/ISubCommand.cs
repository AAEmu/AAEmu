using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.Game.Utils.Scripts.SubCommands;

public interface ICommandV2
{
    string Description { get; }
    string CallPrefix { get; }

    /// <summary>
    /// Validates if there is a subcommand that implements the first argument
    /// </summary>
    /// <param name="character">Character reference</param>
    /// <param name="triggerArgs">Argument that triggered the command</param>
    /// <param name="args">Additional arguments</param>
    /// <param name="messageOutput">Message output reference</param>
    void PreExecute(ICharacter character, string triggerArgs, string[] args, IMessageOutput messageOutput);
}
