using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

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
        public Transform Position { get; set; }

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

        public PlotObject(Transform position) 
        {
            Type = PlotObjectType.POSITION;
            Position = position.CloneDetached();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)Type);

            switch (Type) {
                case PlotObjectType.UNIT:
                    stream.WriteBc(UnitId);
                    break;
                case PlotObjectType.POSITION:
                    stream.WritePosition(Position.Local.Position);
                    var ypr = Position.Local.ToRollPitchYawSBytes();
                    stream.Write(ypr.Item1);
                    stream.Write(ypr.Item2);
                    stream.Write(ypr.Item3);
                    break;
            }

            return stream;
        }
    }
}
