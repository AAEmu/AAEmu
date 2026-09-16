using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the character's own lines in the rankings, which is what the ranking window opens with.
/// </summary>
/// <remarks>
/// The packet has no body. Every parameterless C2S type folds onto that one function, so a shared
/// serializer here is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSRankPersonalDataPacket() : GamePacket(CSOffsets.CSRankPersonalDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var entries = new List<RankingEntryLine>();

        // The boards measured by gear score are the ones the World can fill from live state: the score is
        // already computed for instance entry and squad limits. Every other board measures something the
        // World does not track yet (fish lengths, battlefield score, instance records), so they are left
        // out rather than sent as zeroes.
        foreach (var board in RankingGameData.Instance.BoardsMeasuring(RankingGameData.GearScoreDetailType))
        {
            entries.Add(new RankingEntryLine(board.Id, new RankingEntry
            {
                V1 = character.GearScore,
                V2 = 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                WorldId = (byte)character.Transform.WorldId,
                Id = character.Id,
                AccountId = character.AccountId,
                Type = board.Id,
                PrivacyStatus = 0,
                IsAllocated = true
            }));
        }

        character.SendPacket(new SCRankPersonalDataPacket(0, entries));
        Logger.Info("Rankings: sent {0} gear score line(s) to {1}", entries.Count, character.Name);
    }
}
