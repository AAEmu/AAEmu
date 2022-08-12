using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using Point = AAEmu.Game.Models.Game.AI.AStar.Point;

namespace AAEmu.Game.Utils.Scripts.SubCommands.AStar
{
    public class AStarStartPositionSubCommand : SubCommandBase
    {
        public AStarStartPositionSubCommand()
        {
            Title = "[AStar Start Position]";
            Description = "Let's set the starting point of the path.";
            CallPrefix = $"{CommandManager.CommandPrefix}pf start||begin";
            AddParameter(new NumericSubCommandParameter<float>("x", "x=<new x>", false, "x"));
            AddParameter(new NumericSubCommandParameter<float>("y", "y=<new y>", false, "y"));
            AddParameter(new NumericSubCommandParameter<float>("z", "z=<new z>", false, "z"));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Npc npc;
            if (parameters.TryGetValue("ObjId", out ParameterValue npcObjId))
            {
                npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc is null)
                {
                    SendColorMessage(character, Color.Red, $"AStar: Npc with objId {npcObjId} does not exist |r");
                    return;
                }
            }
            else
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Npc))
                {
                    SendColorMessage(character, Color.Red, $"AStar: You need to target a Npc first");
                    return;
                }
                npc = (Npc)currentTarget;
            }

            var x = GetOptionalParameterValue(parameters, "x", npc.Transform.Local.Position.X);
            var y = GetOptionalParameterValue(parameters, "y", npc.Transform.Local.Position.Y);
            var z = GetOptionalParameterValue(parameters, "z", npc.Transform.Local.Position.Z);
            
            PathNode.pos1 = new Point(x, y, z);

            character.SendMessage($"AStar: the starting point is set X:{PathNode.pos1.X}, Y:{PathNode.pos1.Y}, Z:{PathNode.pos1.Z}");
        }
    }
}
