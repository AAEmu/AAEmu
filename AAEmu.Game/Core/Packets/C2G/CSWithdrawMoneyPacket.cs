using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSWithdrawMoneyPacket() : GamePacket(CSOffsets.CSWithdrawMoneyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var amount = stream.ReadUInt64();
        var aaPoint = stream.ReadUInt64();

        Logger.Debug("WithdrawMoney: amount -> {0}, aa_point -> {1}", amount, aaPoint);

        if (amount > long.MaxValue || aaPoint > long.MaxValue)
        {
            Connection.ActiveChar.SendErrorMessage(Models.Game.ErrorMessageType.Invalid);
            return;
        }

        Connection.ActiveChar.ChangeWallets(
            SlotType.Bank,
            SlotType.Inventory,
            (long)amount,
            (long)aaPoint,
            Models.Game.Items.Actions.ItemTaskType.WithdrawMoney);
    }
}
