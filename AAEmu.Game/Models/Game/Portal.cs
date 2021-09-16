using System.Collections.Generic;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Zones;

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

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name); // TODO max length 128
            stream.Write(ZoneId);
            var origin = ZoneManager.Instance.GetZoneOriginCell(ZoneId);
            var offX = (X - (origin.X * 1024f));
            var offY = (Y - (origin.Y * 1024f));
            stream.Write(offX);
            stream.Write(offY);
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
