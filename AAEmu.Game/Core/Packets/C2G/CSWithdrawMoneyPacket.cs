using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSWithdrawMoneyPacket : GamePacket
    {
        public CSWithdrawMoneyPacket() : base(0x048, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var amount = stream.ReadInt32();
            var aapoint = stream.ReadInt32();

            _log.Debug("WithdrawMoney: amount -> {0}, aa_point -> {1}", amount, aapoint);

            DbLoggerCategory.Database.Connection.ActiveChar.ChangeMoney(SlotType.Bank, SlotType.Inventory, amount);
        }
    }
}
