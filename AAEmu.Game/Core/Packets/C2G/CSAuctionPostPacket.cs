using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionPostPacket() : GamePacket(CSOffsets.CSAuctionPostPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var itemId = stream.ReadUInt64();
        var startPrice = stream.ReadInt64();
        var buyoutPrice = stream.ReadInt64();
        var duration = (AuctionDuration)stream.ReadByte();
        var minStack = stream.ReadInt32();
        var maxStack = stream.ReadInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var minimumLevel = AppConfiguration.Instance.LevelRestrictions.AuctionPostLevel;
        if (character.Level + character.HeirLevel < minimumLevel)
        {
            Logger.Warn("Rejected auction post from {0}: total level {1} is below {2}",
                character.Name, character.Level + character.HeirLevel, minimumLevel);
            return;
        }

        AuctionManager.Instance.PostLotOnAuction(character, itemId, startPrice, buyoutPrice, duration, minStack, maxStack);
    }
}
