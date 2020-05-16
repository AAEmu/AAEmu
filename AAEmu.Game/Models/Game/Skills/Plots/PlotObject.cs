using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public enum PlotObjectType : byte {
        UNIT = 0x1,
        POSITION = 0x2
    }

    public class PlotObject : PacketMarshaler
    {
        public PlotObjectType Type { get; set; }
        public uint UnitId { get; set; }
        public Point Position { get; set; }

        public PlotObject(BaseUnit unit) 
        {
            Type = PlotObjectType.UNIT;
            UnitId = unit.ObjId;
        }

        public PlotObject(uint unitId) 
        {
            Type = PlotObjectType.UNIT;
            UnitId = unitId;
        }

        public PlotObject(Point position) 
        {
            Type = PlotObjectType.POSITION;
            Position = position;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)Type);

            switch (Type) {
                case PlotObjectType.UNIT:
                    stream.WriteBc(UnitId);
                    break;
                case PlotObjectType.POSITION:
                    stream.WritePosition(Position.X, Position.Y, Position.Z);
                    stream.Write(Position.RotationX);
                    stream.Write(Position.RotationY);
                    stream.Write(Position.RotationZ); 
                    break;
            }

            return stream;
        }
    }
}
