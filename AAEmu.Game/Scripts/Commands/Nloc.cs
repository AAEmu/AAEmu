using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Commons.Utils;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nloc : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
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
            return "change target unit position";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 3)
            {
                character.SendMessage("[nloc] /npos <x> <y> <z> - Use x y z instead of a value to keep current position");
                return;
            }

            if (character.CurrentTarget != null)
            {
                float value = 0;
                float x = character.CurrentTarget.Position.X;
                float y = character.CurrentTarget.Position.Y;
                float z = character.CurrentTarget.Position.Z;

                if (float.TryParse(args[0], out value) && args[0] != "x")
                {
                    x = value;
                }

                if (float.TryParse(args[1], out value) && args[1] != "y")
                {
                    y = value;
                }

                if (float.TryParse(args[2], out value) && args[0] != "z")
                {
                    z = value;
                }

                var Seq = (uint)Rand.Next(0, 10000);
                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

                moveType.X = x;
                moveType.Y = y;
                moveType.Z = z;
                character.CurrentTarget.Position.X = x;
                character.CurrentTarget.Position.Y = y;
                character.CurrentTarget.Position.Z = z;


                moveType.RotationX = character.CurrentTarget.Position.RotationX;
                moveType.RotationY = character.CurrentTarget.Position.RotationY;
                moveType.RotationZ = character.CurrentTarget.Position.RotationZ;

                moveType.Flags = 5;
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1; //combat=0, idle=1
                moveType.Alertness = 0; //idle=0, combat=2
                moveType.Time = Seq;

                character.SendMessage("[nloc] New position {0} {1} {2}", character.CurrentTarget.Position.X, character.CurrentTarget.Position.Y, character.CurrentTarget.Position.Z);
                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
            }
            else
                character.SendMessage("[nloc] You need to target something first");
        }
    }
}
