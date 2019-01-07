using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.G2L
{
    public class GLRegisterGameServerPacket : InternalPacket
    {
        public GLRegisterGameServerPacket() : base(0x00)
        {
        }

        public override void Read(PacketStream stream)
        {
            var gsId = stream.ReadByte();
            var ip = stream.ReadString();
            var port = stream.ReadUInt16();
            GameController.Instance.Add(gsId, ip, port, Connection);
        }
    }
}