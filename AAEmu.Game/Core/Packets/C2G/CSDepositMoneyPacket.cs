using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDepositMoneyPacket : GamePacket
{
    public CSDepositMoneyPacket() : base(CSOffsets.CSDepositMoneyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var amount = stream.ReadInt32();
        var aapoint = stream.ReadInt32();

        Logger.Debug("DepositMoney: amount -> {0}, aa_point -> {1}", amount, aapoint);

        Connection.ActiveChar.ChangeMoney(SlotType.Bag, SlotType.Bank, amount);
    }
}
