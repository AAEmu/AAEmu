using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
//using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Game.Utils.Scripts.SubCommands.AStar;

namespace AAEmu.Game.Scripts.Commands
{
    public class AStarCmd : SubCommandBase, ICommand
    {
        public AStarCmd() 
        {
            Title = "[AStar]";
            Description = "Root command to manage Path Findings";
            CallPrefix = $"{CommandManager.CommandPrefix}pf";

            Register(new AStarPathFindingSubCommand(), "find", "go"); // начать поиск пути
            Register(new AStarStartPositionSubCommand(), "start", "begin"); // установить начальную точку пути
            Register(new AStarEndPositionSubCommand(), "goal", "end"); // установить конечную точку пути
            Register(new AStarViewSubCommand(), "view"); // отобразить на местности найденные точки пути
        }

        public void OnLoad()
        {
            CommandManager.Instance.Register("pf", this);
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

        public void Execute(Character character, string[] args)
        {
            throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
        }
    }
}
