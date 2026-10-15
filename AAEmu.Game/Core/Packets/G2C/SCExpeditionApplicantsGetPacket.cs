using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answers manage-applicants. Client deserializer always reads <see cref="SlotCount"/> fixed
/// applicant rows after total/count — short bodies desync the SC stream and the window stays empty.
/// </summary>
public sealed class SCExpeditionApplicantsGetPacket(
    IReadOnlyList<(ExpeditionRecruitmentApplication Application, ExpeditionJoinCandidate Character)> rows)
    : GamePacket(SCOffsets.SCExpeditionApplicantsGetPacket, 1)
{
    /// <summary>Client packet body always walks this many applicant structs.</summary>
    public const int SlotCount = 50;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(rows.Count, SlotCount);
        stream.Write((uint)rows.Count);
        stream.Write((uint)count);
        for (var i = 0; i < SlotCount; i++)
        {
            if (i < count)
                WriteRow(stream, rows[i].Application, rows[i].Character);
            else
                WriteEmptyRow(stream);
        }

        return stream;
    }

    private static void WriteRow(PacketStream stream, ExpeditionRecruitmentApplication application,
        ExpeditionJoinCandidate character)
    {
        stream.Write((ulong)character.CharacterId);
        stream.Write(character.Name ?? string.Empty);
        stream.Write(character.Level);
        stream.Write(character.HeirLevel);
        stream.Write((int)character.FactionId);
        stream.Write(application.Memo ?? string.Empty);
        // Applicant registration time is Unix seconds (same clock as the recruit board).
        stream.Write(Helpers.UnixTime(ServerCalendar.AsUtc(application.RegisteredAt)));
    }

    private static void WriteEmptyRow(PacketStream stream)
    {
        stream.Write(0ul);
        stream.Write(string.Empty);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write(0);
        stream.Write(string.Empty);
        stream.Write(0L);
    }
}
