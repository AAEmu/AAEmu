using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellHousePacket : GamePacket
    {
        public CSSellHousePacket() : base(CSOffsets.CSSellHousePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var moneyAmount = stream.ReadInt32();
            var sellTo = stream.ReadString();
            var isPublic = stream.ReadBoolean();

            _log.Debug("SellHouse, Tl: {0}, MoneyAmount: {1}, SellTo: {2}, isPublic: {3}",
                tl, moneyAmount, sellTo, isPublic);
        }
    }
}
