using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class NpcPositionSubCommand : SubCommandBase
    {
        public NpcPositionSubCommand()
        {
            Prefix = "[Npc Position]";
            Description = "Change npc position and angle";
            CallExample = "/npc target||<ObjId> x=<x> y=<y> z=<z> roll=<roll> pitch=<pitch> yaw=<yaw> - All positions are optional use all or only the ones you want to change (Use yaw to rotate npc)";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            Npc npc;
            var firstArgument = args.FirstOrDefault();
            if (firstArgument is null)
            {
                SendMessage(character, CallExample);
                return;
            }
            if (firstArgument == "target")
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Npc))
                {
                    SendColorMessage(character, Color.Red, "You need to target a Npc");
                    return;
                }
                npc = (Npc)currentTarget;
            }
            else if (!uint.TryParse(firstArgument, out var npcObjId)) 
            {
                SendMessage(character, "Invalid <ObjId> for Npc, please use a number");
                return;
            }
            else
            {
                npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc is null)
                {
                    SendColorMessage(character, Color.Red, "Npc with objId {0} does not exist |r", npcObjId);
                    return;
                }
            }

            var x = GetOptionalArgumentValue(args, "x", npc.Transform.Local.Position.X);
            var y = GetOptionalArgumentValue(args, "y", npc.Transform.Local.Position.Y);
            var z = GetOptionalArgumentValue(args, "z", npc.Transform.Local.Position.Z);
            var yaw = GetOptionalArgumentValue(args, "yaw", npc.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();
            var pitch = GetOptionalArgumentValue(args, "pitch", npc.Transform.Local.Rotation.Y.RadToDeg()).DegToRad();
            var roll = GetOptionalArgumentValue(args, "roll", npc.Transform.Local.Rotation.X.RadToDeg()).DegToRad();

            SendMessage(character, "Npc ObjId:{0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, roll:{5:0.#" +
                "}°, pitch:{6:0.#}°, yaw:{7:0.#}°", npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());
            npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = x;
            moveType.Y = y;
            moveType.Z = z;
            var characterRot = ((Character)character).CurrentTarget.Transform.Local.ToRollPitchYawSBytes();
            moveType.RotationX = characterRot.Item1;
            moveType.RotationY = characterRot.Item2;
            moveType.RotationZ = characterRot.Item3;

            moveType.Flags = 5;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // combat=0, idle=1
            moveType.Alertness = 0; // idle=0, combat=2
            moveType.Time += 50;    // has to change all the time for normal motion.
            character.BroadcastPacket(new SCOneUnitMovementPacket(((Character)character).CurrentTarget.ObjId, moveType), true);
        }
    }
}
