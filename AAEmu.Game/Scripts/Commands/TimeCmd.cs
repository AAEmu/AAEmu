using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands.Time;
using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TimeCmd : SubCommandBase, ICommand, ICommandV2
{
    public string[] CommandNames { get; set; } = ["time"];

    public TimeCmd()
    {
        Title = "[Time]";
        Description = "Root command to manage Time";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new TimeSetSubCommand(), "set", "s");
    }

    public void OnLoad()
    {
        string[] name = ["time"];
        CommandManager.Instance.Register(name, this);
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
