﻿using AAEmu.Game.Core.Managers;
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
    public class Rotate : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
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
            //if (args.Length < 2)
            //{
            //    character.SendMessage("[Rotate] /rotate <objType: npc, doodad> <objId>");
            //    return;
            //}

            if (character.CurrentTarget != null)
            {
                character.SendMessage("[Rotate] Unit: {0}, ObjId: {1}", character.CurrentTarget.Name, character.CurrentTarget.ObjId);

                var Seq = (uint)Rand.Next(0, 10000);
                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                
                moveType.X = character.CurrentTarget.Transform.World.Position.X;
                moveType.Y = character.CurrentTarget.Transform.World.Position.Y;
                moveType.Z = character.CurrentTarget.Transform.World.Position.Z;

                var angle = (float)MathUtil.CalculateAngleFrom(character.CurrentTarget, character);
                var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
                if (args.Length > 0) 
                {
                    sbyte.TryParse(args[0], out rotZ);
                }

                moveType.RotationX = 0;
                moveType.RotationY = 0;
                moveType.RotationZ = rotZ;

                character.CurrentTarget.Transform.Local.SetZRotation((float)MathUtil.DegreeToRadian(angle));
                // character.CurrentTarget.Transform.Local.SetZRotation(rotZ);

                moveType.Flags = 5;
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1; //combat=0, idle=1
                moveType.Alertness = 0; //idle=0, combat=2
                moveType.Time = Seq;

                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
                character.SendMessage("New rotation {0}° ({1} rad, sbyte {2}) for {3}",angle, character.CurrentTarget.Transform.Local.Rotation.Z.ToString("0.00"),rotZ,character.CurrentTarget.ObjId);
            }
            else
                character.SendMessage("[Rotate] You need to target something first");
        }
    }
}
