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
using System.Numerics;

namespace AAEmu.Game.Scripts.Commands
{
    public class Rotate : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("rotate", this);
        }

        public string GetCommandLineHelp()
        {
            return "<npc||doodad> <objId>";
        }

        public string GetCommandHelpText()
        {
            return "Rotate target unit towards you";
        }

        public void Execute(Character character, string[] args)
        {
            //if (args.Length < 2)
            //{
            //    character.SendMessage("[Rotate] /rotate <objType: npc, doodad> <objId>");
            //    return;
            //}

            if (character.CurrentTarget != null)
            {
                character.SendMessage("[Rotate] Unit: {0}, ObjId: {1}", character.CurrentTarget.Name, character.CurrentTarget.ObjId);

                var Seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                var moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

                moveType.X = character.CurrentTarget.Position.X;
                moveType.Y = character.CurrentTarget.Position.Y;
                moveType.Z = character.CurrentTarget.Position.Z;

                var angle = MathUtil.CalculateAngleFrom(character.CurrentTarget, character);
                var rotZ = MathUtil.ConvertDegreeToDirection(angle);

                //var direction = new Vector3();
                //if (vDistance != Vector3.Zero)
                //    direction = Vector3.Normalize(vDistance);
                ////var rotation = (float)Math.Atan2(direction.Y, direction.X);

                //moveType.Rot = Quaternion.CreateFromAxisAngle(direction, rotZ);
                moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                moveType.Flags = 5;

                //moveType.DeltaMovement = new sbyte[3];
                //moveType.DeltaMovement[0] = 0;
                //moveType.DeltaMovement[1] = 0;
                //moveType.DeltaMovement[2] = 0;
                moveType.DeltaMovement = Vector3.Zero;

                moveType.Stance = 1; //combat=0, idle=1
                moveType.Alertness = 0; //idle=0, combat=2
                moveType.Time = Seq;

                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
            }
            else
                character.SendMessage("[Rotate] You need to target something first");
        }
    }
}
