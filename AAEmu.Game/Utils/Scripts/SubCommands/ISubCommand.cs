using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
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
        void PreExecute(ICharacter character, string triggerArg, string[] args);
    }
}
