using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDepositMoneyPacket : GamePacket
    {
        public CSDepositMoneyPacket() : base(0x047, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var amount = stream.ReadInt32();
            var aapoint = stream.ReadInt32();

            _log.Debug("DepositMoney: amount -> {0}, aa_point -> {1}", amount, aapoint);

            DbLoggerCategory.Database.Connection.ActiveChar.ChangeMoney(SlotType.Inventory, SlotType.Bank, amount);
        }
    }
}
