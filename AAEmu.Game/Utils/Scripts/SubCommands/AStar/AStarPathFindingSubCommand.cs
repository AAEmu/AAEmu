using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

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
        if (npc.Ai.PathNode.StartPointPos != Vector3.Zero && npc.Ai.PathNode.EndPointPos != Vector3.Zero)
        {
            npc.Ai.PathNode.ZoneKey = character.Transform.ZoneId;
            var resList = npc.Ai.PathNode.FindPath(npc.ParentWorld, npc.Ai.PathNode.StartPointPos, npc.Ai.PathNode.EndPointPos);
            if (resList.Count > 0 && npc.Ai.PathNode.StartPointPos != resList[0]) // Skip the first node in this list if not on it
                resList.RemoveAt(0);
            var reducedList = npc.ParentWorld.Template.GeoData.ReducePath(resList, 10);
            npc.Ai.PathNode.FoundPath = npc.ParentWorld.Template.Floor.ApplyPathWaypointZ(reducedList);

            character.SendMessage($"AStar: points found Total: {resList?.Count ?? 0}");
            for (var i = 0; i < resList.Count; i++)
            {
                character.SendMessage(
                    $"AStar: point {i} coordinates X:{resList[i].X}, Y:{resList[i].Y}, Z:{resList[i].Z}");
            }
        }
        else
        {
            SendColorMessage(messageOutput, Color.Coral, $"AStar: to find the path, you need to set the start and end points of the route!");
        }
    }
}
