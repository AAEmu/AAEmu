using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCLevelRestrictionConfigPacket(LevelRestrictionConfig restrictions)
    : GamePacket(SCOffsets.SCLevelRestrictionConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        ArgumentNullException.ThrowIfNull(restrictions);

        stream.Write(restrictions.AuctionSearchLevel);
        stream.Write(restrictions.AuctionBidLevel);
        stream.Write(restrictions.AuctionPostLevel);
        stream.Write(restrictions.TradeLevel);
        stream.Write(restrictions.MailLevel);
        stream.Write(restrictions.PermissionLevel);
        stream.Write(restrictions.OtherLevel);

        var chat = restrictions.Chat ?? new ChatLevelRestrictionConfig();
        stream.Write(chat.White);
        stream.Write(chat.Shout);
        stream.Write(chat.Trade);
        stream.Write(chat.GroupFind);
        stream.Write(chat.Party);
        stream.Write(chat.Raid);
        stream.Write(chat.Region);
        stream.Write(chat.Clan);
        stream.Write(chat.System);
        stream.Write(chat.Family);
        stream.Write(chat.RaidLeader);
        stream.Write(chat.Judge);
        stream.Write(chat.Reserved12);
        stream.Write(chat.Reserved13);
        stream.Write(chat.Ally);
        stream.Write(chat.User);
        stream.Write(chat.Reserved16);
        stream.Write(chat.Reserved17);
        stream.Write(chat.Reserved18);
        stream.Write(chat.Reserved19);
        return stream;
    }
}
