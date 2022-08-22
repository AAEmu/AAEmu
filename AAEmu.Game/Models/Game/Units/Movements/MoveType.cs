using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World;

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
        public WorldPos WorldPos { get; set; }
        public byte Flags { get; set; }
        public uint ScType { get; set; }
        public byte Phase { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public Quaternion Rot { get; set; } // значение поворота по оси Z должно быть в радианах
        public Vector3 Velocity { get; set; }
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
                //_log.Warn("ScType: {0} Phase: {1}", ScType, Phase);
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
                    mType = new TransferData();
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
