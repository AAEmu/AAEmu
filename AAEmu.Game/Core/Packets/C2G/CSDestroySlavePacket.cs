using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDestroySlavePacket : GamePacket
    {
        public CSDestroySlavePacket() : base(CSOffsets.CSDestroySlavePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();

            _log.Debug("DestroySlave, Tl: {0}", tl);
        }
    }
}
