using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Hero;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A submitted ballot from the Hero Vote window.
/// </summary>
/// <remarks>
/// The body is a SET, not a single choice. The client's writer (.text 0xac9330) serialises a field it
/// names charIdSet through the collection writer at 0xac5880, which emits "Size" as an i32 and then one
/// u64 per element, and follows the set with a trailing u64 it calls "type".
///
/// That matches the window: it is multi-select, and election.lua:129 only enables Vote while the tick
/// count is between 1 and X2Hero:GetFactionHeroCount() - 6 for Nuia and Haranya, 3 for the Pirates.
/// This used to read one u64 and drop it, which would have mis-parsed every ballot ever sent.
///
/// The trailing u64 is read and ignored. Its meaning is not established, and nothing needed so far
/// depends on it; the picks are what the election runs on.
/// </remarks>
public class CSHeroVotingPacket() : GamePacket(CSOffsets.CSHeroVotingPacket, 1)
{
    /// <summary>A ballot cannot sensibly exceed the largest nation's seat count; this only bounds a
    /// crafted packet, since Vote() rejects anything over the voter's own nation's allowance.</summary>
    private const int MaxPicks = 64;

    public override void Read(PacketStream stream)
    {
        var size = stream.ReadInt32();
        var picks = new List<ulong>(Math.Clamp(size, 0, MaxPicks));

        for (var i = 0; i < size && i < MaxPicks; i++)
            picks.Add(stream.ReadUInt64());

        var voter = Connection?.ActiveChar;
        if (voter == null)
            return;

        var result = HeroElectionManager.Instance.Vote(voter, picks);
        if (result != HeroElectionManager.VoteResult.Ok)
        {
            voter.SendMessage(HeroElectionManager.Explain(result));
            Logger.Debug("HeroVoting from {0} refused: {1}", voter.Name, result);
            return;
        }

        // Tells the client the ballot landed. Bit 2 is what opens the window (.text 0x1085b2), so it is
        // deliberately not set here - the vote is finished, and reopening the ballot on submission would
        // be the opposite of what the player asked for.
        voter.SendPacket(new SCHeroVotingPacket((int)HeroElectionManager.Season, 1));
    }
}
