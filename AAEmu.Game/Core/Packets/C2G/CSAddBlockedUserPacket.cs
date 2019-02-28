using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddBlockedUserPacket : GamePacket
    {
        public CSAddBlockedUserPacket() : base(0x107, 1) // TODO 1.0 opcode: 0x104
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            _log.Warn("AddBlockedUser, {0}", name);
            Connection.ActiveChar.Blocked.AddBlockedUser(name);
        }
    }
}
