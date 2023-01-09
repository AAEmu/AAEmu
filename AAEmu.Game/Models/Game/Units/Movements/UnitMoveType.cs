using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public enum EStance : sbyte
    {
        Null = -1,
        Combat = 0,
        Idle = 1,
        Swim = 2,
        Coswim = 3,
        Zerog = 4,
        Stealth = 5,
        Climb = 6,
        Prone = 7,
        Fly = 8,
        Last = 9
    }
    public enum AiAlertness : sbyte
    {
        Idle = 0,
        Alert = 1,
        Combat = 2
    }
    public enum ActorMoveType : ushort
    {
        StandStill = 3,
        Run = 4,
        Walk = 5,
    }

    public class UnitMoveType : MoveType
    {
        public sbyte[] DeltaMovement { get; set; }
        public EStance Stance { get; set; }
        public AiAlertness Alertness { get; set; }
        public byte GcFlags { get; set; }
        public ushort GcPart { get; set; }
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
        public ActorMoveType ActorFlags { get; set; }
        public uint MaxPushedUnitId { get; set; }

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
            Stance = (EStance)stream.ReadSByte();
            Alertness = (AiAlertness)stream.ReadSByte();
            ActorFlags = (ActorMoveType)stream.ReadUInt16(); // ushort in 3.0.3.0, sbyte in 1.2
            if (((ushort)ActorFlags & 0x80) == 0x80)
            {
                FallVel = stream.ReadUInt16(); // actor.fallVel
            }

            if (((ushort)ActorFlags & 0x20) == 0x20) // TODO если находится на движущейся повозке/лифте/корабле, то здесь координаты персонажа
            {
                GcFlags = stream.ReadByte(); // actor.gcFlags
                GcPart = stream.ReadUInt16(); // actor.gcPart
                GcPartId = stream.ReadUInt16(); // actor.gcPartId
                (X2, Y2, Z2) = stream.ReadPosition(); // ix, iy, iz
                RotationX2 = stream.ReadSByte();
                RotationY2 = stream.ReadSByte();
                RotationZ2 = stream.ReadSByte();
            }
            if (((ushort)ActorFlags & 0x60) != 0)
            {
                GcId = stream.ReadUInt32(); // actor.gcId
            }

            if (((ushort)ActorFlags & 0x40) == 0x40)
            {
                ClimbData = stream.ReadUInt32(); // actor.climbData
            }
            if (((ushort)ActorFlags & 0x100) == 0x100)
            {
                MaxPushedUnitId = stream.ReadUInt32(); // actor.maxPushedUnitId
            }
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
            stream.Write((sbyte)Stance);
            stream.Write((sbyte)Alertness);
            stream.Write((ushort)ActorFlags);
            if (((ushort)ActorFlags & 0x80) == 0x80)
            {
                stream.Write(FallVel);
            }

            if (((ushort)ActorFlags & 0x20) == 0x20)
            {
                stream.Write(GcFlags);
                stream.Write(GcPart);
                stream.Write(GcPartId);
                stream.WritePosition(X2, Y2, Z2);
                stream.Write(RotationX2);
                stream.Write(RotationY2);
                stream.Write(RotationZ2);
            }
            if (((ushort)ActorFlags & 0x60) != 0)
            {
                stream.Write(GcId);
            }

            if (((ushort)ActorFlags & 0x40) == 0x40)
            {
                stream.Write(ClimbData);
            }
            if (((ushort)ActorFlags & 0x100) == 0x100)
            {
                stream.Write(MaxPushedUnitId);
            }

            return stream;
        }
    }
}
