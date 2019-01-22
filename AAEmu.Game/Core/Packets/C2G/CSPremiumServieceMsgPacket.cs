using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServieceMsgPacket : GamePacket
    {
        public CSPremiumServieceMsgPacket() : base(0x136, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var stage = stream.ReadInt32();
            Connection.SendPacket(new SCAccountWarnedPacket(2, "Срок действия премиум-подписки истекает в ..."));
        }
    }
}