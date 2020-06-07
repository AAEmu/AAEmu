using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.DoodadObj;
using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Slave : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;

        public SlaveTemplate Template { get; set; }
        public Character Bounded { get; set; }
        public Character Summoner { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }
        public DateTime SpawnTime { get; set; }
        
        // Ships
        public float AngVelX { get; set; }
        public float AngVelY { get; set; }
        public float AngVelZ { get; set; }
        public short VelX { get; set; }
        public short VelY { get; set; }
        public short VelZ { get; set; }
        public sbyte Steering { get; set; }
        public sbyte Throttle { get; set; }
        public sbyte RequestThrottle { get; set; }
        public sbyte RequestSteering { get; set; }
        public bool Stuck { get; set; }
        public float Speed { get; set; }
        public float RotSpeed { get; set; }
        public short RotationZ { get; set; }
        public float RotationDegrees { get; set; }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner.Name, Summoner.ObjId, Template.Id));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] {ObjId}));
        }
    }
}
