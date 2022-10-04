using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nrot : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("nrot", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <x> <y> <z>";
        }

        public string GetCommandHelpText()
        {
            return "Change target unit rotation";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 3)
            {
                character.SendMessage($"[nrot] {CommandManager.CommandPrefix}nrot <x> <y> <z>");
                return;
            }

            if (character.CurrentTarget != null)
            {
                var local = character.CurrentTarget.Transform.Local;

                float x = local.Position.X;
                float y = local.Position.Y;
                float z = local.Position.Z;
                if (float.TryParse(args[0], out float vx) && args[0] != "x")
                {
                    x = vx;
                }
                if (float.TryParse(args[1], out float vy) && args[1] != "y")
                {
                    y = vy;
                }
                if (float.TryParse(args[2], out float vz) && args[0] != "z")
                {
                    z = vz;
                }

                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                moveType.X = local.Position.X;
                moveType.Y = local.Position.Y;
                moveType.Z = local.Position.Z;
                local.SetRotationDegree(x, y, z);

                var characterRot = local.ToRollPitchYawSBytes();
                moveType.RotationX = characterRot.Item1;
                moveType.RotationY = characterRot.Item2;
                moveType.RotationZ = characterRot.Item3;
                moveType.Flags = 5;
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1; //combat=0, idle=1
                moveType.Alertness = 0; //idle=0, combat=2
                moveType.Time = (uint)Rand.Next(0, 10000);

                character.SendMessage($"[nrot] New position {local.ToString()}");
                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
            }
            else
                character.SendMessage("[nrot] You need to target something first");
        }
    }
}
