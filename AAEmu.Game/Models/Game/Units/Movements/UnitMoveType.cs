﻿using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class UnitMoveType : MoveType
    {
        public sbyte[] DeltaMovement { get; set; }
        public sbyte Stance { get; set; }
        public sbyte Alertness { get; set; }
        //public byte ColliOpt { get; set; } // add in 1200 march 2015
        public byte GcFlags { get; set; }
        public ushort GcPartId { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float Z2 { get; set; }
        public sbyte RotationX2 { get; set; }
        public sbyte RotationY2 { get; set; }
        public sbyte RotationZ2 { get; set; }
        public uint ClimbData { get; set; }
        public uint GcId { get; set; }
        public ushort FallVel { get; set; }
        public ushort ActorFlags { get; set; }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPosition();
            VelX = stream.ReadInt16();
            VelY = stream.ReadInt16();
            VelZ = stream.ReadInt16();
            RotationX = stream.ReadSByte();
            RotationY = stream.ReadSByte();
            RotationZ = stream.ReadSByte();
            DeltaMovement = new sbyte[3];
            DeltaMovement[0] = stream.ReadSByte();
            DeltaMovement[1] = stream.ReadSByte();
            DeltaMovement[2] = stream.ReadSByte();
            Stance = stream.ReadSByte();    // actor.stance
            Alertness = stream.ReadSByte(); // actor.alertness
            ActorFlags = stream.ReadUInt16(); // actor.flags in 1.2 ... 1.8 byte, in 2.0 ushort (flags(byte) + colliopt(byte))
            if ((ActorFlags & 0x8000) == 0x8000) // ActorFlags < 0
                FallVel = stream.ReadUInt16();   // actor.fallVel
            if ((ActorFlags & 0x20) == 0x20)
            {
                GcFlags = stream.ReadByte();          // actor.gcFlags
                GcPartId = stream.ReadUInt16();       // actor.gcPartId
                (X2, Y2, Z2) = stream.ReadPosition(); // ix, iy, iz
                RotationX2 = stream.ReadSByte();
                RotationY2 = stream.ReadSByte();
                RotationZ2 = stream.ReadSByte();
            }
            if ((ActorFlags & 0x60) != 0)
                GcId = stream.ReadUInt32(); // actor.gcId
            if ((ActorFlags & 0x40) == 0x40)
                ClimbData = stream.ReadUInt32(); // actor.climbData
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.WritePosition(X, Y, Z);
            stream.Write(VelX);
            stream.Write(VelY);
            stream.Write(VelZ);
            stream.Write(RotationX);
            stream.Write(RotationY);
            stream.Write(RotationZ);
            stream.Write(DeltaMovement[0]);
            stream.Write(DeltaMovement[1]);
            stream.Write(DeltaMovement[2]);
            stream.Write(Stance);
            stream.Write(Alertness);
            stream.Write(ActorFlags); // actor.flags in 1.2 ... 1.8 byte, in 2.0 ushort (flags(byte) + colliopt(byte))
            if ((ActorFlags & 0x8000) == 0x8000) // ActorFlags < 0
                stream.Write(FallVel);
            if ((ActorFlags & 0x20) == 0x20)
            {
                stream.Write(GcFlags);
                stream.Write(GcPartId);
                stream.WritePosition(X2, Y2, Z2);
                stream.Write(RotationX2);
                stream.Write(RotationY2);
                stream.Write(RotationZ2);
            }
            if ((ActorFlags & 0x60) != 0)
                stream.Write(GcId);
            if ((ActorFlags & 0x40) == 0x40)
                stream.Write(ClimbData);

            return stream;
        }
    }
}
