using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Request for the leadership ranking shown in the Hero window's "Candidates" tab.
/// </summary>
/// <remarks>
/// Answering matters even when the ranking comes back empty: hero_rank.lua:95 raises the loading
/// spinner before sending this and clears it only on HERO_RANK_DATA_RETRIEVED, so an unanswered request
/// hangs the tab forever.
///
/// TypeValue is the faction the tab is showing (X2Hero:RequestRankData(factionId)) and is echoed back so
/// the client routes the result to the right faction.
/// </remarks>
public class CSHeroRankingListPacket() : GamePacket(CSOffsets.CSHeroRankingListPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();

        var me = Connection?.ActiveChar;
        var rows = HeroManager.Instance.GetRanking((uint)TypeValue);

        Logger.Debug("HeroRankingList faction {0}: {1} entries", TypeValue, rows.Count);
        Connection?.SendPacket(new SCHeroRankingListPacket(
            TypeValue, me?.AccumulatedLeadershipPoint ?? 0, me?.LeadershipPoint ?? 0, rows));
    }
}
