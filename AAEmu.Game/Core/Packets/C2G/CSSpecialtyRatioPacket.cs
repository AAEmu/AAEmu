using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpecialtyRatioPacket : GamePacket
    {
        public CSSpecialtyRatioPacket() : base(0x043, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Warn("SpecialtyRatio, Id: {0}", id);
        }
    }
}
