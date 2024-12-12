using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.SubCommands.Slaves;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands;

public class SlaveCmd : SubCommandBase, ICommand, ICommandV2
{
    public string[] CommandNames { get; set; } = ["slave"];

    public SlaveCmd()
    {
        Title = $"[{CommandNames[0]}]";
        Description = "Root command to manage Slaves";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new SlaveInformationSubCommand(), "info");
        Register(new SlavePositionSubCommand(), "position", "pos");
        Register(new SlaveSaveSubCommand(), "save");
        Register(new SlaveSpawnSubCommand(), "spawn");
        Register(new SlaveRemoveSubCommand(), "remove");
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
