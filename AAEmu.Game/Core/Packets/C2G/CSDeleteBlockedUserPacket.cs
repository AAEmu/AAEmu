using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteBlockedUserPacket : GamePacket
    {
        public CSDeleteBlockedUserPacket() : base(CSOffsets.CSDeleteBlockedUserPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();

            _log.Warn("CSDeleteBlockedUserPacket, {0}", name);
            Connection.ActiveChar.Blocked.RemoveBlockedUser(name);
        }
    }
}
