using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.G2L
{
    public class GLLoadPacket : InternalPacket
    {
        public GLLoadPacket() : base(0x03)
        {
        }

        public override void Read(PacketStream stream)
        {
            var gsId = stream.ReadByte();
            var load = stream.ReadByte();

            GameController.Instance.SetLoad(gsId, load);
        }
    }
}