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
                float x = character.CurrentTarget.Transform.World.Position.X;
                float y = character.CurrentTarget.Transform.World.Position.Y;
                float z = character.CurrentTarget.Transform.World.Position.Z;

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

                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

                moveType.X = x;
                moveType.Y = y;
                moveType.Z = z;
                character.CurrentTarget.Transform.Local.SetPosition(x, y, z);

                var characterRot = character.CurrentTarget.Transform.World.ToRollPitchYawSBytes();
                moveType.RotationX = characterRot.Item1 ;
                moveType.RotationY = characterRot.Item2 ;
                moveType.RotationZ = characterRot.Item3 ;

                moveType.ActorFlags = ActorMoveType.Walk;     // 5-walk, 4-run, 3-stand still
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = EStance.Idle;    // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = AiAlertness.Idle; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 100; // has to change all the time for normal motion.

                character.SendMessage("[nloc] New position {0} {1} {2}", character.CurrentTarget.Transform.World.Position.X, character.CurrentTarget.Transform.World.Position.Y, character.CurrentTarget.Transform.World.Position.Z);
                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
            }
            else
                character.SendMessage("[nloc] You need to target something first");
        }
    }
}
