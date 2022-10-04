using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nloc : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("nloc", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <x> <y> <z>";
        }

        public string GetCommandHelpText()
        {
            return "Change target unit position";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 3)
            {
                character.SendMessage($"[nloc] {CommandManager.CommandPrefix}nloc <x> <y> <z> - Use x y z instead of a value to keep current position");
                return;
            }

            if (character.CurrentTarget != null)
            {
                var transform = character.CurrentTarget.Transform;
                float x = transform.World.Position.X;
                float y = transform.World.Position.Y;
                float z = transform.World.Position.Z;
                if (float.TryParse(args[0], out float xv) && args[0] != "x")
                {
                    x = xv;
                }
                if (float.TryParse(args[1], out float yv) && args[1] != "y")
                {
                    y = yv;
                }
                if (float.TryParse(args[2], out float zv) && args[0] != "z")
                {
                    z = zv;
                }

                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                moveType.X = x;
                moveType.Y = y;
                moveType.Z = z;
                transform.Local.SetPosition(x, y, z);

                var characterRot = transform.World.ToRollPitchYawSBytes();
                moveType.RotationX = characterRot.Item1;
                moveType.RotationY = characterRot.Item2;
                moveType.RotationZ = characterRot.Item3;
                moveType.ActorFlags = 5;
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1; //combat=0, idle=1
                moveType.Alertness = 0; //idle=0, combat=2
                moveType.Time += 50; // has to change all the time for normal motion.

                character.SendMessage("[nloc] New position {0} {1} {2}", transform.World.Position.X, transform.World.Position.Y, transform.World.Position.Z);
                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
            }
            else
            {
                character.SendMessage("[nloc] You need to target something first");
            }
        }
    }
}
