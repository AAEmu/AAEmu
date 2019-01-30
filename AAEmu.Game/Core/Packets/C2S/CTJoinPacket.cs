using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTJoinPacket : StreamPacket
    {
        public CTJoinPacket() : base(0x01)
        {
        }

        public override void Read(PacketStream stream)
        {
            var accountId = stream.ReadUInt32();
            var cookie = stream.ReadUInt32();

            StreamManager.Instance.Login(Connection, accountId, cookie);
        }
    }
}