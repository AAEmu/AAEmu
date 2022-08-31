using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class NpcPositionSubCommand : SubCommandBase
    {
        public NpcPositionSubCommand()
        {
            Title = "[Npc Position]";
            Description = "Change npc position and angle - All positions are optional use all or only the ones you want to change (Use yaw to rotate npc)";
            CallPrefix = $"{CommandManager.CommandPrefix}npc position||pos";
            AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
            AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
            AddParameter(new NumericSubCommandParameter<float>("x", "x=<new x>", false, "x"));
            AddParameter(new NumericSubCommandParameter<float>("y", "y=<new y>", false, "y"));
            AddParameter(new NumericSubCommandParameter<float>("z", "z=<new z>", false, "z"));
            AddParameter(new NumericSubCommandParameter<float>("roll", "roll=<new roll degrees>", false, "roll", 0, 360));
            AddParameter(new NumericSubCommandParameter<float>("pitch", "pitch=<new pitch degrees>", false, "pitch", 0, 360));
            AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<new yaw degrees>", false, "yaw", 0, 360));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Npc npc;
            if (parameters.TryGetValue("ObjId", out ParameterValue npcObjId))
            {
                npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc is null)
                {
                    SendColorMessage(character, Color.Red, "Npc with objId {0} does not exist |r", npcObjId);
                    return;
                }
            }
            else
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Npc))
                {
                    SendColorMessage(character, Color.Red, "You need to target a Npc first");
                    return;
                }
                npc = (Npc)currentTarget;
            }

            var x = GetOptionalParameterValue(parameters, "x", npc.Transform.Local.Position.X);
            var y = GetOptionalParameterValue(parameters, "y", npc.Transform.Local.Position.Y);
            var z = GetOptionalParameterValue(parameters, "z", npc.Transform.Local.Position.Z);
            var yaw = GetOptionalParameterValue(parameters, "yaw", npc.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();
            var pitch = GetOptionalParameterValue(parameters, "pitch", npc.Transform.Local.Rotation.Y.RadToDeg()).DegToRad();
            var roll = GetOptionalParameterValue(parameters, "roll", npc.Transform.Local.Rotation.X.RadToDeg()).DegToRad();

            SendMessage(character, "Npc ObjId:{0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, roll:{5:0.#}°, pitch:{6:0.#}°, yaw:{7:0.#}°", 
                npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());
            
            npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = x;
            moveType.Y = y;
            moveType.Z = z;
            var npcRot = npc.Transform.Local.ToRollPitchYawSBytes();
            moveType.RotationX = npcRot.Item1;
            moveType.RotationY = npcRot.Item2;
            moveType.RotationZ = npcRot.Item3;

            moveType.Flags = 5;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // combat=0, idle=1
            moveType.Alertness = 0; // idle=0, combat=2
            moveType.Time += 50;    // has to change all the time for normal motion.
            character.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
        }
    }
}
