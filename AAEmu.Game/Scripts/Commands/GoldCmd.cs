using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.SubCommands.Gold;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands;

public class GoldCmd : SubCommandBase, ICommand, ICommandV2
{
    public string[] CommandNames { get; set; } = ["gold"];

    public GoldCmd()
    {
        Title = "[Gold]";
        Description = "Root command to manage gold";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new GoldSetSubCommand(), "add");
        Register(new GoldSetSubCommand(), "set");
        Register(new GoldSetSubCommand(), "remove");
    }

    public void OnLoad()
    {
        // Own the primary name. AddGold keeps addgold / add_gold as aliases only.
        CommandManager.Instance.Register(CommandNames[0], this);
    }

    public string GetCommandLineHelp()
    {
        return $"<{string.Join("||", SupportedCommands)}> [self||target||name] <gold> [silver] [copper]  — or just <gold> [silver] [copper]";
    }

    public string GetCommandHelpText()
    {
        return $"{CallPrefix} add self 100   |   {CallPrefix} 100";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        throw new InvalidOperationException(
            $"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
    }

    // CommandManager dispatches through ICommandV2; re-bind so bare `/gold 100` works.
    void ICommandV2.PreExecute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput)
    {
        PreExecute(character, triggerArgument, GoldCommandRules.NormalizeArgs(args), messageOutput);
    }
}
