using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSJoinUserChatChannelPacket : GamePacket
    {
        public CSJoinUserChatChannelPacket() : base(CSOffsets.CSJoinUserChatChannelPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            var pwd = stream.ReadString();
            var create = stream.ReadBoolean();

            _log.Debug("JoinUserChatChannel, Name: {0}, Password: {1}, Create: {2}", name, pwd, create);
        }
    }
}
