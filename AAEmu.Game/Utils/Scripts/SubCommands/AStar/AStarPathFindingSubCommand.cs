using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar
{
    public class AStarPathFindingSubCommand : SubCommandBase
    {
        public AStarPathFindingSubCommand()
        {
            Title = "[AStar Path Finding]";
            Description = "Start looking for a path. Returns a list of points.";
            CallPrefix = $"{CommandManager.CommandPrefix}pf go";
            AddParameter(new NumericSubCommandParameter<uint>("templateId", "templateId", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint templateId = parameters["templateId"];

            //if (PathNode.pos1.X > 0 && PathNode.pos1.Y > 0 && PathNode.pos2.X > 0 && PathNode.pos2.Y > 0)
            if (PathNode.pos1 != null && PathNode.pos2 != null)
            {
                PathNode.findPath = PathNode.FindPath(PathNode.pos1, PathNode.pos2);

                character.SendMessage($"AStar: points found Total: {PathNode.findPath?.Count ?? 0}");
                if (PathNode.findPath != null)
                {
                    for (var i = 0; i < PathNode.findPath.Count; i++)
                    {
                        character.SendMessage($"AStar: point {i} coordinates X:{PathNode.findPath[i].X}, Y:{PathNode.findPath[i].Y}, Z:{PathNode.findPath[i].Z}");
                    }
                }
            }
            else
            {
                character.SendMessage($"AStar: to find the path, you need to set the start and end points of the route!");
            }
        }
    }
}
