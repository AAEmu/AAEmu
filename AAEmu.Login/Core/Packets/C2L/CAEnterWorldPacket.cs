using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CAEnterWorldPacket : LoginPacket
    {
        public CAEnterWorldPacket() : base(0x0b)
        {
        }

        public override void Read(PacketStream stream)
        {
            var flag = stream.ReadUInt64();
            var gsId = stream.ReadByte();

            GameController.Instance.RequestEnterWorld(Connection, gsId);
        }
    }
}