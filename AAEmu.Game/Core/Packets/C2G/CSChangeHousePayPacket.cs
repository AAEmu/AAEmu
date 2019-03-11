using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeHousePayPacket : GamePacket
    {
        public CSChangeHousePayPacket() : base(0x05b, 1) //TODO 1.0 opcode: 0x059
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var moneyAmount = stream.ReadInt32();

            _log.Debug("ChangeHousePay, Tl: {0}, MoneyAmount: {1}", tl, moneyAmount);
        }
    }
}
