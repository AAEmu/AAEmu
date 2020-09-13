using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Gimmicks;

using NLog;

namespace AAEmu.Game.Models.Game.Gimmicks
{
    public class Gimmick : Unit
    {
        private static Logger s_log = LogManager.GetCurrentClassLogger();

        public uint GimmickId { get; set; } // obj
        public uint TemplateId { get; set; }
        public long EntityGuid { get; set; } // TODO это не Guid в GameObject
        //public SystemFaction Faction { get; set; } // TODO Guid есть в GameObject
        public GimmickTemplate Template { get; set; }
        public uint SpawnerUnitId { get; set; }
        public uint GrasperUnitId { get; set; }
        public string ModelPath { get; set; }
        //public int StaticZoneId { get; set; } // TODO есть ZoneId в Position в GameObject
        public Quaternion Rot { get; set; } // углы должны быть в радианах
        //public float Scale { get; set; } // TODO есть Scale в BaseUnit
        public Vector3 Vel { get; set; }
        public Vector3 AngVel { get; set; }
        public float ScaleVel { get; set; }
        public uint Time { get; set; }
        //public bool isRunning { get; set; }
        public GimmickSpawner Spawner { get; set; }
        public GimmickTask GimmickTask { get; set; }

        public Gimmick()
        {
            Ai = new GimmickAi(this, 50f);
            UnitType = BaseUnitType.Transfer; // TODO какое на самом деле?
            Position = new Point();
            WorldPos = new WorldPos();

            //EntityGuid = BitConverter.ToInt64(Guid.ToByteArray(), 0);
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
            {
                character.SendPacket(packet);
            }
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCGimmicksCreatedPacket(new[] { this }));
            var temp = new Gimmick[0];
            character.SendPacket(new SCGimmickJointsBrokenPacket(temp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            character.SendPacket(new SCGimmicksRemovedPacket(new[] { GimmickId }));
        }

        private float _scale;
        public void SetScale(float scale)
        {
            _scale = scale;
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(GimmickId);  // GimmickId
            stream.Write(0);          // TemplateId
            stream.Write(EntityGuid); // entityGUID = 0x4227234CE506AFDB box
            stream.Write(0);          // Faction
            stream.Write(SpawnerUnitId);
            stream.Write(GrasperUnitId);
            stream.Write(Position.ZoneId);
            stream.Write((short)0);   // ModelPath
            stream.WriteWorldPosition(Position.X, Position.Y, Position.Z); // WorldPos
            stream.WriteQuaternionSingle(Rot, true); // Quaternion Rotation
            stream.Write(Scale);
            stream.WriteVector3Single(Vel);
            stream.WriteVector3Single(AngVel);

            stream.Write(ScaleVel);

            return stream;
        }
    }
}
