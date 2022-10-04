using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class Rotate : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("rotate", this);
        }

        public string GetCommandLineHelp()
        {
            return "[angle]";
        }

        public string GetCommandHelpText()
        {
            return "Rotate target unit towards you, or set it's local rotation to a given angle";
        }

        public void Execute(Character character, string[] args)
        {
            // if (args.Length < 2)
            // {
            //    character.SendMessage($"[Rotate] {CommandManager.CommandPrefix}rotate <objType: npc, doodad> <objId>");
            //    return;
            // }

            var target = character.CurrentTarget;
            if (target == null)
            {
                character.SendMessage("[Rotate] You need to target something first");
                return;
            }

            character.SendMessage("[Rotate] Unit: {0}, ObjId: {1}", target.Name, target.ObjId);

            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            var pos = target.Transform.World.Position;
            moveType.X = pos.X;
            moveType.Y = pos.Y;
            moveType.Z = pos.Z;

            var angle = (float)MathUtil.CalculateAngleFrom(target, character) - 90f;
            var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
            /*
            if (args.Length > 0)
            {
                if (!sbyte.TryParse(args[0], out rotZ))
                    character.SendMessage("Rotation sbyte out of range");
            }

            var angle2 = angle;
            angle = (float)MathUtil.ConvertSbyteDirectionToDegree(rotZ);
            */
            var local = target.Transform.Local;
            local.SetRotationDegree(0.0f, 0.0f, angle);
            //local.LookAt(character.Transform.Local.Position);

            moveType.RotationX = MathUtil.ConvertRadianToDirection(local.Rotation.X);
            moveType.RotationY = MathUtil.ConvertRadianToDirection(local.Rotation.Y);
            moveType.RotationZ = MathUtil.ConvertRadianToDirection(local.Rotation.Z);
            //moveType.RotationZ = rotZ;
            moveType.ActorFlags = 5;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1; //combat=0, idle=1
            moveType.Alertness = 0; //idle=0, combat=2
            moveType.Time += 50; // has to change all the time for normal motion.

            character.BroadcastPacket(new SCOneUnitMovementPacket(target.ObjId, moveType), true);
            character.SendMessage("New rotation {0}° ({1} rad, sbyte {2}) for {3}", angle, local.Rotation.Z.ToString("0.00"), rotZ, target.ObjId);
            character.SendMessage($"New position {local.ToString()}");
            //character.SendMessage($"New position A1:{angle}  A2:{angle2}  {local.ToString()}");
        }
    }
}
