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

        /*
           // ----------------------------------------------------------------
           struct Vec3_tpl<float>
           {
           float x;
           float y;
           float z;
           };
           // ----------------------------------------------------------------
           struct Quat_tpl<float>
           {
           Vec3_tpl<float> v;
           float w;
           };
           // ----------------------------------------------------------------
           struct __declspec(align(8)) WorldPos
           {  
           __int64 x;
           __int64 y;
           float z;
           };   
           // ----------------------------------------------------------------
           X2::DescType<X2::GimmickIdTag, unsigned int, X2::DS3> id; 
           X2::DescType<X2::GimmickDesc, unsigned int, X2::DSF> type; 
           unsigned __int64 entityGUID; 
           X2::DescType<X2::SystemFactionDesc,unsigned int, X2::DSF> faction; 
           X2::DescType<X2::UnitIdTag, unsigned int, X2::DS3> spawnerUnitId; 
           X2::DescType<X2::UnitIdTag, unsigned int, X2::DS3> grasperUnitId; 
           int staticZoneId; 
           char modelPath[100]; 
           WorldPos pos; 
           Quat_tpl<float> rot; 
           float scale; 
           Vec3_tpl<float> vel; 
           Vec3_tpl<float> angVel; 
           float scaleVel; 
           // ----------------------------------------------------------------
        */
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        //public Guid EntityGuid { get; set; } // TODO Guid есть в GameObject
        //public SystemFaction Faction { get; set; } // TODO Guid есть в GameObject
        public GimmickTemplate Template { get; set; }
        public uint SpawnerUnitId { get; set; }
        public uint GrasperUnitId { get; set; }
        public string ModelPath { get; set; }
        //public int StaticZoneId { get; set; } // TODO есть ZoneId в Position в GameObject
        //public WorldPos Pos { get; set; } // TODO есть Position в GameObject
        public Quaternion Rotation { get; set; }
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
            var gimmicks = GetList(new List<Gimmick>(), this.ObjId).ToArray();
            for (var i = 0; i < gimmicks.Length; i += 30)
            {
                var count = gimmicks.Length - i;
                var temp = new Gimmick[count <= 30 ? count : 30];
                Array.Copy(gimmicks, i, temp, 0, temp.Length);
                character.SendPacket(new SCGimmicksCreatedPacket(temp));
            }
        }

        private List<T> GetList<T>(List<T> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }
            foreach (var obj in temp)
            {
                var item = obj as T;
                if (item != null && obj.ObjId != exclude)
                    result.Add(item);
            }

            return result;
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            var gimmickIds = GetListId<Gimmick>(new List<uint>(), character.ObjId).ToArray();
            for (var offset = 0; offset < gimmickIds.Length; offset += 500)
            {
                var length = gimmickIds.Length - offset;
                var last = length <= 500;
                var temp = new uint[last ? length : 500];
                Array.Copy(gimmickIds, offset, temp, 0, temp.Length);
                character.SendPacket(new SCGimmicksRemovedPacket(temp));
            }
        }

        private readonly object _objectsLock = new object();
        private GameObject[] _objects;
        private int _objectsSize;
        private List<uint> GetListId<T>(List<uint> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                {
                    return result;
                }

                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
            {
                if (obj is T && obj.ObjId != exclude)
                {
                    result.Add(obj.ObjId);
                }
            }

            return result;
        }

        private float _scale;
        public void SetScale(float scale)
        {
            _scale = scale;
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(TemplateId); // GimmickId
            stream.Write(0);          // type
            stream.Write((long)123);  // entityGUID
            stream.Write(0);          // type
            stream.Write(SpawnerUnitId);
            stream.Write(GrasperUnitId);
            stream.Write(Position.ZoneId);
            stream.Write(ModelPath);

            stream.Write(Helpers.ConvertLongX(Position.X));  // WorldPos
            stream.Write(Helpers.ConvertLongX(Position.Y));
            stream.Write(Position.Z);

            stream.Write(Rotation.X); // Quaternion Rotation
            stream.Write(Rotation.Y);
            stream.Write(Rotation.Z);
            stream.Write(Rotation.W);

            stream.Write(Scale);
            stream.Write(Vel.X);
            stream.Write(Vel.Y);
            stream.Write(Vel.Z);
            stream.Write(AngVel.X);
            stream.Write(AngVel.Y);
            stream.Write(AngVel.Z);
            stream.Write(ScaleVel);

            return stream;
        }
    }
}
