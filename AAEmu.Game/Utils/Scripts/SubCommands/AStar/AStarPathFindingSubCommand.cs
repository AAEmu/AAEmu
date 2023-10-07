using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar;

public class AStarPathFindingSubCommand : SubCommandBase
{
    public AStarPathFindingSubCommand()
    {
        Title = "[AStar Path Finding]";
        Description = "Start looking for a path. Returns a list of points.";
        CallPrefix = $"{CommandManager.CommandPrefix}go||find";
        AddParameter(new StringSubCommandParameter("templateId", "templateId", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        string templateId = parameters["templateId"]; // что бы был, без праметра не работает скрипт
        var currentTarget = ((Character)character).CurrentTarget;
        var npc = currentTarget as Npc;
        if (currentTarget is null || npc == null)
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: You need to target a Npc first");
            return;
        }

        //if (PathNode.pos1.X > 0 && PathNode.pos1.Y > 0 && PathNode.pos2.X > 0 && PathNode.pos2.Y > 0)
        if (npc.Ai.PathNode.pos1 != null && npc.Ai.PathNode.pos2 != null)
        {
            npc.Ai.PathNode.ZoneKey = character.Transform.ZoneId;
            npc.Ai.PathNode.findPath = npc.Ai.PathNode.FindPath(npc.Ai.PathNode.pos1, npc.Ai.PathNode.pos2);

            character.SendMessage($"AStar: points found Total: {npc.Ai.PathNode.findPath?.Count ?? 0}");
            if (npc.Ai.PathNode.findPath != null)
            {
                for (var i = 0; i < npc.Ai.PathNode.findPath.Count; i++)
                {
                    character.SendMessage($"AStar: point {i} coordinates X:{npc.Ai.PathNode.findPath[i].X}, Y:{npc.Ai.PathNode.findPath[i].Y}, Z:{npc.Ai.PathNode.findPath[i].Z}");
                }
            }
        }
        else
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: to find the path, you need to set the start and end points of the route!");
        }
    }
}
