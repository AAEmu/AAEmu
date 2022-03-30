using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConvertItemLookPacket : GamePacket
    {
        public CSConvertItemLookPacket() : base(CSOffsets.CSConvertItemLookPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var baseId = stream.ReadUInt64();
            var lookId = stream.ReadUInt64();

            _log.Warn("ConvertItemLook, BaseId: {0}, LookId: {1}", baseId, lookId);
        }
    }
}
