using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveChatChannelPacket : GamePacket
    {
        public CSLeaveChatChannelPacket() : base(CSOffsets.CSLeaveChatChannelPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var chat = stream.ReadInt64(); // TODO нужно разложить

            _log.Debug("LeaveChatChannel, Chat: {0}", chat);
        }
    }
}
