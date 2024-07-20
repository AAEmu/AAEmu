using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeHousePayPacket : GamePacket
{
    public CSChangeHousePayPacket() : base(CSOffsets.CSChangeHousePayPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = stream.ReadUInt16();
        var moneyAmount = stream.ReadInt32();

        Logger.Debug("ChangeHousePay, Tl: {0}, MoneyAmount: {1}", tl, moneyAmount);
    }
}
