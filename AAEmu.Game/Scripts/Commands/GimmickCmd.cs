using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.SubCommands.Gimmick;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands;

public class GimmickCmd : SubCommandBase, ICommand
{
    public string[] CommandNames { get; set; } = ["gimmick"];

    public GimmickCmd()
    {
        Title = "[Gimmick]";
        Description = "Root command to manage Gimmicks";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new GimmickSpawnSubCommand(), "spawn");
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames[0], this);
    }

    public GimmickCmd(Dictionary<ICommandV2, string[]> subcommands) : base(subcommands)
    {
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
