using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public enum MoveTypeEnum
    {
        Default = 0,
        Unit = 1,
        Vehicle = 2,
        Ship = 3,
        ShipRequest = 4,
        Transfer = 5
    }

    public abstract class MoveType : PacketMarshaler
    {
        public MoveTypeEnum Type { get; set; }
        public uint Time { get; set; }
        public byte Flags { get; set; }
        public uint ScType { get; set; }
        public byte Phase { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public short VelX { get; set; }
        public short VelY { get; set; }
        public short VelZ { get; set; }
        public sbyte RotationX { get; set; }
        public sbyte RotationY { get; set; }
        public sbyte RotationZ { get; set; }

        public override void Read(PacketStream stream)
        {
            Time = stream.ReadUInt32();
            Flags = stream.ReadByte();
            if ((Flags & 0x10) == 0x10)
            {
                ScType = stream.ReadUInt32();
                Phase = stream.ReadByte();
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Time);
            stream.Write(Flags);
            if ((Flags & 0x10) == 0x10)
            {
                stream.Write(ScType);
                stream.Write(Phase);
            }

            return stream;
        }

        public static MoveType GetType(MoveTypeEnum type)
        {
            MoveType mType = null;
            switch (type)
            {
                case MoveTypeEnum.Unit:
                    mType = new UnitMoveType();
                    break;
                case MoveTypeEnum.Vehicle:
                    mType = new VehicleMoveType();
                    break;
                case MoveTypeEnum.Ship:
                    mType = new ShipMoveType();
                    break;
                case MoveTypeEnum.ShipRequest:
                    mType = new ShipRequestMoveType();
                    break;
                case MoveTypeEnum.Transfer:
                    // TODO ...
                    break;
                default:
                    mType = new DefaultMoveType();
                    break;
            }

            if (mType != null)
                mType.Type = type;
            return mType;
        }
    }
}
