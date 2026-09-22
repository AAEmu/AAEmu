using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellHousePacket() : GamePacket(CSOffsets.CSSellHousePacket, 1)
{
    public override void Read(PacketStream stream)
    {

        // Fix: wire is u16 tl + u64 moneyAmount + string sellTo + bool isPublic
        // (client serializer; was u32 + missing bool, which desynced sellTo/isPublic)
        var tl = stream.ReadUInt16();
        var moneyAmount = stream.ReadUInt64();
        string sellTo = string.Empty;
        var isPublic = stream.Buffer[stream.Pos + stream.LeftBytes - 1] != 0;
        if (stream.LeftBytes >= 4)
        {
            var nameLen = stream.Buffer[stream.Pos] | (stream.Buffer[stream.Pos + 1] << 8);
            if (2 + nameLen + 1 == stream.LeftBytes)
            {
                stream.ReadUInt16();
                sellTo = nameLen > 0 ? stream.ReadString(nameLen) : string.Empty;
            }
        }
        while (stream.HasBytes)
            stream.ReadByte();

        Logger.Debug("SellHouse, Tl: {0}, MoneyAmount: {1}, SellTo: {2}, IsPublic: {3}", tl, moneyAmount, sellTo, isPublic);

        // Get buyer Id
        var sellToId = 0u;
        if (!string.IsNullOrEmpty(sellTo))
        {
            sellToId = NameManager.Instance.GetCharacterId(sellTo);
            if (sellToId <= 0)
            {
                // Invalid buyer specified
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotSellAsDesignatedBuyerNotFound);
                return;
            }
        }

        if (moneyAmount > uint.MaxValue)
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }
        if (moneyAmount > 0)
        {
            // TODO(Phase 2): persist isPublic (sell_public column) for the property listing
            HousingManager.Instance.SetForSale(tl, (uint)moneyAmount, sellToId, Connection.ActiveChar, isPublic);
        }
        else
            HousingManager.Instance.CancelForSale(tl, Connection.ActiveChar, true);
    }
}


