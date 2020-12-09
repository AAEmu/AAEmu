using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.DoodadObj;
using Jitter.Dynamics;
using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Slave : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Slave;
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;

        public SlaveTemplate Template { get; set; }
        public Character Bounded { get; set; }
        public Character Summoner { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }
        public List<Slave> AttachedSlaves { get; set; }
        public DateTime SpawnTime { get; set; }
        public sbyte ThrottleRequest { get; set; }
        public sbyte Throttle { get; set; }
        public float Speed { get; set; }
        public sbyte SteeringRequest { get; set; }
        public sbyte Steering { get; set; }
        public float RotSpeed { get; set; }
        public short RotationZ { get; set; }
        public float RotationDegrees { get; set; }
        public sbyte AttachPointId { get; set; } = -1;
        public uint OwnerObjId { get; set; }
        public RigidBody RigidBody { get; set; }
        

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
        
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        /// <summary>
        /// Moves a slave by X, Y & Z. Also moves attached slaves, doodads & driver
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public void Move(float newX, float newY, float newZ)
        {
            var xD = newX - Position.X;
            var yD = newY - Position.Y;
            var zD = newZ - Position.Z;
            SetPosition(newX, newY, newZ);
            
            foreach (var doodad in AttachedDoodads)
            {
                doodad.SetPosition(doodad.Position.X + xD, doodad.Position.Y + yD, doodad.Position.Z + zD);
            }
            
            foreach (var attachedSlave in AttachedSlaves)
            {
                attachedSlave.Move(newX, newY, newZ);
            }
            
            Bounded?.SetPosition(Bounded.Position.X + xD, Bounded.Position.Y + yD, Bounded.Position.Z + zD);
        }
    }
}
