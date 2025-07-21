using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar;

public class AStarStartPositionSubCommand : SubCommandBase
{
    public AStarStartPositionSubCommand()
    {
        Title = "[AStar Start Position]";
        Description = "Let's set the starting point of the path.";
        CallPrefix = $"{CommandManager.CommandPrefix}start||begin";
        AddParameter(new NumericSubCommandParameter<float>("x", "x=<new x>", false, "x"));
        AddParameter(new NumericSubCommandParameter<float>("y", "y=<new y>", false, "y"));
        AddParameter(new NumericSubCommandParameter<float>("z", "z=<new z>", false, "z"));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Npc npc;
        if (parameters.TryGetValue("ObjId", out ParameterValue npcObjId))
        {
            npc = ((Character)character).ParentWorld.GetNpc(npcObjId);
            if (npc is null)
            {
                SendColorMessage(messageOutput, Color.Coral, $"AStar: Npc with objId {npcObjId} does not exist");
                return;
            }
        }
        else
        {
            var currentTarget = ((Character)character).CurrentTarget;
            var target = currentTarget as Npc;
            if (currentTarget is null || target == null)
            {
                SendColorMessage(messageOutput, Color.Coral, $"AStar: You need to target a Npc first");
                return;
            }
            npc = target;
        }

        var x = GetOptionalParameterValue(parameters, "x", character.Transform.World.Position.X);
        var y = GetOptionalParameterValue(parameters, "y", character.Transform.World.Position.Y);
        var z = GetOptionalParameterValue(parameters, "z", character.Transform.World.Position.Z);

        npc.Ai.PathNode.Pos1 = new Vector3(x, y, z);

        messageOutput.SendMessage($"AStar: the starting point is set X:{npc.Ai.PathNode.Pos1.X}, Y:{npc.Ai.PathNode.Pos1.Y}, Z:{npc.Ai.PathNode.Pos1.Z}");
    }
}
