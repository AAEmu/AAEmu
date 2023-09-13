using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.L2G
{
    public class LGPlayerReconnectPacket : LoginPacket
    {
        public LGPlayerReconnectPacket() : base(LGOffsets.LGPlayerReconnectPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var accountId = stream.ReadUInt32();
            var connection = GameConnectionTable.Instance.GetConnection(accountId);
            connection?.SendPacket(new SCReconnectAuthPacket(connection.Id));
        }
    }
}
