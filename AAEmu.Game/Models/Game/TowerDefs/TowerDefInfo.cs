using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDefInfo : PacketMarshaler
    {
        public TowerDefKey TowerDefKey { get; set; }
        public uint ZoneId { get; set; }
        public uint SpotId { get; set; }
        public uint TargetObjId { get; set; }
        public Point Position { get; set; }
        public uint CurrentStep { get; set; }
        
        public override PacketStream Write(PacketStream stream)
        {
            TowerDefKey.Write(stream);
            stream.Write(ZoneId);
            stream.Write(SpotId);
            stream.WriteBc(TargetObjId);
            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongX(Position.Y));
            stream.Write(Position.Z);
            stream.Write(CurrentStep);
            return stream;
        }
    }
}
