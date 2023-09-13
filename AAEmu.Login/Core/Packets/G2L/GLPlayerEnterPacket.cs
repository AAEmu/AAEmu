using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.G2L
{
    public class GLPlayerEnterPacket : InternalPacket
    {
        public GLPlayerEnterPacket() : base(GLOffsets.GLPlayerEnterPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var connectionId = stream.ReadUInt32();
            var gsId = stream.ReadByte();
            var result = stream.ReadByte();

            var connection = LoginConnectionTable.Instance.GetConnection(connectionId);
            GameController.Instance.EnterWorld(connection, gsId, result);
        }
    }
}
