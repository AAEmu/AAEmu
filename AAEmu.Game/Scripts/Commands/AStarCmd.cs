using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Game.Utils.Scripts.SubCommands.AStar;

namespace AAEmu.Game.Scripts.Commands;

public class AStarCmd : SubCommandBase, ICommand
{
    public string[] CommandNames { get; set; } = ["pathfind", "pf"];

    public AStarCmd()
    {
        Title = "[AStar]";
        Description = "Root command to manage Path Findings";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new AStarPathFindingSubCommand(), "find", "go"); // start searching for a path
        Register(new AStarStartPositionSubCommand(), "start", "begin"); // set the starting point of the path
        Register(new AStarEndPositionSubCommand(), "goal", "end"); // set the end point of the path
        Register(new AStarViewSubCommand(), "view"); // display the found path points on the terrain
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public AStarCmd(Dictionary<ICommandV2, string[]> subcommands) : base(subcommands)
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
