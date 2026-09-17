using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
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

        // The character's own lines come from where the boards are kept, which is what the window's
        // "current record" reads: the same figures the board itself is built from. They all travel in one
        // answer, named for the character they are about — the client takes an answer for the character it
        // is playing and files each line it carries under the line's own board.
        var entries = RankScoreManager.Instance.PersonalLines(character);
        character.SendPacket(new SCRankPersonalDataPacket(character.Id, entries));

        Logger.Info("Rankings: sent {0} personal line(s) to {1} over {2} board(s)",
            entries.Count, character.Name, entries.Select(line => line.Key).Distinct().Count());
    }

    /// <summary>
    /// One board's line for a character. The second value is left at zero: the boards' secondary figure
    /// (the item's own score, the period total) has no source in the World yet, and a zero says so.
    /// </summary>
    private static RankingEntryLine Line(Character character, uint boardId, long score)
    {
        return new RankingEntryLine(boardId, new RankingEntry
        {
            V1 = score,
            V2 = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            WorldId = (byte)character.Transform.WorldId,
            Id = character.Id,
            AccountId = character.AccountId,
            Type = boardId,
            PrivacyStatus = 0
        });
    }
}
