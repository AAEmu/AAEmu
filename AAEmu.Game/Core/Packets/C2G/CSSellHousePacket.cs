using System.IO;

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
        // isPublic is the last byte of the body and is read through the indexer rather than through
        // the read API, so nothing at the stream level can see a body that ends before it. The
        // bound is written out here for that reason.
        var isPublic = stream.LeftBytes >= 1 && stream.Buffer[stream.Pos + stream.LeftBytes - 1] != 0;
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

        // moneyAmount degrades to 0 on a short read and the 0 branch below is CancelForSale, so a
        // body that lost its price would take a live listing down instead of doing nothing.
        // Overran has to be tested before that branch: LeftBytes cannot tell "the client sent 0"
        // from "the price never arrived", because the position is pinned at the end either way.
        if (stream.Overran)
            throw new InvalidDataException("SellHouse: truncated body");

        // Character select still dispatches this packet. With no character it must not fall through
        // to the command clear, which accepts a null caller.
        if (Connection.ActiveChar == null)
            return;

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


