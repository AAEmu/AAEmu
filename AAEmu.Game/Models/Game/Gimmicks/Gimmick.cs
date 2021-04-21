using System;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Gimmicks;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Gimmicks
{
    public class Gimmick : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Transfer; // TODO для Gimmick не понятно что выбрать
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
        /// <summary>
        /// MoveZ
        /// </summary>
        public bool moveDown  { get; set; } = false;
        public DateTime WaitTime { get; set; }
        public uint TimeLeft => WaitTime > DateTime.Now ? (uint)(WaitTime - DateTime.Now).TotalMilliseconds : 0;
        public Gimmick()
        {
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

        public void SetScale(float scale)
        {
            Scale = scale;
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(GimmickId);        // GimmickId
            stream.Write(0);                // TemplateId
            stream.Write(EntityGuid);       // entityGUID = 0x4227234CE506AFDB box
            stream.Write(0);                // Faction
            stream.Write(SpawnerUnitId);    // spawnerUnitId
            stream.Write(GrasperUnitId);    // grasperUnitId
            stream.Write(Position.ZoneId);
            stream.Write((short)0);         // ModelPath
            
            stream.Write(Helpers.ConvertLongX(Position.X)); // WorldPosition qx,qx,fz
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);
            
            stream.Write(Rot.X); // Quaternion Rotation
            stream.Write(Rot.Y);
            stream.Write(Rot.Z);
            stream.Write(Rot.W);

            stream.Write(Scale);

            stream.Write(Vel.X);    // vector3 vel
            stream.Write(Vel.Y);
            stream.Write(Vel.Z);

            stream.Write(AngVel.X); // vector3 angVel
            stream.Write(AngVel.Y);
            stream.Write(AngVel.Z);

            stream.Write(ScaleVel);

            return stream;
        }
    }
}
