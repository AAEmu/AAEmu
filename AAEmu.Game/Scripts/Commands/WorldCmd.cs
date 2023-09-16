using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands.World;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands;

public class WorldCmd : SubCommandBase, ICommand, ICommandV2
{
    public WorldCmd()
    {
        Title = "[World]";
        Description = "Root command to manage World";
        CallPrefix = $"{CommandManager.CommandPrefix}world";

        Register(new WorldSetSubCommand(), "set", "s");
    }
    public void OnLoad()
    {
        string[] name = { "world", "w" };
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

    public void Execute(Character character, string[] args)
    {
        throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
    }
}
