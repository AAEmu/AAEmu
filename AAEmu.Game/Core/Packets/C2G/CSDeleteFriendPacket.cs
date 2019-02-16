using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteFriendPacket : GamePacket
    {
        public CSDeleteFriendPacket() : base(0x105, 1) // 0x102
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();

            _log.Info("CSDeleteFriendPacket, {0}", name);
            Connection.ActiveChar.Friends.RemoveFriend(name);
        }
    }
}
