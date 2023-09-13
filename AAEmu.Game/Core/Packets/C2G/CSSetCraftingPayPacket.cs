using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetCraftingPayPacket : GamePacket
    {
        public CSSetCraftingPayPacket() : base(CSOffsets.CSSetCraftingPayPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var moneyAmount = stream.ReadInt32();

            _log.Warn("SetCraftingPay, ObjId: {0}, MoneyAmount: {1}", objId, moneyAmount);
        }
    }
}
