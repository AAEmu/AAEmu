using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands.FishFinder;
using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class FishFinderCmd : SubCommandBase, ICommand, ICommandV2
{
    public string[] CommandNames { get; set; } = new string[] { "fishfinder" };

    public FishFinderCmd()
    {
        Title = "[FishFinder]";
        Description = "Root command to manage FishFinder";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new FishFinderSetSubCommand(), "set", "s");
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return $"<{string.Join("||", SupportedCommands)}>";
    }

    public string GetCommandHelpText()
    {
        return CallPrefix;
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        throw new InvalidOperationException(
            $"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
    }
}
