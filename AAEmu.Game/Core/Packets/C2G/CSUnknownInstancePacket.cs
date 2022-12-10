using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnknownInstancePacket : GamePacket
    {
        public CSUnknownInstancePacket() : base(CSOffsets.CSUnknownInstancePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt16();
            var zoneId = stream.ReadInt32();
            var instId = stream.ReadUInt32();
        }
    }
}
