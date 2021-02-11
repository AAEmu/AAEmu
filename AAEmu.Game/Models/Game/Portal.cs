using System.Collections.Generic;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game
{
    public class Portal : PacketMarshaler
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint ZoneId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float ZRot { get; set; }

        public uint SubZoneId { get; set; }
        public uint Owner { get; set; }
        public uint DoodadId { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name); // TODO max length 128
            stream.Write(ZoneId);
            stream.Write(X);
            stream.Write(Y);
            stream.Write(Z);
            stream.Write(ZRot);
            return stream;
        }

    }

    public class VisitedDistrict
    {
        public uint Id { get; set; }
        public uint SubZone { get; set; }
        public uint Owner { get; set; }
    }
}
